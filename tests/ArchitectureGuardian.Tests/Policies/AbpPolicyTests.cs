using ArchitectureGuardian.Configuration.Validation;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Policies;
using Xunit;

namespace ArchitectureGuardian.Tests.Policies;

/// <summary>Verifies the abp pack's layer template, the abp policy settings and their composition and overrides.</summary>
public sealed class AbpPolicyTests
{
    private const string Extends = """
        version: 1
        extends:
          - pack: abp
            version: 1.0.0

        """;

    private const string Declarations = """
        modules:
          - id: BookStore
            projects: ["src/**/*.csproj", "test/**/*.csproj"]
        layers:
          - id: Host
            projects: ["src/Acme.BookStore.Web/*.csproj"]
          - id: Tests
            projects: ["test/**/*.csproj"]

        """;

    /// <summary>The ABP startup template's projects, one per pack layer.</summary>
    private static readonly string[] TemplateProjects =
    [
        "src/Acme.BookStore.Application/Acme.BookStore.Application.csproj",
        "src/Acme.BookStore.Application.Contracts/Acme.BookStore.Application.Contracts.csproj",
        "src/Acme.BookStore.Domain/Acme.BookStore.Domain.csproj",
        "src/Acme.BookStore.Domain.Shared/Acme.BookStore.Domain.Shared.csproj",
        "src/Acme.BookStore.EntityFrameworkCore/Acme.BookStore.EntityFrameworkCore.csproj",
        "src/Acme.BookStore.HttpApi/Acme.BookStore.HttpApi.csproj",
        "src/Acme.BookStore.HttpApi.Client/Acme.BookStore.HttpApi.Client.csproj",
        "src/Acme.BookStore.Web/Acme.BookStore.Web.csproj",
        "test/Acme.BookStore.Domain.Tests/Acme.BookStore.Domain.Tests.csproj",
    ];

