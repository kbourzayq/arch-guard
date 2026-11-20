using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using Xunit;
using Xunit.v3;

// Each workspace scenario can start multiple MSBuild/Roslyn processes. CI shards this assembly
// across runners, so three collections per shard keep 4-vCPU runners busy without oversubscribing.
[assembly: Parallelization(MaxThreads = 3)]
[assembly: TestCollectionOrderer(typeof(WorkspaceCollectionOrderer))]
