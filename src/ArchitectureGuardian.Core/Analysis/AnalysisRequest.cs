using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Analysis;

/// <summary>Pairs a prepared analysis context with the severity policy used to report its findings.</summary>
/// <typeparam name="TContext">The neutral input model understood by the registered rules.</typeparam>
/// <remarks>
/// The caller must keep the context stable throughout evaluation. An arbitrary context cannot
/// be defensively copied here; workspace and transport objects must be converted by adapters first.
/// </remarks>
public sealed class AnalysisRequest<TContext> where TContext : notnull
{
    /// <summary>Creates a request over a prepared context with optional severity overrides.</summary>
    /// <param name="context">The stable input to pass to each registered rule.</param>
    /// <param name="severityPolicy">The reporting policy, or <see langword="null"/> to use error severity by default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public AnalysisRequest(TContext context, SeverityPolicy? severityPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        SeverityPolicy = severityPolicy ?? new SeverityPolicy();
    }

    /// <summary>Gets the prepared input shared by the rules for this run.</summary>
    public TContext Context { get; }

    /// <summary>Gets the policy applied after rules produce severity-free findings.</summary>
    public SeverityPolicy SeverityPolicy { get; }
}
