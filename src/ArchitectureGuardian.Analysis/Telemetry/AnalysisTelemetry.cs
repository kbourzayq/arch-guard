using System.Diagnostics;
using System.Diagnostics.Metrics;
using ArchitectureGuardian.Core.Analysis;

namespace ArchitectureGuardian.Analysis.Telemetry;

/// <summary>Publishes bounded, path-free analysis measurements through the runtime's OpenTelemetry APIs.</summary>
internal static class AnalysisTelemetry
{
    internal const string SourceName = "ArchitectureGuardian.Analysis";
    private static readonly ActivitySource Source = new(SourceName);
    private static readonly Meter Meter = new(SourceName);
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("archguard.phase.duration", "s");
    private static readonly Counter<long> Rules = Meter.CreateCounter<long>("archguard.rule.evaluations", "{evaluation}");

    internal static async Task<T> MeasureAsync<T>(string phase, Func<Task<T>> operation,
        Func<T, AnalysisStatus> getStatus, CancellationToken cancellationToken, string? ruleId = null)
    {
        // Avoid clocks, tags and activities when nobody is collecting this source.
        if (!Source.HasListeners() && !Duration.Enabled && (ruleId is null || !Rules.Enabled))
        {
            return await operation().ConfigureAwait(false);
        }

        using var activity = Source.StartActivity(phase);
        activity?.SetTag("phase", phase);
        if (ruleId is not null) { activity?.SetTag("rule.id", ruleId); }
        var start = Stopwatch.GetTimestamp();
        var status = AnalysisStatus.Failed;
        try
        {
            var result = await operation().ConfigureAwait(false);
            status = getStatus(result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = AnalysisStatus.Cancelled;
            throw;
        }
        finally
        {
            var outcome = status.ToString().ToLowerInvariant();
            activity?.SetTag("status", outcome);
            if (status == AnalysisStatus.Failed) { activity?.SetStatus(ActivityStatusCode.Error); }
            var tags = new TagList { { "phase", phase }, { "status", outcome } };
            if (ruleId is not null) { tags.Add("rule.id", ruleId); }
            Duration.Record(Stopwatch.GetElapsedTime(start).TotalSeconds, tags);
            if (ruleId is not null) { Rules.Add(1, tags); }
        }
    }
}