    /// <summary>The pack maps the startup template's projects to its layers and enforces ABP's dependency directions.</summary>
    [Fact]
    public void Pack_follows_the_abp_startup_template()
    {
        var policy = Load(Extends + Declarations);
        Assert.Equal(["ABP001", "ABP002", "ABP003", "ABP004", "ABP005", "AG002", "AG006"],
            policy.SeverityPolicy.RuleSeverities.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(DiagnosticSeverity.Warning, policy.SeverityPolicy.RuleSeverities["ABP004"]);
        Assert.Equal(DiagnosticSeverity.Warning, policy.SeverityPolicy.RuleSeverities["ABP005"]);
        Assert.Equal(["Application", "Domain", "Persistence", "Tests"], policy.Abp!.RepositoryLayers);
        Assert.Empty(policy.Abp.TenantFilterBypassLayers);
        var resolved = new ResolvedPolicy(policy, TemplateProjects);
        Assert.Equal(["Application", "ApplicationContracts", "Domain", "DomainShared", "Persistence", "HttpApi", "HttpApiClient", "Host", "Tests"],
            TemplateProjects.Select(project => resolved.Projects[project].LayerId));
        var paths = TemplateProjects.ToDictionary(project => resolved.Projects[project].LayerId, StringComparer.Ordinal);
        // Allowed directions from the ABP layering guide.
        Assert.True(resolved.AllowsLayerDependency(paths["Application"], paths["Domain"]));
        Assert.True(resolved.AllowsLayerDependency(paths["Application"], paths["ApplicationContracts"]));
        Assert.True(resolved.AllowsLayerDependency(paths["Persistence"], paths["Domain"]));
        Assert.True(resolved.AllowsLayerDependency(paths["HttpApi"], paths["ApplicationContracts"]));
        Assert.True(resolved.AllowsLayerDependency(paths["Host"], paths["Persistence"]));
        // Forbidden directions: contracts never see the domain, and the domain never sees the database provider.
        Assert.False(resolved.AllowsLayerDependency(paths["ApplicationContracts"], paths["Domain"]));
        Assert.False(resolved.AllowsLayerDependency(paths["Domain"], paths["Persistence"]));
        Assert.False(resolved.AllowsLayerDependency(paths["Application"], paths["Persistence"]));
        Assert.False(resolved.AllowsLayerDependency(paths["HttpApi"], paths["Domain"]));
        Assert.False(resolved.AllowsLayerDependency(paths["DomainShared"], paths["Domain"]));
        Assert.Contains("Microsoft.EntityFrameworkCore", policy.Layers.Single(layer => layer.Id == "Domain").DomainInfrastructure!.ForbiddenNamespaces);
        Assert.Contains("Microsoft.AspNetCore.Mvc", policy.Layers.Single(layer => layer.Id == "Application").DomainInfrastructure!.ForbiddenNamespaces);
    }

    /// <summary>The discovery template restates the embedded pack exactly, so proposals and the pack cannot drift apart.</summary>
    [Fact]
    public void Discovery_template_matches_the_embedded_pack()
    {
        var policy = Load(Extends + Declarations);
        Assert.Equal(AbpSolutionTemplate.Layers.Select(layer => layer.Id).Order(StringComparer.Ordinal), policy.Layers.Select(layer => layer.Id));
        foreach (var (id, allowed, forbidden) in AbpSolutionTemplate.Layers)
        {
            var layer = policy.Layers.Single(item => item.Id == id);
            Assert.Equal(allowed.Order(StringComparer.Ordinal), layer.AllowedDependencies);
            Assert.Equal(forbidden.Order(StringComparer.Ordinal), layer.DomainInfrastructure?.ForbiddenNamespaces ?? []);
        }

        Assert.Equal(AbpSolutionTemplate.Rules.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            policy.SeverityPolicy.RuleSeverities.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(AbpSolutionTemplate.RepositoryLayers, policy.Abp!.RepositoryLayers);
    }

    /// <summary>Policies scope and override the pack: fields compose independently, exclusions keep references valid.</summary>
    [Fact]
    public void Conventions_are_scoped_and_overridden_through_policy()
    {
        var policy = Load(Extends.Replace("    version: 1.0.0\n", "    version: 1.0.0\n    exclude: [\"layer:Tests\", \"rule:ABP004\"]\n", StringComparison.Ordinal)
            + Declarations.Replace("  - id: Tests\n    projects: [\"test/**/*.csproj\"]\n", "", StringComparison.Ordinal)
                .Replace(", \"test/**/*.csproj\"", "", StringComparison.Ordinal) + """
            abp:
              tenantFilterBypassLayers: [Host]
            rules:
              ABP005: error
              ABP002: warning

            """);
        // Excluding the layer removes it from the inherited repositoryLayers; the policy's bypass layers merge independently.
        Assert.Equal(["Application", "Domain", "Persistence"], policy.Abp!.RepositoryLayers);
        Assert.Equal(["Host"], policy.Abp.TenantFilterBypassLayers);
        Assert.DoesNotContain("ABP004", policy.SeverityPolicy.RuleSeverities.Keys);
        Assert.Equal(DiagnosticSeverity.Error, policy.SeverityPolicy.RuleSeverities["ABP005"]);
        Assert.Equal(DiagnosticSeverity.Warning, policy.SeverityPolicy.RuleSeverities["ABP002"]);
        Assert.DoesNotContain(policy.Layers, layer => layer.Id == "Tests");
    }

    /// <summary>The pack's host layer has no selectors, so the extending policy must name its composition roots.</summary>
    [Fact]
    public void Host_projects_are_required_from_the_policy()
    {
        var error = Assert.Throws<PolicyConfigurationException>(() => Load(Extends + "modules:\n  - id: BookStore\n    projects: [\"src/**/*.csproj\"]\n"));
        Assert.Contains("Host", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Invalid references and a selected ABP003 without repository layers fail at their source field.</summary>
    /// <param name="settings">The abp block, if any.</param>
    /// <param name="rule">The selected rule.</param>
    /// <param name="path">The expected policy path.</param>
    /// <param name="message">The expected message fragment.</param>
    [Theory]
    [InlineData("abp:\n  repositoryLayers: [Data]\n", "ABP001", "$.abp.repositoryLayers", "Unknown layer 'Data'")]
    [InlineData("abp:\n  tenantFilterBypassLayers: [Data]\n", "ABP005", "$.abp.tenantFilterBypassLayers", "Unknown layer 'Data'")]
    [InlineData("abp:\n  repositoryLayers: [App, App]\n", "ABP003", "$.abp.repositoryLayers", "Duplicate")]
    [InlineData("abp:\n  unknown: [x]\n", "ABP001", "$.abp.unknown", "Unknown or unsupported field")]
    [InlineData("", "ABP003", "$", "ABP003 is selected")]
    [InlineData("abp:\n  repositoryLayers: []\n", "ABP003", "$.abp.repositoryLayers", "ABP003 is selected")]
    public void Invalid_settings_fail_clearly(string settings, string rule, string path, string message)
    {
        const string standalone = """
            version: 1
            modules:
              - id: Shop
                projects: ["src/**/*.csproj"]
            layers:
              - id: App
                projects: ["src/**/*.csproj"]

            """;
        var error = Assert.Throws<PolicyConfigurationException>(() => Load(standalone + settings + $"rules:\n  {rule}: error\n"));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Equal(path, error.PolicyPath);
    }

    /// <summary>The Core policy enforces the same invariants for hosts that construct it directly.</summary>
    [Fact]
    public void Core_policy_rejects_unresolved_layers_and_missing_settings()
    {
        var rules = new Dictionary<string, DiagnosticSeverity> { ["ABP003"] = DiagnosticSeverity.Error };
        Assert.Throws<ArgumentException>(() => new ArchitecturePolicy([new("Shop", ["**"])], [new("All", ["**"])], rules));
        Assert.Throws<ArgumentException>(() => new ArchitecturePolicy([new("Shop", ["**"])], [new("All", ["**"])], rules, abp: new AbpPolicy(["Data"])));
        Assert.Throws<ArgumentException>(() => new ArchitecturePolicy([new("Shop", ["**"])], [new("All", ["**"])], rules,
            abp: new AbpPolicy(["All"], ["Data"])));
        Assert.Equal(["All"], new ArchitecturePolicy([new("Shop", ["**"])], [new("All", ["**"])], rules, abp: new AbpPolicy(["All"])).Abp!.RepositoryLayers);
    }

    /// <summary>ABP startup-template names map to the pack's layers and module prefixes; other names stay unresolved.</summary>
    /// <param name="name">The project name.</param>
    /// <param name="module">The expected module prefix, or null when unrecognized.</param>
    /// <param name="layer">The expected layer.</param>
    [Theory]
    [InlineData("Acme.BookStore.Domain.Shared", "Acme.BookStore", "DomainShared")]
    [InlineData("Acme.BookStore.Domain", "Acme.BookStore", "Domain")]
    [InlineData("Acme.BookStore.Application.Contracts", "Acme.BookStore", "ApplicationContracts")]
    [InlineData("Acme.BookStore.Application", "Acme.BookStore", "Application")]
    [InlineData("Acme.BookStore.EntityFrameworkCore", "Acme.BookStore", "Persistence")]
    [InlineData("Acme.BookStore.MongoDB", "Acme.BookStore", "Persistence")]
    [InlineData("Acme.BookStore.HttpApi", "Acme.BookStore", "HttpApi")]
    [InlineData("Acme.BookStore.HttpApi.Client", "Acme.BookStore", "HttpApiClient")]
    [InlineData("Acme.BookStore.HttpApi.Host", "Acme.BookStore", "Host")]
    [InlineData("Acme.BookStore.Web", "Acme.BookStore", "Host")]
    [InlineData("Acme.BookStore.Blazor.Client", "Acme.BookStore", "Host")]
    [InlineData("Acme.BookStore.DbMigrator", "Acme.BookStore", "Host")]
    [InlineData("Acme.BookStore.AuthServer", "Acme.BookStore", "Host")]
    [InlineData("Acme.BookStore.Domain.Tests", "Acme.BookStore", "Tests")]
    [InlineData("Acme.BookStore.TestBase", "Acme.BookStore", "Tests")]
    [InlineData("Acme.BookStore.HttpApi.Client.ConsoleTestApp", "Acme.BookStore", "Tests")]
    [InlineData("Acme.Web.Store.Domain", "Acme.Web.Store", "Domain")]
    [InlineData("Acme.BookStore", null, "")]
    [InlineData("Domain", null, "")]
    [InlineData("Acme.BookStore.Infrastructure", null, "")]
    public void Startup_template_names_are_classified(string name, string? module, string layer)
    {
        var result = AbpSolutionTemplate.Classify(name);
        if (module is null)
        {
            Assert.Null(result);
            return;
        }

        Assert.Equal((module, layer), result);
    }

    private static ArchitecturePolicy Load(string yaml) =>
        new YamlPolicyLoader().Parse(yaml, "policy.yaml", TestContext.Current.CancellationToken);
}
