using ArchitectureGuardian.ArchitectureTests.Changes;
using ArchitectureGuardian.ArchitectureTests.Cli;
using ArchitectureGuardian.ArchitectureTests.Mcp;
using ArchitectureGuardian.ArchitectureTests.ReferenceSolutions;
using Xunit.Sdk;
using Xunit.v3;

namespace ArchitectureGuardian.ArchitectureTests.Infrastructure;

/// <summary>Starts the longest workspace collections first so bounded workers do not finish with an idle tail.</summary>
/// <remarks>
/// Priorities follow measured full-suite durations, longest first; they change scheduling, never isolation or
/// concurrency. CI runs filtered shards of this assembly, and each shard keeps this relative order.
/// </remarks>
public sealed class WorkspaceCollectionOrderer : ITestCollectionOrderer
{
    private static readonly string[] LongestFirst =
    [
        nameof(ReferenceSolutionTests),
        nameof(CliDoctorTests),
        nameof(McpOnboardingWorkflowTests),
        nameof(CliOnboardingWorkflowTests),
        nameof(CliWorkspaceTests),
        nameof(ChangeAnalysisTests),
        nameof(ChangeSnapshotSafetyTests),
        nameof(CorrectionWorkflowTests),
        nameof(ChangeInventoryTests),
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection => testCollections
        .OrderBy(collection => Priority(collection.TestCollectionDisplayName))
        .ThenBy(collection => collection.TestCollectionDisplayName, StringComparer.Ordinal)
        .ToArray();

    private static int Priority(string displayName)
    {
        var index = Array.IndexOf(LongestFirst, displayName);
        return index < 0 ? LongestFirst.Length : index;
    }
}
