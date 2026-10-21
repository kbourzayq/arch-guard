using Xunit;

// Bound concurrent workspace fixture preparation and semantic analysis on shared runners.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]
