using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Core.Rules;
using Xunit;

namespace ArchitectureGuardian.Tests.Rules;

/// <summary>Verifies rule-owned explanation provenance without parsing diagnostic wording.</summary>
public sealed class ViolationContextTests
{
    /// <summary>Every canonical evaluator retains observed endpoints, the exact constraint and configured contract alternatives.</summary>
    [Fact]
    public async Task Canonical_rules_retain_paths_constraints_and_declared_contracts()
    {
        var policy = new ArchitecturePolicy([
            new("Orders", ["Orders/*.csproj"], forbiddenNamespaces: ["Forbidden"]),
            new("Billing", ["Billing/*.csproj"], [new("api", ["Billing/Contracts.csproj"])])],
            [new("Domain", ["Orders/*.csproj"], domainInfrastructure: new(["Billing"], ["Forbidden"], ["Forbidden.Safe"])),
                new("Infrastructure", ["Billing/*.csproj"])],
            RuleCatalog.All.ToDictionary(rule => rule.Id, _ => DiagnosticSeverity.Error, StringComparer.Ordinal));
        var projects = new[] { "Orders/App.csproj", "Billing/Internal.csproj", "Billing/Contracts.csproj" };
        var repository = new RepositoryModel("Input.slnx", policy,
            projects.Select(path => new RepositoryProject(path, path, new(path, path, []))));
        var location = new SourceLocation("Orders/Use.cs", 3, 7);
        var forward = new DependencyEvidence(projects[0], projects[1], new("Observed semantic use.", location),
            targetSymbol: "Forbidden.Store", targetAssembly: "Billing", targetNamespace: "Forbidden.Persistence");
        var reverse = new DependencyEvidence(projects[1], projects[0], new("Observed reverse reference."), projectReference: projects[0]);
        var edges = new[] { Edge(forward), Edge(reverse) };
        var context = new DependencyBuildResult(repository, new([], edges), AnalysisStatus.Completed);
        IArchitectureRule<DependencyBuildResult>[] rules = [new ModuleDependenciesRule(), new LayerDependenciesRule(),
            new ModuleCyclesRule(), new ImplementationBoundariesRule(), new ForbiddenReferencesRule(), new DomainInfrastructureRule()];
        foreach (var rule in rules)
        {
            var result = await rule.EvaluateAsync(context);
            Assert.Equal(AnalysisStatus.Completed, result.Status);
            Assert.NotEmpty(result.Violations);
            var repeated = await rule.EvaluateAsync(new(repository, new([], edges.Reverse()), AnalysisStatus.Completed));
            Assert.Equal(result.Violations.Select(item => item.Id).Order(StringComparer.Ordinal), repeated.Violations.Select(item => item.Id).Order(StringComparer.Ordinal));
            foreach (var finding in result.Violations)
            {
                var details = Assert.IsType<ViolationContext>(finding.Context);
                Assert.NotEmpty(details.PolicyConstraints);
                Assert.All(finding.Evidence, fact => Assert.Contains(fact, new[] { forward.Fact, reverse.Fact }));
                var withoutProjection = new RuleViolation(finding.Rule, finding.Message, finding.Target, finding.Evidence, finding.Remediation, finding.Location);
                Assert.Equal(withoutProjection.Id, finding.Id);
                var again = Assert.Single(repeated.Violations, item => item.Id == finding.Id).Context!;
                Assert.Equal(details.DependencyPath, again.DependencyPath);
                Assert.Equal(details.PublicContracts, again.PublicContracts);
                Assert.Equal(details.PolicyConstraints.Select(item => (item.Scope, item.Setting, string.Join(",", item.Values))),
                    again.PolicyConstraints.Select(item => (item.Scope, item.Setting, string.Join(",", item.Values))));

                var constraint = Assert.Single(details.PolicyConstraints);
                if (rule.Descriptor.Id == "AG003")
                {
                    Assert.Equal(["Billing", "Orders", "Billing"], details.DependencyPath.Select(node => node.Identity));
                    Assert.All(details.DependencyPath, node => Assert.Equal("module", node.Kind));
                    Assert.Equal("rule:AG003", constraint.Scope);
                    Assert.Equal("acyclicModules", constraint.Setting);
                    Assert.Empty(constraint.Values);
                    continue;
                }

                Assert.Equal("project", details.DependencyPath[0].Kind);
                Assert.Contains(details.DependencyPath[0].Identity, projects);
                if (finding.Target.Module == "Orders")
                {
                    Assert.Equal(projects[0], details.DependencyPath[0].Identity);
                    switch (rule.Descriptor.Id)
                    {
                        case "AG001":
                            Assert.Equal(projects[1], details.DependencyPath[1].Identity);
                            Assert.Equal("modules.Orders", constraint.Scope);
                            Assert.Equal("allowedDependencies", constraint.Setting);
                            Assert.Empty(constraint.Values);
                            Assert.Equal(["contract:Billing/api"], details.PublicContracts);
                            break;
                        case "AG002":
                            Assert.Equal("layers.Domain", constraint.Scope);
                            Assert.Equal("allowedDependencies", constraint.Setting);
                            Assert.Empty(details.PublicContracts);
                            break;
                        case "AG004":
                            Assert.Equal([projects[0], projects[1], "Forbidden.Store"], details.DependencyPath.Select(node => node.Identity));
                            Assert.Equal("symbol", details.DependencyPath[2].Kind);
                            Assert.Equal("modules.Billing", constraint.Scope);
                            Assert.Equal("contracts", constraint.Setting);
                            Assert.Equal(["api"], constraint.Values);
                            Assert.Equal(["contract:Billing/api"], details.PublicContracts);
                            break;
                        case "AG005":
                        case "AG006":
                            Assert.Equal("symbol", details.DependencyPath[1].Kind);
                            Assert.Equal("Forbidden.Store", details.DependencyPath[1].Identity);
                            Assert.Equal(rule.Descriptor.Id == "AG005" ? "modules.Orders" : "layers.Domain.domainInfrastructure", constraint.Scope);
                            Assert.Equal(constraint.Setting == "forbiddenNamespaces" ? "Forbidden" : "Billing", Assert.Single(constraint.Values));
                            Assert.Empty(details.PublicContracts);
                            break;
                    }
                }
                else
                {
                    Assert.Null(finding.Location); // Reference evidence supplies no source coordinates.
                    Assert.Empty(details.PublicContracts);
                }
            }
        }
    }

