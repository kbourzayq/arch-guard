using Xunit.v3;

// Each workspace scenario can start multiple MSBuild/Roslyn processes on shared runners.
[assembly: Parallelization(MaxThreads = 2)]
