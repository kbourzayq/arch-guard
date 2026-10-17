using System.Collections.ObjectModel;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Policies;

/// <summary>Assigns reporting severities to rule findings without changing evaluation or deciding quality gates.</summary>
public sealed class SeverityPolicy
{
    /// <summary>Captures case-sensitive per-rule overrides and the fallback severity.</summary>
    /// <param name="ruleSeverities">Overrides keyed by ordinal rule ID; null means no overrides.</param>
    /// <param name="defaultSeverity">The level used for rule IDs without an override.</param>
    /// <exception cref="ArgumentException">An override has a blank rule ID.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The default or an override severity is undefined.</exception>
    public SeverityPolicy(IReadOnlyDictionary<string, DiagnosticSeverity>? ruleSeverities = null,
        DiagnosticSeverity defaultSeverity = DiagnosticSeverity.Error)
    {
        ValidateSeverity(defaultSeverity);
        var copy = new Dictionary<string, DiagnosticSeverity>(StringComparer.Ordinal);
        foreach (var (ruleId, severity) in ruleSeverities ?? new Dictionary<string, DiagnosticSeverity>())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
            ValidateSeverity(severity);
            copy.Add(ruleId, severity);
        }

        RuleSeverities = new ReadOnlyDictionary<string, DiagnosticSeverity>(copy);
        DefaultSeverity = defaultSeverity;
    }

    /// <summary>Gets the fallback reporting level for rules without an explicit override.</summary>
    public DiagnosticSeverity DefaultSeverity { get; }

    /// <summary>Gets the read-only snapshot of case-sensitive per-rule severity overrides.</summary>
    public IReadOnlyDictionary<string, DiagnosticSeverity> RuleSeverities { get; }

    /// <summary>Resolves an override by ordinal rule ID, falling back to the configured default.</summary>
    /// <param name="ruleId">The case-sensitive ID of the rule whose finding is being reported.</param>
    /// <returns>The override severity, or <see cref="DefaultSeverity"/> if no override exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ruleId"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="ruleId"/> is blank.</exception>
    public DiagnosticSeverity GetSeverity(string ruleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        return RuleSeverities.TryGetValue(ruleId, out var severity) ? severity : DefaultSeverity;
    }

    private static void ValidateSeverity(DiagnosticSeverity severity)
    {
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }
    }
}
