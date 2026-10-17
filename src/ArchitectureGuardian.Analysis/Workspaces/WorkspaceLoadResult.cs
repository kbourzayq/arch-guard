using ArchitectureGuardian.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Owns a workspace and its loading coverage; dispose it after consuming its solution.</summary>
/// <remarks>Roslyn objects remain inside the Analysis adapter and must be normalized before Core evaluation.</remarks>
public sealed class WorkspaceLoadResult : IDisposable
{
    private readonly MSBuildWorkspace? _workspace;

    internal WorkspaceLoadResult(MSBuildWorkspace? workspace, string inputPath, AnalysisStatus status, IEnumerable<AnalysisIssue> issues)
    {
        _workspace = workspace;
        InputPath = inputPath;
        var coverage = new AnalysisResult(status, issues: issues.Distinct());
        Status = coverage.Status;
        Issues = coverage.Issues;
    }

    /// <summary>Gets the absolute solution or project input path; normalization makes it policy-relative.</summary>
    public string InputPath { get; }

    /// <summary>Gets loading coverage for the requested configuration and framework, independently of rule evaluation.</summary>
    public AnalysisStatus Status { get; }

    /// <summary>Gets deterministically ordered operational issues explaining any missing coverage.</summary>
    public IReadOnlyList<AnalysisIssue> Issues { get; }

    /// <summary>Gets the available, possibly partial solution; null means no workspace was created.</summary>
    public Solution? Solution => _workspace?.CurrentSolution;

    /// <summary>Releases workspace resources; the solution must no longer be used afterward.</summary>
    public void Dispose() => _workspace?.Dispose();
}
