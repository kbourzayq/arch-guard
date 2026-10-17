using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Analysis.Orchestration;

/// <summary>Host-independent orchestration over an already prepared neutral context.</summary>
/// <typeparam name="TContext">The stable input model understood by all registered rules.</typeparam>
/// <remarks>
/// Rules run sequentially in ordinal ID order. The service applies severity policy and preserves
/// coverage failures and partial findings so CLI and protocol hosts share the same analysis behavior.
/// </remarks>
public sealed class AnalysisService<TContext> : IAnalysisService<TContext> where TContext : notnull
{
    private readonly IArchitectureRule<TContext>[] _rules;

    /// <summary>Snapshots rule registrations and establishes their deterministic execution order.</summary>
    /// <param name="rules">The rules available for this scope; each descriptor ID must be registered once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rules"/> is null.</exception>
    /// <exception cref="ArgumentException">A rule or descriptor is null, or a descriptor ID is duplicated.</exception>
    public AnalysisService(IEnumerable<IArchitectureRule<TContext>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var copy = rules.ToArray();
        if (copy.Any(rule => rule is null || rule.Descriptor is null))
        {
            throw new ArgumentException("Rules and their descriptors cannot be null.", nameof(rules));
        }

        if (copy.Select(rule => rule.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ArgumentException("Register each rule ID only once.", nameof(rules));
        }

        _rules = copy.OrderBy(rule => rule.Descriptor.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Evaluates the registered rules and applies the request's severity policy to their findings.</summary>
    /// <param name="request">The prepared context and reporting policy, kept stable for the duration of the run.</param>
    /// <param name="cancellationToken">A cooperative cancellation token forwarded to every rule.</param>
    /// <returns>
    /// The coverage outcome, ordered diagnostics and operational issues. Requested cancellation returns
    /// a cancelled result; rule exceptions return a failed result. Both retain available partial findings.
    /// </returns>
    /// <remarks>
    /// No registered rules produces an incomplete result. Incomplete evaluations allow later rules to run;
    /// failed or cancelled evaluations stop the run. Error-severity findings alone do not imply failure.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async Task<AnalysisResult> AnalyzeAsync(AnalysisRequest<TContext> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<Diagnostic>();
        var issues = new List<AnalysisIssue>();
        var status = AnalysisStatus.Completed;

        if (cancellationToken.IsCancellationRequested)
        {
            // Honour cancellation even when no rules are registered, without invoking any rule.
            return new AnalysisResult(AnalysisStatus.Cancelled);
        }

        if (_rules.Length == 0)
        {
            // An unavailable engine must not look like a completed scan that found no violations.
            return new AnalysisResult(AnalysisStatus.Incomplete, issues:
                [new AnalysisIssue("no-rules", "No architecture rules are available for this analysis.")]);
        }

        foreach (var rule in _rules)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var evaluation = await rule.EvaluateAsync(request.Context, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("A rule returned no evaluation result.");
                // Reject misattributed findings before applying another rule's policy or publishing its identity.
                if (evaluation.Violations.Any(violation => !StringComparer.Ordinal.Equals(violation.Rule.Id, rule.Descriptor.Id)))
                {
                    throw new InvalidOperationException("A rule returned a violation for another rule ID.");
                }

                diagnostics.AddRange(evaluation.Violations.Select(violation =>
                    new Diagnostic(violation, request.SeverityPolicy.GetSeverity(violation.Rule.Id))));
                issues.AddRange(evaluation.Issues);
                // Preserve the returned findings, but catch cancellation even if the rule ignored its token.
                cancellationToken.ThrowIfCancellationRequested();

                if (evaluation.Status is AnalysisStatus.Failed or AnalysisStatus.Cancelled)
                {
                    return new AnalysisResult(evaluation.Status, diagnostics, issues);
                }

                if (evaluation.Status == AnalysisStatus.Incomplete)
                {
                    // Keep collecting useful findings without letting a later completed rule hide a coverage gap.
                    status = AnalysisStatus.Incomplete;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only cancellation requested by this run is treated as its cancellation outcome.
                return new AnalysisResult(AnalysisStatus.Cancelled, diagnostics, issues);
            }
            catch (Exception exception)
            {
                // Unexpected cancellation also reaches here; retain rule context for the host's error report.
                issues.Add(new AnalysisIssue("rule-failed", $"{exception.GetType().Name}: {exception.Message}", rule.Descriptor.Id));
                return new AnalysisResult(AnalysisStatus.Failed, diagnostics, issues);
            }
        }

        return new AnalysisResult(status, diagnostics, issues);
    }
}
