using System.Diagnostics.Metrics;
using System.Text.Json;

namespace ArchitectureGuardian.Analysis.Telemetry;

/// <summary>Collects local analysis metrics as JSON lines without an exporter, service or network connection.</summary>
/// <remarks>The caller owns the writer. Source paths, source text and exception messages are never recorded.</remarks>
public sealed class LocalTelemetryListener : IDisposable
{
    private readonly MeterListener _listener;
    private readonly TextWriter _output;
    private readonly object _gate = new();

    /// <summary>Starts collecting analysis measurements until disposed.</summary>
    /// <param name="output">A local destination, normally stderr; stdout must remain available to reports or MCP.</param>
    public LocalTelemetryListener(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AnalysisTelemetry.SourceName) { listener.EnableMeasurementEvents(instrument); }
            },
        };
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Write(instrument, value, tags));
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Write(instrument, value, tags));
        _listener.Start();
    }

    /// <summary>Enables local collection only when ARCHGUARD_TELEMETRY is exactly 1.</summary>
    /// <param name="output">The caller's stderr or another explicitly selected local writer.</param>
    /// <returns>An owned listener, or null when collection is disabled.</returns>
    public static LocalTelemetryListener? FromEnvironment(TextWriter output) =>
        Environment.GetEnvironmentVariable("ARCHGUARD_TELEMETRY") == "1" ? new(output) : null;

    /// <summary>Stops collection without closing the caller's writer.</summary>
    public void Dispose() => _listener.Dispose();

    private void Write<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var attributes = new Dictionary<string, object?>();
        foreach (var tag in tags) { attributes.Add(tag.Key, tag.Value); }
        lock (_gate)
        {
            // A closed local sink must not change coverage, findings or cancellation semantics.
            try
            {
                _output.WriteLine(JsonSerializer.Serialize(new
                {
                    source = AnalysisTelemetry.SourceName,
                    instrument = instrument.Name,
                    value,
                    unit = instrument.Unit,
                    tags = attributes,
                }));
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
