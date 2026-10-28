using System.Security.Cryptography;
using System.Text.Json;
using ArchitectureGuardian.Analysis.Dependencies;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Policies;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace ArchitectureGuardian.Performance;

/// <summary>Measures prepared-input workspace, graph, rule and complete-scan costs with standard diagnostics.</summary>
[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5, invocationCount: 1)]
public class AnalysisBenchmarks
{
    private WorkspaceLoadRequest _request = null!;
    private ArchitecturePolicy _policy = null!;
    private string _directory = null!;
    private string _manifestDirectory = null!;
    private string _fingerprint = null!;
    private WorkspaceLoadResult _workspace = null!;
    private DependencyBuildResult _graph = null!;
    private AnalysisResult? _lastAnalysis;
    private DependencyBuildResult? _lastGraph;

    /// <summary>Gets or sets the reproducible repository shape supplied by the preparation script.</summary>
    [Params("CleanArchitecture", "ModularMonolith", "Generated24x40")]
    public string Input { get; set; } = "CleanArchitecture";

    /// <summary>Prepares immutable contexts and verifies deterministic results outside measurement.</summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        var manifest = Environment.GetEnvironmentVariable("ARCHGUARD_BENCHMARK_INPUTS")
            ?? throw new InvalidOperationException("Run this suite through eng/benchmark.py to prepare inputs.");
        _manifestDirectory = Path.GetDirectoryName(manifest)!;
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifest));
        var paths = document.RootElement.GetProperty(Input);
        var policyPath = paths.GetProperty("policy").GetString()!;
        _directory = Path.GetDirectoryName(policyPath)!;
        _request = new(paths.GetProperty("input").GetString()!, true, "Release");
        _policy = await new YamlPolicyLoader().LoadAsync(policyPath);
        _workspace = await new MSBuildWorkspaceLoader().LoadAsync(_request);
        _graph = await new DependencyGraphBuilder().BuildAsync(_workspace, _directory, _policy);
        var first = await EvaluateAsync();
        _fingerprint = Fingerprint(first);
        if (_fingerprint != Fingerprint(await EvaluateAsync())) { throw new InvalidOperationException("Analysis is nondeterministic."); }
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
    public async Task<AnalysisResult> Rules() => _lastAnalysis = await EvaluateAsync();

    /// <summary>Runs the public full-scan service, including owned workspace disposal.</summary>
    /// <returns>The deterministic analysis result, consumed by BenchmarkDotNet.</returns>
    [Benchmark]
    public async Task<AnalysisResult> FullScan() =>
        _lastAnalysis = await new SolutionAnalysisService().AnalyzeAsync(_request, _directory, _policy);

    /// <summary>Checks measured outputs and releases the prepared workspace outside measurement.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (_lastAnalysis is not null && Fingerprint(_lastAnalysis) != _fingerprint)
            {
                throw new InvalidOperationException("Measured analysis differs from its complete prepared context.");
            }
            if (_lastGraph is not null && (_lastGraph.Status != AnalysisStatus.Completed
                || _lastGraph.Graph?.Edges.Count != _graph.Graph?.Edges.Count))
            {
                throw new InvalidOperationException("Measured graph coverage or size changed.");
            }
            var validation = JsonSerializer.Serialize(new
            {
                fingerprint = _fingerprint,
                projects = _graph.Repository!.Projects.Count,
                nodes = _graph.Graph!.Nodes.Count,
                edges = _graph.Graph.Edges.Count,
            });
            var path = Path.Combine(_manifestDirectory, Input + ".validation.json");
            if (File.Exists(path) && File.ReadAllText(path) != validation)
            {
                throw new InvalidOperationException("Benchmark processes disagree on analysis results.");
            }
            File.WriteAllText(path, validation);
        }
        finally { _workspace.Dispose(); }
    }

    private Task<AnalysisResult> EvaluateAsync() => new AnalysisService<DependencyBuildResult>(_policy.SelectRules(SolutionRules.All))
        .AnalyzeAsync(new(_graph, _policy.SeverityPolicy));

    private static string Fingerprint(AnalysisResult result)
    {
        if (!result.IsComplete) { throw new InvalidOperationException(string.Join('\n', result.Issues.Select(issue => issue.Message))); }
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(result)));
    }
}
