using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Distinguishes policy-only rule guidance from freshly resolved instance provenance and coverage.</summary>
/// <param name="SchemaVersion">Response contract version.</param>
/// <param name="Status">Completed or underlying analysis coverage status.</param>
/// <param name="Scope">Rule or diagnostic explanation.</param>
/// <param name="PolicyPath">Policy path relative to the operator root.</param>
/// <param name="InputPath">Analysis input path, or null for policy-only guidance.</param>
/// <param name="Configuration">Build configuration, or null when no workspace was loaded.</param>
/// <param name="TargetFramework">Explicit analysis framework, if supplied.</param>
/// <param name="Rule">Canonical metadata, selection and severity; null if incomplete analysis cannot resolve the ID.</param>
/// <param name="Explanation">Deterministic rule semantics or the actual finding message.</param>
/// <param name="Diagnostic">Original diagnostic and evidence, or null for rule guidance or unresolved incomplete analysis.</param>
/// <param name="DependencyPath">Evaluator-owned endpoints in directed path order; empty without a resolved instance.</param>
/// <param name="PolicyConstraints">Relevant configured constraints, or built-in rule invariants.</param>
/// <param name="PublicContracts">Declared contract alternatives; no API suitability or permission is implied.</param>
/// <param name="Remediation">Rule-specific guidance, including declared contracts when applicable.</param>
/// <param name="Issues">Operational coverage issues from fresh analysis.</param>
internal sealed record ExplanationResponse(
    int SchemaVersion,
    string Status,
    string Scope,
    string PolicyPath,
    string? InputPath,
    string? Configuration,
    string? TargetFramework,
    RuleContext? Rule,
    string Explanation,
    AnalysisDiagnostic? Diagnostic,
    IReadOnlyList<ViolationPathNode> DependencyPath,
    IReadOnlyList<PolicyConstraint> PolicyConstraints,
    IReadOnlyList<string> PublicContracts,
    string Remediation,
    IReadOnlyList<AnalysisIssue> Issues);
