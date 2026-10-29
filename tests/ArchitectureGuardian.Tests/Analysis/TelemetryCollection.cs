using Xunit;

namespace ArchitectureGuardian.Tests.Analysis;

/// <summary>Isolates process-wide telemetry listeners and environment changes from other tests.</summary>
[CollectionDefinition("Telemetry", DisableParallelization = true)]
public sealed class TelemetryCollection;
