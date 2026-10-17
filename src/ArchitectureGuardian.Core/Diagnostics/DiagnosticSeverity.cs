namespace ArchitectureGuardian.Core.Diagnostics;

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
