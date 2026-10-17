using ArchitectureGuardian.Core.Canonicalization;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Analysis;

/// <summary>Describes an operational problem that prevents full analysis coverage.</summary>
/// <remarks>
/// Loading failures and missing context belong here rather than in rule diagnostics,
/// so consumers can distinguish an unavailable analysis from a completed policy evaluation.
/// </remarks>
public sealed record AnalysisIssue
{
    /// <summary>Creates an operational issue with optional rule and source context.</summary>
    /// <param name="code">A stable code that consumers can use to identify the kind of problem.</param>
    /// <param name="message">An explanation that helps the caller understand or resolve the problem.</param>
    /// <param name="ruleId">The affected rule ID, or <see langword="null"/> for a problem outside a specific rule.</param>
    /// <param name="location">The relevant source range, when one can be established.</param>
    /// <exception cref="ArgumentException">A required value, or a supplied rule ID, is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="code"/> or <paramref name="message"/> is null.</exception>
    public AnalysisIssue(string code, string message, string? ruleId = null, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (ruleId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        }

        Code = code;
        Message = message;
        RuleId = ruleId;
        Location = location;
    }

    /// <summary>Gets the machine-readable category of the operational problem.</summary>
    public string Code { get; }

    /// <summary>Gets the explanation of the problem and any available recovery context.</summary>
    public string Message { get; }

    /// <summary>Gets the affected rule ID, or <see langword="null"/> for an issue outside a specific rule.</summary>
    public string? RuleId { get; }

    /// <summary>Gets the relevant source range, or <see langword="null"/> when unavailable.</summary>
    public SourceLocation? Location { get; }

    internal string SortKey => CanonicalData.Encode(Code, RuleId, Location?.CanonicalKey, Message);
}
