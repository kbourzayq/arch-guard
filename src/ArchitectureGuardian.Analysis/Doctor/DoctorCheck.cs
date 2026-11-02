namespace ArchitectureGuardian.Analysis.Doctor;

/// <summary>Describes an actionable static or evaluated installation check, independently of architecture findings.</summary>
/// <param name="Id">Stable check identifier; scope disambiguates project-level checks.</param>
/// <param name="Scope">Absolute input or project path, or an environment label.</param>
/// <param name="Mode">Static or evaluated; static observations never claim effective MSBuild settings.</param>
/// <param name="Status">Passed, informational, warning, blocked or notRun.</param>
/// <param name="Required">Whether this check is required for the selected workflow.</param>
/// <param name="Message">Observed facts or the reason a check could not run.</param>
/// <param name="Remediation">A concrete next step; never executed by doctor.</param>
public sealed record DoctorCheck(string Id, string Scope, string Mode, string Status, bool Required, string Message, string Remediation);
