using ArchitectureGuardian.Core.Canonicalization;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Rules;

/// <summary>A rule finding before severity policy or quality gates are applied.</summary>
/// <remarks>All evidence and identifying content are captured at construction so callers cannot mutate the finding.</remarks>
public sealed class RuleViolation
{
    /// <summary>Creates an evidence-backed finding and computes its reproducible snapshot identity.</summary>
    /// <param name="rule">The descriptor of the rule that established the violation.</param>
    /// <param name="message">An explanation of which architecture constraint was violated.</param>
    /// <param name="target">The affected logical module, symbol or both.</param>
    /// <param name="evidence">At least one supporting fact; input order does not affect identity.</param>
    /// <param name="remediation">Guidance for correcting the violation.</param>
    /// <param name="location">The primary source range, when one is available.</param>
    /// <param name="context">Optional structured provenance supplied by the evaluator; not part of the v1 finding identity.</param>
    /// <param name="classification">How much the evidence establishes, or null for rules that always prove their findings.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentException">Message or remediation is blank, or evidence is empty or contains null.</exception>
    public RuleViolation(RuleDescriptor rule, string message, DiagnosticTarget target,
        IEnumerable<DiagnosticEvidence> evidence, string remediation, SourceLocation? location = null, ViolationContext? context = null,
        ViolationClassification? classification = null)
    {
        if (classification is { } value && !Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        ArgumentNullException.ThrowIfNull(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(remediation);

        Rule = rule;
        Message = message;
        Target = target;
        Evidence = CanonicalData.Snapshot(evidence, item => item.CanonicalKey);
        if (Evidence.Count == 0)
        {
            throw new ArgumentException("A violation must include supporting evidence.", nameof(evidence));
        }

        Remediation = remediation;
        Location = location;
        Context = context;
        Classification = classification;
        // The version tag allows future identity formats to be distinguished. Severity is excluded
        // so reporting policy can change without changing the identity of this snapshot's finding.
        var identity = new List<string?>
        {
            "ag-diagnostic-v1", rule.Id, target.Module, target.Symbol, location?.CanonicalKey, message, remediation,
            rule.DocumentationUrl.AbsoluteUri, CanonicalData.Encode(Evidence.Select(item => item.CanonicalKey).ToArray()),
        };
        if (classification is not null)
        {
            // Appended only when present so identities of rules without classification are unchanged.
            identity.Add(classification.ToString());
        }

        Id = CanonicalData.Hash(CanonicalData.Encode(identity.ToArray()));
    }

    /// <summary>Gets the lowercase SHA-256 identity of the versioned canonical finding content.</summary>
    /// <remarks>Location, wording, guidance or evidence changes may change this ID; it is not a baseline-matching fingerprint.</remarks>
    public string Id { get; }

    /// <summary>Gets the identity and documentation of the rule that produced the finding.</summary>
    public RuleDescriptor Rule { get; }

    /// <summary>Gets the explanation of the architecture constraint that was violated.</summary>
    public string Message { get; }

    /// <summary>Gets the affected logical module, symbol or both.</summary>
    public DiagnosticTarget Target { get; }

    /// <summary>Gets the read-only supporting facts in canonical order, retaining identical duplicates.</summary>
    public IReadOnlyList<DiagnosticEvidence> Evidence { get; }

    /// <summary>Gets guidance for correcting the violation.</summary>
    public string Remediation { get; }

    /// <summary>Gets the primary source range, or <see langword="null"/> when unavailable.</summary>
    public SourceLocation? Location { get; }

    /// <summary>Gets evaluator-owned explanation details, or null for findings without structured provenance.</summary>
    /// <remarks>This additive projection does not change the existing v1 diagnostic identity.</remarks>
    public ViolationContext? Context { get; }

    /// <summary>Gets how much the evidence establishes, or null when the rule does not classify its findings.</summary>
    /// <remarks>Rules whose analysis has runtime blind spots set this so consumers can separate proof from concerns.</remarks>
    public ViolationClassification? Classification { get; }

    internal string SortKey => CanonicalData.Encode(Rule.Id, Id);
}
