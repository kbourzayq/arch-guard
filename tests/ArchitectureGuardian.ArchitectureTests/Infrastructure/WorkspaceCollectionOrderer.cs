using ArchitectureGuardian.ArchitectureTests.Changes;
using ArchitectureGuardian.ArchitectureTests.Cli;
using ArchitectureGuardian.ArchitectureTests.ReferenceSolutions;
using Xunit.Sdk;
using Xunit.v3;

namespace ArchitectureGuardian.ArchitectureTests.Infrastructure;

/// <summary>Starts the longest workspace collections first so bounded workers do not finish with an idle tail.</summary>
/// <remarks>Priorities follow measured full-suite durations; they change scheduling, never isolation or concurrency.</remarks>
public sealed class WorkspaceCollectionOrderer : ITestCollectionOrderer
{
    /// <inheritdoc />
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : ITestCollection => testCollections
        .OrderBy(collection => collection.TestCollectionDisplayName switch
        {
            nameof(ChangeAnalysisTests) => 0,
            nameof(ReferenceSolutionTests) => 1,
            nameof(CliWorkspaceTests) => 2,
            _ => 3,
        })
        .ThenBy(collection => collection.TestCollectionDisplayName, StringComparer.Ordinal)
        .ToArray();
}
