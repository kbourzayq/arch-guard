using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Tests.Diagnostics;
using Xunit;

namespace ArchitectureGuardian.Tests.Analysis;

/// <summary>Verifies reusable orchestration, severity application, partial coverage and cancellation behavior.</summary>
public sealed class AnalysisServiceTests
{
    private const string Context = "neutral snapshot";

    /// <summary>Checks context/token forwarding and verifies that severity policy changes reporting rather than finding identity.</summary>
    [Fact]
    public async Task Service_passes_context_and_token_and_applies_policy_after_evaluation()
    {
        using var cancellation = new CancellationTokenSource();
        var finding = DiagnosticTests.Finding();
        var rule = new TestRule(RuleCatalog.ModuleDependencies, (context, token) =>
        {
            Assert.Same(Context, context);
            Assert.Equal(cancellation.Token, token);
            return Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Completed, [finding]));
        });
        IAnalysisService<string> service = new AnalysisService<string>([rule]);
        var warningPolicy = new SeverityPolicy(new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Warning });

        var warningResult = await service.AnalyzeAsync(new(Context, warningPolicy), cancellation.Token);
        var errorResult = await service.AnalyzeAsync(new(Context), cancellation.Token);

        Assert.True(warningResult.IsComplete);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(warningResult.Diagnostics).Severity);
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(errorResult.Diagnostics).Severity);
        Assert.Equal(warningResult.Diagnostics[0].Id, errorResult.Diagnostics[0].Id);
    }

    /// <summary>Checks that rule registration and finding enumeration order cannot alter execution order or result identities.</summary>
    [Fact]
    public async Task Registration_and_finding_order_do_not_change_results()
    {
        var calls = new List<string>();
        TestRule CreateRule(RuleDescriptor descriptor, bool reverse) => new(descriptor, (_, _) =>
        {
            calls.Add(descriptor.Id);
            var findings = new[] { DiagnosticTests.Finding(descriptor), DiagnosticTests.Finding(descriptor, new("Billing")) };
            return Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Completed, reverse ? findings.Reverse() : findings));
        });
        var first = await new AnalysisService<string>([CreateRule(RuleCatalog.ModuleCycles, false), CreateRule(RuleCatalog.ModuleDependencies, false)])
            .AnalyzeAsync(new(Context), TestContext.Current.CancellationToken);
        var second = await new AnalysisService<string>([CreateRule(RuleCatalog.ModuleDependencies, true), CreateRule(RuleCatalog.ModuleCycles, true)])
            .AnalyzeAsync(new(Context), TestContext.Current.CancellationToken);

        Assert.Equal(["AG001", "AG003", "AG001", "AG003"], calls);
        Assert.Equal(first.Diagnostics.Select(item => item.Id), second.Diagnostics.Select(item => item.Id));
    }

    /// <summary>Prevents an unavailable rule engine from reporting a completed, clean scan.</summary>
    [Fact]
    public async Task Missing_rules_never_report_clean_completion()
    {
        var result = await new AnalysisService<string>([]).AnalyzeAsync(new(Context), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("no-rules", Assert.Single(result.Issues).Code);
    }

    /// <summary>Checks that a coverage gap retains findings and remains visible after subsequent rules complete.</summary>
    [Fact]
    public async Task Incomplete_rule_preserves_findings_and_evaluates_remaining_rules()
    {
        var issue = new AnalysisIssue("missing-project", "Billing could not be loaded.", "AG001");
        var incomplete = new TestRule(RuleCatalog.ModuleDependencies, (_, _) => Task.FromResult(
            new RuleEvaluationResult(AnalysisStatus.Incomplete, [DiagnosticTests.Finding()], [issue])));
        var remaining = CleanRule(RuleCatalog.ModuleCycles);
        var result = await new AnalysisService<string>([remaining, incomplete]).AnalyzeAsync(new(Context), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Incomplete, result.Status);
        Assert.Single(result.Diagnostics);
        Assert.Equal(issue, Assert.Single(result.Issues));
        Assert.Equal(1, remaining.Calls);
    }

    /// <summary>Checks that thrown and explicitly returned failures preserve prior findings and stop later rules.</summary>
    /// <param name="throws">Whether the failing rule throws instead of returning a failed evaluation.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_preserves_partial_findings_and_stops_evaluation(bool throws)
    {
        var problem = new TestRule(RuleCatalog.LayerDependencies, (_, _) => throws
            ? throw new InvalidOperationException("The snapshot is invalid.")
            : Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Failed,
                issues: [new("invalid-snapshot", "The snapshot is invalid.", "AG002")])));
        var later = UnexpectedRule(RuleCatalog.ModuleCycles);
        var result = await new AnalysisService<string>([FindingRule(), problem, later]).AnalyzeAsync(new(Context), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Failed, result.Status);
        Assert.Single(result.Diagnostics);
        Assert.Contains("The snapshot is invalid.", Assert.Single(result.Issues).Message);
        Assert.Equal("AG002", result.Issues[0].RuleId);
        Assert.Equal(0, later.Calls);
    }

    /// <summary>Checks that cancellation before evaluation returns a cancelled result without invoking any rule.</summary>
    [Fact]
    public async Task Already_cancelled_request_does_not_invoke_rules()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var rule = UnexpectedRule(RuleCatalog.ModuleDependencies);

        var result = await new AnalysisService<string>([rule]).AnalyzeAsync(new(Context), cancellation.Token);

        Assert.Equal(AnalysisStatus.Cancelled, result.Status);
        Assert.False(result.IsComplete);
        Assert.Equal(0, rule.Calls);
    }

    /// <summary>Checks that mid-run cancellation retains prior findings even when a rule ignores the supplied token.</summary>
    /// <param name="observesToken">Whether the cancelling rule throws after observing the token.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_evaluation_preserves_partial_results(bool observesToken)
    {
        using var cancellation = new CancellationTokenSource();
        var cancelling = new TestRule(RuleCatalog.LayerDependencies, async (_, token) =>
        {
            await Task.Yield();
            cancellation.Cancel();
            if (observesToken)
            {
                token.ThrowIfCancellationRequested();
            }

            return new RuleEvaluationResult(AnalysisStatus.Completed);
        });
        var later = UnexpectedRule(RuleCatalog.ModuleCycles);

        var result = await new AnalysisService<string>([FindingRule(), cancelling, later]).AnalyzeAsync(new(Context), cancellation.Token);

        Assert.Equal(AnalysisStatus.Cancelled, result.Status);
        Assert.Single(result.Diagnostics);
        Assert.Equal(0, later.Calls);
    }

    /// <summary>Prevents an unexpected cancellation exception from masquerading as cancellation requested by the caller.</summary>
    [Fact]
    public async Task Unrelated_cancellation_exception_is_an_operational_failure()
    {
        var rule = new TestRule(RuleCatalog.ModuleDependencies, (_, _) => throw new OperationCanceledException("Unexpected cancellation."));

        var result = await new AnalysisService<string>([rule]).AnalyzeAsync(new(Context), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Failed, result.Status);
        Assert.Equal("rule-failed", Assert.Single(result.Issues).Code);
    }

    /// <summary>Checks that an explicit cancelled evaluation retains its own findings and prevents further evaluation.</summary>
    [Fact]
    public async Task Explicit_cancelled_result_is_preserved()
    {
        var cancelled = new TestRule(RuleCatalog.LayerDependencies, (_, _) => Task.FromResult(
            new RuleEvaluationResult(AnalysisStatus.Cancelled, [DiagnosticTests.Finding(RuleCatalog.LayerDependencies)])));
        var later = UnexpectedRule(RuleCatalog.ModuleCycles);

        var result = await new AnalysisService<string>([FindingRule(), cancelled, later]).AnalyzeAsync(new(Context), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Cancelled, result.Status);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Equal(0, later.Calls);
    }

    /// <summary>Rejects findings attributed to a different rule before an incorrect identity or severity can be reported.</summary>
    [Fact]
    public async Task Rule_cannot_emit_another_rules_identity()
    {
        var rule = new TestRule(RuleCatalog.LayerDependencies, (_, _) => Task.FromResult(
            new RuleEvaluationResult(AnalysisStatus.Completed, [DiagnosticTests.Finding()])));

        var result = await new AnalysisService<string>([rule]).AnalyzeAsync(new(Context), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Failed, result.Status);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("another rule ID", Assert.Single(result.Issues).Message);
    }

    /// <summary>Checks that a missing rule result becomes a failed analysis with an actionable rule-specific issue.</summary>
    [Fact]
    public async Task Missing_evaluation_result_fails_with_useful_context()
    {
        var rule = new TestRule(RuleCatalog.ModuleDependencies, (_, _) => Task.FromResult<RuleEvaluationResult>(null!));

        var result = await new AnalysisService<string>([rule]).AnalyzeAsync(new(Context), TestContext.Current.CancellationToken);

        Assert.Equal(AnalysisStatus.Failed, result.Status);
        Assert.Equal("AG001", Assert.Single(result.Issues).RuleId);
        Assert.Contains("no evaluation result", result.Issues[0].Message);
    }

    /// <summary>Checks defensive registration copies and rejects duplicate rules, null entries and null contexts.</summary>
    [Fact]
    public async Task Rule_registration_is_copied_and_invalid_registrations_are_rejected()
    {
        var rules = new List<IArchitectureRule<string>> { CleanRule(RuleCatalog.ModuleDependencies) };
        var service = new AnalysisService<string>(rules);
        rules.Clear();

        Assert.True((await service.AnalyzeAsync(new(Context), TestContext.Current.CancellationToken)).IsComplete);
        Assert.Throws<ArgumentException>(() => new AnalysisService<string>([FindingRule(), FindingRule()]));
        Assert.Throws<ArgumentException>(() => new AnalysisService<string>([null!]));
        Assert.Throws<ArgumentNullException>(() => new AnalysisRequest<string>(null!));
    }

    private static TestRule FindingRule() => new(RuleCatalog.ModuleDependencies, (_, _) =>
        Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Completed, [DiagnosticTests.Finding()])));

    private static TestRule CleanRule(RuleDescriptor descriptor) => new(descriptor, (_, _) =>
        Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Completed)));

    private static TestRule UnexpectedRule(RuleDescriptor descriptor) => new(descriptor, (_, _) =>
        throw new InvalidOperationException("This rule should not execute."));

    /// <summary>Adapts a test delegate to a rule and counts invocations to expose orchestration behavior.</summary>
    /// <param name="descriptor">The identity this test rule claims to evaluate.</param>
    /// <param name="evaluate">The behavior to execute for each evaluation.</param>
    private sealed class TestRule(RuleDescriptor descriptor, Func<string, CancellationToken, Task<RuleEvaluationResult>> evaluate)
        : IArchitectureRule<string>
    {
        /// <inheritdoc/>
        public RuleDescriptor Descriptor { get; } = descriptor;

        /// <summary>Gets the number of times the service invoked this rule.</summary>
        public int Calls { get; private set; }

        /// <summary>Records an invocation and forwards the context and token to the test's configured delegate.</summary>
        /// <param name="context">The context received from the service.</param>
        /// <param name="cancellationToken">The cancellation token received from the service.</param>
        /// <returns>The evaluation task produced by the test delegate.</returns>
        public Task<RuleEvaluationResult> EvaluateAsync(string context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return evaluate(context, cancellationToken);
        }
    }
}
