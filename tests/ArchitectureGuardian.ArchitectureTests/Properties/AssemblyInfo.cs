using Xunit;

// Each workspace scenario can start multiple MSBuild/Roslyn processes on shared runners.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]
