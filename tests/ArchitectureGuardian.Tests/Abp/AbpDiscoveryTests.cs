using ArchitectureGuardian.Analysis.Discovery;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Tests.Workspaces;
using Xunit;

namespace ArchitectureGuardian.Tests.Abp;

/// <summary>Verifies that discovery recognizes ABP Framework solutions and proposes a policy that extends the abp pack.</summary>
/// <param name="fixture">Restored ABP workspace.</param>
[Trait("Category", "Integration")]
public sealed class AbpDiscoveryTests(AbpWorkspaceFixture fixture) : IClassFixture<AbpWorkspaceFixture>
{
    private async Task<DiscoveryResult> DiscoverAsync()
    {
        var root = Path.Combine(fixture.Root, "Abp");
        var result = await new DiscoveryService().DiscoverAsync(new(Path.Combine(root, "BookStore.slnx"), true, "Debug"), root,
            TestContext.Current.CancellationToken);
        Assert.True(result.Status == AnalysisStatus.Completed, string.Join('\n', result.Observations.Issues.Select(item => item.Code + ": " + item.Message)));
        return result;
    }

    /// <summary>ABP references and startup-template names produce high-confidence ownership and an abp pack suggestion.</summary>
    [Fact]
    public async Task Abp_solutions_are_recognized_with_evidence()
    {
        var proposal = (await DiscoverAsync()).Proposal;
        var style = Assert.Single(proposal.Inferences, item => item.Kind == "style");
        Assert.Equal(("ABP Framework", "high"), (style.Subject, style.Confidence));
        Assert.All(style.Evidence, item => Assert.Contains("Volo.Abp.", item, StringComparison.Ordinal));
        Assert.Contains(proposal.Inferences, item => item.Kind == "rule-pack" && item.Subject == "abp@1.0.0");
        Assert.Equal(["Acme.BookStore", "Acme.Catalog", "Acme.Ordering"], proposal.Policy!.Modules.Select(module => module.Id));
        Assert.Equal(["Application", "ApplicationContracts", "Domain", "DomainShared", "Host", "HttpApi", "HttpApiClient", "Persistence"],
            proposal.Policy.Layers.Select(layer => layer.Id));
        // ABP modules publish Application.Contracts and Domain.Shared; observed edges grant no cross-module permission.
        Assert.Equal(["src/Catalog/Acme.Catalog.Application.Contracts/Acme.Catalog.Application.Contracts.csproj",
            "src/Catalog/Acme.Catalog.Domain.Shared/Acme.Catalog.Domain.Shared.csproj"],
            proposal.Policy.Modules.Single(module => module.Id == "Acme.Catalog").Contracts.Single().Projects.Select(pattern => pattern.Value));
        Assert.All(proposal.Policy.Modules, module => Assert.Empty(module.AllowedDependencies));
        Assert.Equal(["layer:Tests"], proposal.Pack!.Exclusions);
    }

