namespace ArchitectureGuardian.Core;

/// <summary>Defines a pure architecture check over neutral input, independently of severity and quality gates.</summary>
/// <typeparam name="TContext">The input model that supplies the facts and scope required by this rule.</typeparam>
public interface IArchitectureRule<in TContext> where TContext : notnull
{
    /// <summary>Gets the stable rule identity, display title and documentation used by its findings.</summary>
    RuleDescriptor Descriptor { get; }

    /// <summary>Evaluates available facts and returns evidence-backed findings with an explicit coverage outcome.</summary>
    /// <param name="context">The prepared input, which the rule must treat as read-only.</param>
    /// <param name="cancellationToken">A token the rule should observe for prompt cooperative cancellation.</param>
    /// <returns>Findings without severity, plus the coverage status and any operational issues.</returns>
    /// <remarks>
    /// Findings must use this rule's descriptor ID. Missing context must produce an incomplete result,
    /// and evaluation must use the supplied scope rather than loading a workspace or invoking a host.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Cancellation interrupts evaluation.</exception>
    Task<RuleEvaluationResult> EvaluateAsync(TContext context, CancellationToken cancellationToken = default);
}
