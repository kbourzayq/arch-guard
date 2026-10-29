using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Policies;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace ArchitectureGuardian.Benchmarks;

/// <summary>Measures prepared-input workspace, graph, rule and complete-scan costs with standard diagnostics.</summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn, MedianColumn]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 3, iterationCount: 10, invocationCount: 1)]
public class AnalysisBenchmarks
{
    private WorkspaceLoadRequest _request = null!;
    private ArchitecturePolicy _policy = null!;
    private string _directory = null!;
    private AnalysisResult _expected = null!;
    private WorkspaceLoadResult _workspace = null!;
    private DependencyBuildResult _graph = null!;
    private AnalysisResult? _lastAnalysis;
    private DependencyBuildResult? _lastGraph;

    /// <summary>Gets or sets the reproducible repository shape prepared by the C# entry point.</summary>
    [Params("CleanArchitecture", "ModularMonolith", "Generated24x40")]
    public string Input { get; set; } = "CleanArchitecture";

    /// <summary>Prepares immutable contexts and verifies deterministic results outside measurement.</summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        var root = Environment.GetEnvironmentVariable(BenchmarkInputs.EnvironmentVariable)
            ?? throw new InvalidOperationException("Run the benchmark project's .NET entry point to prepare inputs.");
        _directory = Path.Combine(root, Input);
        var policyPath = Path.Combine(_directory, "architecture.guardian.yaml");
        _request = new(Path.Combine(_directory, Input + ".slnx"), true, "Release");
        _policy = await new YamlPolicyLoader().LoadAsync(policyPath);
        if (_policy.SelectRules(SolutionRules.All).Count != 6) { throw new InvalidOperationException("Benchmark inputs must select all six rules."); }
        _workspace = await new MSBuildWorkspaceLoader().LoadAsync(_request);
        _graph = await new DependencyGraphBuilder().BuildAsync(_workspace, _directory, _policy);
        _expected = await EvaluateAsync(_graph);
        VerifyAnalysis(_expected);
        VerifyAnalysis(await EvaluateAsync(_graph));
    }

    /// <summary>Loads and disposes a fresh workspace, including compilation coverage validation.</summary>
    /// <returns>The complete project count, consumed by BenchmarkDotNet.</returns>
    [Benchmark]
    public async Task<int> Workspace()
    {
        using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(_request);
        if (workspace.Status != AnalysisStatus.Completed) { throw new InvalidOperationException("Workspace coverage is incomplete."); }
        return workspace.Solution!.ProjectIds.Count;
    }

    /// <summary>Normalizes and extracts a fresh graph from a loaded workspace.</summary>
    /// <returns>The graph and coverage, consumed by BenchmarkDotNet.</returns>
    [Benchmark]
    public async Task<DependencyBuildResult> Graph() =>
        _lastGraph = await new DependencyGraphBuilder().BuildAsync(_workspace, _directory, _policy);

    /// <summary>Evaluates all selected rules against a prepared graph.</summary>
    /// <returns>The deterministic analysis result, consumed by BenchmarkDotNet.</returns>
    [Benchmark]
    public async Task<AnalysisResult> Rules() => _lastAnalysis = await EvaluateAsync(_graph);

    /// <summary>Runs the public full-scan service, including owned workspace disposal.</summary>
    /// <returns>The deterministic analysis result, consumed by BenchmarkDotNet.</returns>
    [Benchmark]
    public async Task<AnalysisResult> FullScan() =>
        _lastAnalysis = await new SolutionAnalysisService().AnalyzeAsync(_request, _directory, _policy);

    /// <summary>Rejects incomplete or changed results after every warmup/measured invocation, outside timing.</summary>
    [IterationCleanup]
    public void VerifyIteration()
    {
        if (_lastAnalysis is not null) { VerifyAnalysis(_lastAnalysis); }
        if (_lastGraph is not null)
        {
            if (_lastGraph.Status != AnalysisStatus.Completed
                || _lastGraph.Graph is not { } actual || _graph.Graph is not { } expected
                || !actual.Nodes.Select(node => node.Id).SequenceEqual(expected.Nodes.Select(node => node.Id))
                || !actual.Edges.Select(edge => edge.Id).SequenceEqual(expected.Edges.Select(edge => edge.Id)))
            {
                throw new InvalidOperationException("Measured graph coverage or size changed.");
            }
        }
        _lastAnalysis = null;
        _lastGraph = null;
    }

    /// <summary>Releases prepared workspace resources without adding disposal to graph/rule measurements.</summary>
    [GlobalCleanup]
    public void Cleanup() => _workspace?.Dispose();

    private Task<AnalysisResult> EvaluateAsync(DependencyBuildResult graph) =>
        new AnalysisService<DependencyBuildResult>(_policy.SelectRules(SolutionRules.All)).AnalyzeAsync(new(graph, _policy.SeverityPolicy));

    private void VerifyAnalysis(AnalysisResult result)
    {
        if (!result.IsComplete) { throw new InvalidOperationException(string.Join('\n', result.Issues.Select(issue => issue.Message))); }
        if (!result.Diagnostics.Select(diagnostic => (diagnostic.Id, diagnostic.Severity))
                .SequenceEqual(_expected.Diagnostics.Select(diagnostic => (diagnostic.Id, diagnostic.Severity))))
        {
            throw new InvalidOperationException("Analysis differs from its complete prepared context.");
        }
    }
}
