using ArchitectureGuardian.Configuration.Validation;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Policy;
using ArchitectureGuardian.Policy.Packs;
using Xunit;

namespace ArchitectureGuardian.Tests.Policies;

/// <summary>Verifies the entityFrameworkCore policy settings, their references and their pack composition.</summary>
public sealed class EntityFrameworkCorePolicyTests
{
    private const string Declarations = """
        version: 1
        modules:
          - id: Shop
            projects: ["src/**/*.csproj"]
        layers:
          - id: Application
            projects: ["src/App/*.csproj"]
          - id: Infrastructure
            projects: ["src/Data/*.csproj"]

        """;

    /// <summary>Declared settings reach the Core policy in ordinal order and select no rule on their own.</summary>
    [Fact]
    public void Settings_are_loaded_in_ordinal_order()
    {
        var policy = Load(Declarations + """
            entityFrameworkCore:
              persistenceLayers: [Infrastructure]
              forbiddenApis: [IgnoreQueryFilters, FromSqlRaw]
            rules:
              EF001: warning
              EF002: error
              EF003: error
            """);
        Assert.Equal(["Infrastructure"], policy.EntityFrameworkCore!.PersistenceLayers);
        Assert.Equal(["FromSqlRaw", "IgnoreQueryFilters"], policy.EntityFrameworkCore.ForbiddenApis);
        Assert.Equal(DiagnosticSeverity.Warning, policy.SeverityPolicy.RuleSeverities["EF001"]);
        Assert.Null(Load(Declarations + "rules:\n  EF001: error\n").EntityFrameworkCore);
    }

    /// <summary>Invalid references, values and missing settings for selected rules fail at their source field.</summary>
    /// <param name="settings">The entityFrameworkCore block, if any.</param>
    /// <param name="rule">The selected rule.</param>
    /// <param name="path">The expected policy path.</param>
    /// <param name="message">The expected message fragment.</param>
    [Theory]
    [InlineData("entityFrameworkCore:\n  persistenceLayers: [Data]\n", "EF001", "$.entityFrameworkCore.persistenceLayers", "Unknown layer 'Data'")]
    [InlineData("entityFrameworkCore:\n  forbiddenApis: [\"Microsoft.EntityFrameworkCore.FromSqlRaw\"]\n", "EF003", "$.entityFrameworkCore.forbiddenApis", "method names")]
    [InlineData("entityFrameworkCore:\n  forbiddenApis: [FromSqlRaw, FromSqlRaw]\n", "EF003", "$.entityFrameworkCore.forbiddenApis", "Duplicate")]
    [InlineData("entityFrameworkCore:\n  unknown: [x]\n", "EF001", "$.entityFrameworkCore.unknown", "Unknown or unsupported field")]
    [InlineData("", "EF002", "$", "EF002 is selected")]
    [InlineData("entityFrameworkCore:\n  persistenceLayers: [Infrastructure]\n", "EF003", "$", "EF003 is selected")]
    public void Invalid_settings_fail_clearly(string settings, string rule, string path, string message)
    {
        var error = Assert.Throws<PolicyConfigurationException>(() => Load(Declarations + settings + $"rules:\n  {rule}: error\n"));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Equal(path, error.PolicyPath);
    }

    /// <summary>The Core policy enforces the same invariants for hosts that construct it directly.</summary>
    [Fact]
    public void Core_policy_rejects_unresolved_layers_and_missing_settings()
    {
        var rules = new Dictionary<string, DiagnosticSeverity> { ["EF002"] = DiagnosticSeverity.Error };
        Assert.Throws<ArgumentException>(() => new ArchitecturePolicy([new("Shop", ["**"])], [new("All", ["**"])], rules));
        Assert.Throws<ArgumentException>(() => new ArchitecturePolicy([new("Shop", ["**"])], [new("All", ["**"])], rules,
            entityFrameworkCore: new EntityFrameworkCorePolicy(["Data"])));
        Assert.Equal(["All"], new ArchitecturePolicy([new("Shop", ["**"])], [new("All", ["**"])], rules,
            entityFrameworkCore: new EntityFrameworkCorePolicy(["All"])).EntityFrameworkCore!.PersistenceLayers);
    }

    /// <summary>Packs supply settings field by field; excluding a layer also removes it from the persistence layers.</summary>
    [Fact]
    public void Packs_compose_settings_field_by_field_and_exclusions_keep_references_valid()
    {
        var pack = """
            manifest: 1
            id: persistence
            version: 1.0.0
            requires:
              policySchema: 1
              engine: 0.1.0
            rules:
              EF001: error
            layers:
              - id: Data
                projects: ["src/Data/*.csproj"]
              - id: Legacy
                projects: ["src/Legacy/*.csproj"]
            entityFrameworkCore:
              persistenceLayers: [Data, Legacy]
              forbiddenApis: [FromSqlRaw]
            """;
        var resolver = new MemoryResolver(pack);
        var root = """
            version: 1
            extends:
              - pack: persistence
                version: 1.0.0
                path: packs/persistence
                exclude: ["layer:Legacy"]
            modules:
              - id: Shop
                projects: ["src/**/*.csproj"]
            entityFrameworkCore:
              forbiddenApis: [IgnoreQueryFilters]

            """;
        var document = new PolicyDocumentReader().Parse(root, "policy.yaml", resolver, TestContext.Current.CancellationToken);
        Assert.Equal(["Data"], document.EntityFrameworkCore!.PersistenceLayers);
        Assert.Equal(["IgnoreQueryFilters"], document.EntityFrameworkCore.ForbiddenApis);
    }

    private static ArchitecturePolicy Load(string yaml) =>
        new YamlPolicyLoader().Parse(yaml, "policy.yaml", TestContext.Current.CancellationToken);

    /// <summary>Serves one local manifest from memory.</summary>
    /// <param name="yaml">The manifest content.</param>
    private sealed class MemoryResolver(string yaml) : IPolicyPackResolver
    {
        /// <inheritdoc />
        public PolicyPackSource? Resolve(PolicyPackRequest request, CancellationToken cancellationToken) =>
            new(request.ManifestPath, yaml);
    }
}
