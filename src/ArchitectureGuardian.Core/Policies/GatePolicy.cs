using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Baselines;
using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Policies;

/// <summary>Evaluates a completed result against an inclusive severity threshold, separately from finding discovery.</summary>
public sealed class GatePolicy
{
    /// <summary>Creates a gate; noncompleted coverage always fails and cannot be relaxed by configuration.</summary>
    /// <param name="failOn">The lowest severity that fails the gate.</param>
    /// <exception cref="ArgumentOutOfRangeException">The severity is undefined.</exception>
    public GatePolicy(DiagnosticSeverity failOn = DiagnosticSeverity.Error)
    {
        if (!Enum.IsDefined(failOn))
        {
            throw new ArgumentOutOfRangeException(nameof(failOn));
        }

        FailOn = failOn;
    }

    /// <summary>Gets the inclusive failure threshold.</summary>
    public DiagnosticSeverity FailOn { get; }

    /// <summary>Checks coverage and diagnostic severity without modifying the result or evaluating rules.</summary>
    /// <param name="result">The analysis result with severity policy already applied.</param>
    /// <returns>True only for completed analysis without diagnostics at or above the threshold.</returns>
    /// <exception cref="ArgumentNullException">The result is null.</exception>
    public bool Passes(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsComplete && result.Diagnostics.All(diagnostic => diagnostic.Severity < FailOn);
    }

    /// <summary>Checks completed coverage and the severity of new findings, retaining existing debt for reporting.</summary>
    /// <param name="comparison">The current analysis classified against an explicit baseline.</param>
    /// <returns>True only for completed analysis without new findings at or above the threshold.</returns>
    public bool PassesNewFindings(BaselineComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return comparison.Analysis.IsComplete && comparison.New.All(diagnostic => diagnostic.Severity < FailOn);
    }
}
