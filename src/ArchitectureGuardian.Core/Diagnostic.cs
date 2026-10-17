namespace ArchitectureGuardian.Core;

/// <summary>Expresses the reporting level assigned to a finding by severity policy.</summary>
/// <remarks>Severity alone does not decide whether analysis completed or a quality gate passes.</remarks>
public enum DiagnosticSeverity
{
    /// <summary>Reports a finding as informational guidance.</summary>
    Info,

    /// <summary>Reports a finding that warrants attention.</summary>
    Warning,

    /// <summary>Reports a finding at the strongest available level for consumers to evaluate.</summary>
    Error,
}

/// <summary>Combines a rule finding with its policy-assigned severity for reporting by any host.</summary>
/// <remarks>The original finding supplies the identity and evidence; severity changes do not change that identity.</remarks>
public sealed class Diagnostic
{
    /// <summary>Applies a reporting severity to an existing rule finding.</summary>
    /// <param name="violation">The immutable finding produced by rule evaluation.</param>
    /// <param name="severity">The severity selected by policy, independently of quality-gate decisions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="violation"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="severity"/> is undefined.</exception>
    public Diagnostic(RuleViolation violation, DiagnosticSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(violation);
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        Violation = violation;
        Severity = severity;
    }

    /// <summary>Gets the severity-free finding from which this diagnostic was produced.</summary>
    public RuleViolation Violation { get; }

    /// <summary>Gets the reproducible snapshot identity inherited from the finding.</summary>
    public string Id => Violation.Id;

    /// <summary>Gets the canonical or rule-pack identifier of the check that produced the finding.</summary>
    public string RuleId => Violation.Rule.Id;

    /// <summary>Gets the reporting level assigned by severity policy.</summary>
    public DiagnosticSeverity Severity { get; }

    /// <summary>Gets the explanation of the architecture violation.</summary>
    public string Message => Violation.Message;

    /// <summary>Gets the primary source range, or <see langword="null"/> when unavailable.</summary>
    public SourceLocation? Location => Violation.Location;

    /// <summary>Gets the affected logical module, symbol or both.</summary>
    public DiagnosticTarget Target => Violation.Target;

    /// <summary>Gets the read-only, canonically ordered facts supporting the finding.</summary>
    public IReadOnlyList<DiagnosticEvidence> Evidence => Violation.Evidence;

    /// <summary>Gets guidance for correcting the architecture violation.</summary>
    public string Remediation => Violation.Remediation;

    /// <summary>Gets the absolute HTTP or HTTPS URL explaining the rule.</summary>
    public Uri DocumentationUrl => Violation.Rule.DocumentationUrl;

    internal string SortKey => CanonicalData.Encode(RuleId, Id, Severity.ToString());
}
