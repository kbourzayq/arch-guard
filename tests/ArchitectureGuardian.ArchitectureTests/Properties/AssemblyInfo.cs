using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using Xunit;
using Xunit.v3;

// Each workspace scenario can start multiple MSBuild/Roslyn processes on shared runners.
[assembly: Parallelization(MaxThreads = 2)]
[assembly: TestCollectionOrderer(typeof(WorkspaceCollectionOrderer))]
