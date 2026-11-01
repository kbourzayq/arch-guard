using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;

namespace ArchitectureGuardian.Core.Discovery;

/// <summary>Separates inventory coverage, architectural suggestions and candidate compliance.</summary>
public sealed class DiscoveryResult
{
    /// <summary>Snapshots discovery evidence for reuse by presentation and onboarding hosts.</summary>
    /// <param name="input">Selected portable input identity.</param>
    /// <param name="configuration">Requested configuration.</param>
    /// <param name="framework">Requested framework, or null for a single-target input.</param>
    /// <param name="staticProjects">Literal project paths, explicitly unevaluated.</param>
    /// <param name="contexts">Actually evaluated contexts; inspect coverage before relying on them.</param>
    /// <param name="observations">Policy-independent declarations, dependencies and coverage.</param>
    /// <param name="proposal">Candidate intentions and unresolved questions.</param>
    /// <param name="compliance">Independent evaluation against the proposal, or null when unavailable.</param>
    /// <param name="staticIssues">Unevaluated hint diagnostics, separate from verified context coverage.</param>
    public DiscoveryResult(string input, string configuration, string? framework, IEnumerable<string> staticProjects,
        IEnumerable<DiscoveryContext> contexts, DependencyInventoryResult observations, PolicyProposal proposal, AnalysisResult? compliance = null,
        IEnumerable<AnalysisIssue>? staticIssues = null)
    {
        Input = input;
        Configuration = configuration;
        Framework = framework;
        StaticProjects = Array.AsReadOnly(staticProjects.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        StaticIssues = Array.AsReadOnly((staticIssues ?? []).Distinct().OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal).ToArray());
        Contexts = Array.AsReadOnly(contexts.OrderBy(context => context.Project, StringComparer.Ordinal).ToArray());
        Observations = observations;
        Proposal = proposal;
        Compliance = compliance;
    }

    /// <summary>Gets the selected relative input.</summary>
    public string Input { get; }
    /// <summary>Gets the requested configuration.</summary>
    public string Configuration { get; }
    /// <summary>Gets the requested framework.</summary>
    public string? Framework { get; }
    /// <summary>Gets unevaluated literal paths; imports, conditions and wildcards are not expanded.</summary>
    public IReadOnlyList<string> StaticProjects { get; }
    /// <summary>Gets static hint issues; inactive references do not reduce verified context coverage.</summary>
    public IReadOnlyList<AnalysisIssue> StaticIssues { get; }
    /// <summary>Gets evaluated contexts; other configurations/frameworks remain unexplored.</summary>
    public IReadOnlyList<DiscoveryContext> Contexts { get; }
    /// <summary>Gets policy-independent evidence and inventory coverage.</summary>
    public DependencyInventoryResult Observations { get; }
    /// <summary>Gets reviewable intentions, independently of observations.</summary>
    public PolicyProposal Proposal { get; }
    /// <summary>Gets candidate policy compliance, independently of configuration validity.</summary>
    public AnalysisResult? Compliance { get; }
    /// <summary>Gets combined operational coverage; unresolved ownership is reported separately.</summary>
    public AnalysisStatus Status => Observations.Status != AnalysisStatus.Completed ? Observations.Status : Compliance?.Status ?? Observations.Status;
}