    /// <summary>The proposal extends the pack, and the shared reader composes it into the same candidate policy.</summary>
    [Fact]
    public async Task Proposed_yaml_extends_the_pack_and_composes_equivalently()
    {
        var result = await DiscoverAsync();
        var yaml = ProposedPolicyYaml.Serialize(result.Proposal, TestContext.Current.CancellationToken);
        Assert.Contains("extends:\n  - pack: abp\n    version: 1.0.0\n", yaml, StringComparison.Ordinal);
        Assert.Contains("exclude: [\"layer:Tests\"]", yaml, StringComparison.Ordinal);
        // The pack supplies layer selectors; only the host's projects come from the proposal.
        Assert.DoesNotContain("DomainShared", yaml, StringComparison.Ordinal);
        Assert.Contains("  - id: \"Host\"\n    projects: [\"src/Acme.BookStore.Web/Acme.BookStore.Web.csproj\"]\n", yaml, StringComparison.Ordinal);

        var composed = new YamlPolicyLoader().Parse(yaml, "architecture.guardian.proposed.yaml", TestContext.Current.CancellationToken);
        var projects = result.Observations.Inventory!.Projects.Select(project => project.Id).ToArray();
        var expected = new ResolvedPolicy(result.Proposal.Policy!, projects);
        Assert.All(projects, project => Assert.Equal(expected.Projects[project], new ResolvedPolicy(composed, projects).Projects[project]));
        Assert.Equal(result.Proposal.Policy!.SeverityPolicy.RuleSeverities.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            composed.SeverityPolicy.RuleSeverities.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(["Application", "Domain", "Persistence"], composed.Abp!.RepositoryLayers);
        // The same input yields byte-identical text.
        Assert.Equal(yaml, ProposedPolicyYaml.Serialize((await DiscoverAsync()).Proposal, TestContext.Current.CancellationToken));
    }

    /// <summary>Candidate compliance evaluates the pack's ABP rules with real facts instead of reporting unavailable coverage.</summary>
    [Fact]
    public async Task Candidate_compliance_runs_the_abp_rules()
    {
        var result = await DiscoverAsync();
        Assert.Equal(AnalysisStatus.Completed, result.Compliance!.Status);
        Assert.DoesNotContain(result.Compliance.Issues, issue => issue.Code == "abp-facts-unavailable");
        Assert.Contains(result.Compliance.Diagnostics, diagnostic => diagnostic.RuleId == "ABP002");
        Assert.Contains(result.Compliance.Diagnostics, diagnostic => diagnostic.RuleId == "ABP005");
    }

    /// <summary>A pack form that would not resolve to the same candidate falls back to the complete standalone policy.</summary>
    [Fact]
    public async Task Inequivalent_pack_forms_fall_back_to_standalone_yaml()
    {
        var proposal = (await DiscoverAsync()).Proposal;
        var policy = proposal.Policy!;
        // Moving the domain project into the persistence layer is something the pack's selectors cannot express.
        var domain = policy.Layers.Single(layer => layer.Id == "Domain").Projects.Select(pattern => pattern.Value).ToArray();
        var layers = policy.Layers.Where(layer => layer.Id != "Domain").Select(layer => layer.Id == "Persistence"
            ? new LayerPolicy(layer.Id, layer.Projects.Select(pattern => pattern.Value).Concat(domain), layer.AllowedDependencies.Where(id => id != "Domain"))
            : new LayerPolicy(layer.Id, layer.Projects.Select(pattern => pattern.Value), layer.AllowedDependencies.Where(id => id != "Domain"),
                layer.ForbiddenAssemblies, layer.ForbiddenNamespaces, layer.DomainInfrastructure));
        var changed = new ArchitecturePolicy(policy.Modules, layers, policy.SeverityPolicy.RuleSeverities, policy.Gate,
            abp: new AbpPolicy(policy.Abp!.RepositoryLayers.Where(id => id != "Domain")));
        var yaml = ProposedPolicyYaml.Serialize(Proposal(changed, proposal), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("extends:", yaml, StringComparison.Ordinal);
        Assert.Contains("abp:\n  repositoryLayers: [\"Application\", \"Persistence\"]\n", yaml, StringComparison.Ordinal);
        Assert.Equal(changed.Layers.Select(layer => layer.Id),
            new YamlPolicyLoader().Parse(yaml, "architecture.guardian.proposed.yaml", TestContext.Current.CancellationToken).Layers.Select(layer => layer.Id));
    }

    /// <summary>Creates a proposal with another candidate through the internal constructor, keeping the pack suggestion.</summary>
    private static PolicyProposal Proposal(ArchitecturePolicy policy, PolicyProposal source) => (PolicyProposal)typeof(PolicyProposal)
        .GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Single()
        .Invoke([policy, source.Ownership, source.Inferences, source.Questions, source.Pack]);
}
