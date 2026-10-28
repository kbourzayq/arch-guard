using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Changes;

/// <summary>Isolates the process-wide metric listener while its output is inspected.</summary>
[CollectionDefinition("Telemetry", DisableParallelization = true)]
public sealed class TelemetryCollection;
