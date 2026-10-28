using System.Diagnostics;
using System.Text.Json;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Telemetry;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Tests.Diagnostics;
using Xunit;

namespace ArchitectureGuardian.Tests.Analysis;

/// <summary>Checks observable metrics, tracing, opt-in behavior and unchanged analysis contracts.</summary>
[Collection("Telemetry")]
public sealed class TelemetryTests
{
    /// <summary>Measures one evaluation, preserves report bytes and exposes nested tracing without any exporter.</summary>
    [Fact]
    public async Task Metrics_and_traces_preserve_deterministic_results()
    {
        var service = Service(_ => Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Completed, [DiagnosticTests.Finding()])));
        var expected = await service.AnalyzeAsync(new("context"), TestContext.Current.CancellationToken);
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ArchitectureGuardian.Analysis",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        using var output = new StringWriter();
        using (var telemetry = new LocalTelemetryListener(output))
        {
            var actual = await service.AnalyzeAsync(new("context"), TestContext.Current.CancellationToken);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        }

        var measurements = Parse(output);
        var count = Assert.Single(measurements, item => item.GetProperty("instrument").GetString() == "archguard.rule.evaluations");
        Assert.Equal(1, count.GetProperty("value").GetInt64());
        Assert.Equal("AG001", count.GetProperty("tags").GetProperty("rule.id").GetString());
        Assert.All(measurements, item => Assert.Equal("completed", item.GetProperty("tags").GetProperty("status").GetString()));
        Assert.All(measurements.Where(item => item.GetProperty("unit").GetString() == "s"),
            item => Assert.True(item.GetProperty("value").GetDouble() >= 0));
        var rule = Assert.Single(activities, activity => activity.OperationName == "rule");
        var rules = Assert.Single(activities, activity => activity.OperationName == "rules");
        Assert.Equal(rules.SpanId, rule.ParentSpanId);
        Assert.Equal("AG001", rule.GetTagItem("rule.id"));
        Assert.DoesNotContain("context", output.ToString());
        var before = output.ToString();
        await service.AnalyzeAsync(new("context"), TestContext.Current.CancellationToken);
        Assert.Equal(before, output.ToString());
    }

    /// <summary>Records returned coverage outcomes without converting incomplete work to success.</summary>
    /// <param name="status">The rule's coverage status.</param>
    [Theory]
    [InlineData(AnalysisStatus.Completed)]
    [InlineData(AnalysisStatus.Incomplete)]
    [InlineData(AnalysisStatus.Failed)]
    [InlineData(AnalysisStatus.Cancelled)]
    public async Task Metrics_preserve_each_returned_outcome(AnalysisStatus status)
    {
        using var output = new StringWriter();
        using var telemetry = new LocalTelemetryListener(output);
        var result = await Service(_ => Task.FromResult(new RuleEvaluationResult(status,
                issues: status == AnalysisStatus.Incomplete ? [new("missing-context", "Test coverage gap")] : null)))
            .AnalyzeAsync(new("context"), TestContext.Current.CancellationToken);
        Assert.Equal(status, result.Status);
        Assert.All(Parse(output), item => Assert.Equal(status.ToString().ToLowerInvariant(),
            item.GetProperty("tags").GetProperty("status").GetString()));
    }

    /// <summary>Preserves cancellation and failure semantics, including rules which ignore their token.</summary>
    /// <param name="cancel">Whether to request cancellation rather than throw an unexpected failure.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Exceptions_and_mid_rule_cancellation_keep_their_outcome(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter();
        using var telemetry = new LocalTelemetryListener(output);
        var result = await Service(_ =>
        {
            if (!cancel) { throw new InvalidOperationException("private exception detail"); }
            cancellation.Cancel();
            return Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Completed));
        }).AnalyzeAsync(new("context"), cancellation.Token);
        Assert.Equal(cancel ? AnalysisStatus.Cancelled : AnalysisStatus.Failed, result.Status);
        Assert.All(Parse(output), item => Assert.Equal(cancel ? "cancelled" : "failed",
            item.GetProperty("tags").GetProperty("status").GetString()));
        Assert.DoesNotContain("private exception detail", output.ToString());
    }

    /// <summary>A pre-cancelled request never counts a rule which did not run.</summary>
    [Fact]
    public async Task Cancellation_before_evaluation_does_not_increment_rule_count()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new StringWriter();
        using var telemetry = new LocalTelemetryListener(output);
        var result = await Service(_ => throw new InvalidOperationException("Unexpected evaluation"))
            .AnalyzeAsync(new("context"), cancellation.Token);
        Assert.Equal(AnalysisStatus.Cancelled, result.Status);
        Assert.Equal("rules", Assert.Single(Parse(output)).GetProperty("tags").GetProperty("phase").GetString());
    }

    /// <summary>Collection requires an explicit environment value and never closes caller streams.</summary>
    /// <param name="value">The operator's environment setting.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("1")]
    public void Environment_collection_is_opt_in(string? value)
    {
        var previous = Environment.GetEnvironmentVariable("ARCHGUARD_TELEMETRY");
        try
        {
            Environment.SetEnvironmentVariable("ARCHGUARD_TELEMETRY", value);
            using var output = new StringWriter();
            var telemetry = LocalTelemetryListener.FromEnvironment(output);
            Assert.Equal(value == "1", telemetry is not null);
            telemetry?.Dispose();
            output.Write("still owned by caller");
        }
        finally { Environment.SetEnvironmentVariable("ARCHGUARD_TELEMETRY", previous); }
    }

    /// <summary>A failed local sink cannot turn a clean analysis into a coverage failure.</summary>
    [Fact]
    public async Task Closed_sink_does_not_change_analysis()
    {
        using var output = new StringWriter();
        using var telemetry = new LocalTelemetryListener(output);
        output.Dispose();
        Assert.True((await Service(_ => Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Completed)))
            .AnalyzeAsync(new("context"), TestContext.Current.CancellationToken)).IsComplete);
    }

    private static JsonElement[] Parse(StringWriter output) => output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();

    private static AnalysisService<string> Service(Func<CancellationToken, Task<RuleEvaluationResult>> evaluate) => new([new TestRule(evaluate)]);

    /// <summary>Supplies controlled rule outcomes without workspace integration.</summary>
    private sealed class TestRule(Func<CancellationToken, Task<RuleEvaluationResult>> evaluate) : IArchitectureRule<string>
    {
        /// <inheritdoc/>
        public RuleDescriptor Descriptor => RuleCatalog.ModuleDependencies;

        /// <inheritdoc/>
        public Task<RuleEvaluationResult> EvaluateAsync(string context, CancellationToken cancellationToken = default) => evaluate(cancellationToken);
    }
}
