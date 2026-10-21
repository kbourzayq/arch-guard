using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Stable diagnostic projection without compiler or internal rule objects.</summary>
/// <param name="Id">Shared deterministic finding identity.</param>
/// <param name="RuleId">Canonical rule ID.</param>
/// <param name="Severity">Policy-adjusted info, warning or error.</param>
/// <param name="Message">Finding explanation.</param>
/// <param name="Location">Primary source span, when available.</param>
/// <param name="Target">Affected module or symbol.</param>
/// <param name="Evidence">All supporting facts and source spans.</param>
/// <param name="Remediation">Correction guidance.</param>
/// <param name="DocumentationUrl">Canonical rule documentation URL.</param>
internal sealed record AnalysisDiagnostic(string Id, string RuleId, string Severity, string Message,
    SourceLocation? Location, DiagnosticTarget Target, IReadOnlyList<DiagnosticEvidence> Evidence,
    string Remediation, string DocumentationUrl);
