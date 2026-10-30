# Telemetry integration for embedding hosts

## Collect through OpenTelemetry

The adapter uses runtime `ActivitySource` and `Meter` APIs, following the
[.NET tracing guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs)
and [metrics guidance](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation).
It adds no runtime packages or exporter configuration. Embedding hosts can use an
`ActivityListener`/`MeterListener`, or register
`AddSource("ArchitectureGuardian.Analysis")` and
`AddMeter("ArchitectureGuardian.Analysis")` in their own OpenTelemetry SDK providers.
No remote exporter is required or enabled. The environment variable controls the
built-in local listener; external listeners are the embedding host's separate
opt-in. With no listeners, instrumentation skips clocks and activity/tag creation.
Activities use the same phase names/tags and preserve parent spans across awaits.

Instrumentation does not cache or skip rules, change ordering, alter cancellation
tokens or narrow comparison contexts. Tests cover metrics, tracing, outcomes,
cancellation, deterministic reports and instrumented full-scan/Git-comparison
agreement. The normal semantic and comparison suite remains required.
