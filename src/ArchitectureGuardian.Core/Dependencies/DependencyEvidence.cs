using ArchitectureGuardian.Core.Canonicalization;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Dependencies;

/// <summary>Retains the original project reference or semantic use through every graph aggregation.</summary>
public sealed record DependencyEvidence
{
    /// <summary>Creates evidence with exact project ownership and optional symbol and source context.</summary>
    /// <param name="sourceProject">The policy-relative project containing the dependency.</param>
    /// <param name="targetProject">The target source project, or null for external metadata.</param>
    /// <param name="fact">The observed reference or use and its available source range.</param>
    /// <param name="sourceSymbol">The originating source symbol, when available.</param>
    /// <param name="targetSymbol">The referenced symbol, when available.</param>
    /// <param name="projectReference">The evaluated referenced project path for project-reference evidence.</param>
    public DependencyEvidence(string sourceProject, string? targetProject, DiagnosticEvidence fact,
        string? sourceSymbol = null, string? targetSymbol = null, string? projectReference = null)
    {
        SourceProject = ProjectPattern.NormalizeIdentity(sourceProject);
        TargetProject = targetProject is null ? null : ProjectPattern.NormalizeIdentity(targetProject);
        ArgumentNullException.ThrowIfNull(fact);
        Fact = fact;
        SourceSymbol = sourceSymbol;
        TargetSymbol = targetSymbol;
        ProjectReference = projectReference is null ? null : ProjectPattern.NormalizeIdentity(projectReference);
    }

    /// <summary>Gets the original source project's identity.</summary>
    public string SourceProject { get; }
    /// <summary>Gets the original target project's identity, or null for external metadata.</summary>
    public string? TargetProject { get; }
    /// <summary>Gets the observed dependency and source range; this can be reused directly in a rule finding.</summary>
    public DiagnosticEvidence Fact { get; }
    /// <summary>Gets the originating source symbol, or null for a project/assembly reference.</summary>
    public string? SourceSymbol { get; }
    /// <summary>Gets the referenced symbol, when available.</summary>
    public string? TargetSymbol { get; }
    /// <summary>Gets the evaluated project reference, or null for semantic and metadata-reference evidence.</summary>
    public string? ProjectReference { get; }

    internal string SortKey => CanonicalData.Encode(SourceProject, TargetProject, SourceSymbol, TargetSymbol,
        ProjectReference, Fact.CanonicalKey);
}