    /// <summary>Caller mutation cannot alter path order, constraints or guidance after construction.</summary>
    [Fact]
    public void Provenance_is_an_immutable_snapshot_and_rejects_missing_endpoints()
    {
        var path = new List<ViolationPathNode> { new("module", "Z"), new("module", "A"), new("module", "Z") };
        var values = new List<string> { "B", "A" };
        var contracts = new List<string> { "contract:B/api" };
        var constraints = new List<PolicyConstraint> { new("modules.Z", "allowedDependencies", values) };
        var context = new ViolationContext(path, constraints, contracts);
        path.Clear(); values.Clear(); constraints.Clear(); contracts.Clear();
        Assert.Equal(["Z", "A", "Z"], context.DependencyPath.Select(node => node.Identity));
        Assert.Equal(["A", "B"], Assert.Single(context.PolicyConstraints).Values);
        Assert.Equal(["contract:B/api"], context.PublicContracts);
        Assert.Throws<NotSupportedException>(() => ((IList<ViolationPathNode>)context.DependencyPath).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)context.PolicyConstraints[0].Values).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<PolicyConstraint>)context.PolicyConstraints).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)context.PublicContracts).Clear());
        Assert.Throws<ArgumentException>(() => new ViolationContext([], []));
        Assert.Throws<ArgumentException>(() => new ViolationContext([new("module", "A"), null!], []));
    }

    private static DependencyEdge Edge(DependencyEvidence evidence) => new(
        new(DependencyKind.Project, evidence.SourceProject, evidence.SourceProject),
        new(DependencyKind.Project, evidence.TargetProject!, evidence.TargetProject!), [evidence]);
}
