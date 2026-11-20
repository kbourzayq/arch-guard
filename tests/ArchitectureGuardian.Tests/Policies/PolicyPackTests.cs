using System.Security.Cryptography;
using System.Text;
using ArchitectureGuardian.Configuration.Validation;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Policy;
using ArchitectureGuardian.Policy.Documents;
using ArchitectureGuardian.Policy.Packs;
using Xunit;

namespace ArchitectureGuardian.Tests.Policies;

/// <summary>Verifies rule-pack manifests, pinned resolution and deterministic extends composition.</summary>
public sealed class PolicyPackTests
{
    private const string Modules = """
        modules:
          - id: Shop
            projects: ["src/**/*.csproj"]
        """;

    private static readonly string[] CleanProjects =
    [
        "src/Shop.Domain/Shop.Domain.csproj",
        "src/Shop.Application/Shop.Application.csproj",
        "src/Shop.Infrastructure/Shop.Infrastructure.csproj",
        "src/Shop.Api/Shop.Api.csproj",
    ];

    /// <summary>Every embedded pack is discoverable by its pinned label and composes into a valid policy.</summary>
    [Fact]
    public void Built_in_packs_are_versioned_and_compose()
    {
        Assert.Equal(["clean-architecture@1.0.0", "modular-monolith@1.0.0", "vertical-slice@1.0.0"], BuiltInPolicyPacks.Available);
        foreach (var label in BuiltInPolicyPacks.Available)
        {
            var parts = label.Split('@');
            var yaml = $"version: 1\nextends:\n  - pack: {parts[0]}\n    version: {parts[1]}\nmodules:\n  - id: App\n    projects: [\"app/*.csproj\"]\n";
            var document = Read(yaml);
            Assert.NotEmpty(document.Layers);
            Assert.NotEmpty(document.Rules);
        }
    }

    /// <summary>Released built-in pack versions are immutable: content changes require a new pack version.</summary>
    /// <param name="label">The embedded pack label.</param>
    /// <param name="sha256">The released manifest hash with LF line endings.</param>
    [Theory]
    [InlineData("clean-architecture@1.0.0", "d679701da3268e99bacc25c22c206405a6c1ef7596fba67bd64df4f9da7d5469")]
    [InlineData("modular-monolith@1.0.0", "98b39da2956301f66d0921b2dc4635e4c3169c8bc7051d1e5ab0ae0837de544e")]
    [InlineData("vertical-slice@1.0.0", "d239e7125409ed1501a49bd4dcda21e67e0805eaa8ebc560e666f9b78d14bf2b")]
    public void Released_built_in_packs_are_immutable(string label, string sha256)
    {
        var parts = label.Split('@');
        using var stream = typeof(BuiltInPolicyPacks).Assembly.GetManifestResourceStream($"packs/{parts[0]}/{parts[1]}/pack.yaml");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd().Replace("\r", "", StringComparison.Ordinal);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
    }

    /// <summary>Clean Architecture defaults enforce inward layer directions and AG006 domain restrictions.</summary>
    [Fact]
    public void Clean_architecture_defaults_enforce_declared_directions()
    {
        var policy = Load(Extend("clean-architecture") + Modules);
        Assert.Equal(["AG002", "AG006"], policy.SeverityPolicy.RuleSeverities.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["Api", "Application", "Domain", "Infrastructure"], policy.Layers.Select(layer => layer.Id));
        Assert.Contains("System.Data", policy.Layers.Single(layer => layer.Id == "Domain").DomainInfrastructure!.ForbiddenNamespaces);
        var resolved = new ResolvedPolicy(policy, CleanProjects);
        Assert.True(resolved.AllowsLayerDependency("src/Shop.Infrastructure/Shop.Infrastructure.csproj", "src/Shop.Domain/Shop.Domain.csproj"));
        Assert.True(resolved.AllowsLayerDependency("src/Shop.Api/Shop.Api.csproj", "src/Shop.Infrastructure/Shop.Infrastructure.csproj"));
        Assert.False(resolved.AllowsLayerDependency("src/Shop.Application/Shop.Application.csproj", "src/Shop.Infrastructure/Shop.Infrastructure.csproj"));
        Assert.False(resolved.AllowsLayerDependency("src/Shop.Domain/Shop.Domain.csproj", "src/Shop.Application/Shop.Application.csproj"));
    }

    /// <summary>A policy overrides individual inherited fields without restating the rest of a declaration.</summary>
    [Fact]
    public void Policy_overrides_patterns_severities_and_gate_field_by_field()
    {
        var policy = Load(Extend("clean-architecture") + Modules + """

            layers:
              - id: Domain
                projects: ["src/Core/*.csproj"]
              - id: Api
                projects: ["src/Web/*.csproj"]
            rules:
              AG006: warning
              AG005: error
            gate:
              failOn: warning
            """);
        var domain = policy.Layers.Single(layer => layer.Id == "Domain");
        Assert.Equal(["src/Core/*.csproj"], domain.Projects.Select(pattern => pattern.Value));
        Assert.NotNull(domain.DomainInfrastructure);
        Assert.Equal(["Application", "Domain", "Infrastructure"], policy.Layers.Single(layer => layer.Id == "Api").AllowedDependencies);
        Assert.Equal(DiagnosticSeverity.Warning, policy.SeverityPolicy.RuleSeverities["AG006"]);
        Assert.Equal(DiagnosticSeverity.Error, policy.SeverityPolicy.RuleSeverities["AG005"]);
        Assert.Equal(DiagnosticSeverity.Warning, policy.Gate.FailOn);
        _ = new ResolvedPolicy(policy, ["src/Core/Shop.csproj", "src/Shop.Application/Shop.Application.csproj",
            "src/Shop.Infrastructure/Shop.Infrastructure.csproj", "src/Web/Shop.csproj"]);
    }

    /// <summary>Exclusions remove a pack's declaration, the permissions targeting it, or a rule selection.</summary>
    [Fact]
    public void Exclusions_remove_declarations_permissions_and_rules()
    {
        var policy = Load(Extend("clean-architecture", "    exclude: [\"layer:Infrastructure\", \"rule:AG006\"]\n") + Modules);
        Assert.DoesNotContain(policy.Layers, layer => layer.Id == "Infrastructure");
        Assert.Equal(["Application", "Domain"], policy.Layers.Single(layer => layer.Id == "Api").AllowedDependencies);
        Assert.Equal(["AG002"], policy.SeverityPolicy.RuleSeverities.Keys);
        var error = Assert.Throws<PolicyConfigurationException>(() => Load(Extend("clean-architecture", "    exclude: [\"layer:Web\"]\n") + Modules));
        Assert.Contains("does not supply 'layer:Web'", error.Message, StringComparison.Ordinal);
        Assert.Equal("$.extends[0].exclude[0]", error.PolicyPath);
    }

    /// <summary>Sibling packs with conflicting values fail regardless of order until the policy overrides the setting.</summary>
    [Fact]
    public void Sibling_conflicts_are_order_independent_and_resolved_by_explicit_override()
    {
        var resolver = new MemoryResolver(
            ("packs/a", Pack("a", "layers:\n  - id: Core\n    projects: [\"src/*.csproj\"]\n")),
            ("packs/b", Pack("b", "layers:\n  - id: Core\n    projects: [\"lib/*.csproj\"]\n")));
        foreach (var order in new[] { "a,b", "b,a" })
        {
            var extends = string.Concat(order.Split(',').Select(id => $"  - pack: {id}\n    version: 1.0.0\n    path: packs/{id}\n"));
            var yaml = "version: 1\nextends:\n" + extends + "modules:\n  - id: App\n    projects: [\"src/*.csproj\"]\n";
            var error = Assert.Throws<PolicyReadException>(() => Read(yaml, resolver));
            Assert.Contains("set 'layers.Core.projects' to different values", error.Problem, StringComparison.Ordinal);
            Assert.Equal("$.extends", error.PolicyPath);
            var resolved = Read(yaml + "layers:\n  - id: Core\n    projects: [\"src/*.csproj\"]\n", resolver);
            Assert.Equal(["src/*.csproj"], resolved.Layers.Single().Projects.Select(pattern => pattern.Value));
        }
    }

    /// <summary>Equal values from a shared base (a diamond) are not conflicts and the base is resolved once.</summary>
    [Fact]
    public void Diamond_extends_with_equal_values_composes()
    {
        var resolver = new MemoryResolver(
            ("packs/a", Pack("a", "", "extends:\n  - pack: clean-architecture\n    version: 1.0.0\n")),
            ("packs/b", Pack("b", "", "extends:\n  - pack: clean-architecture\n    version: 1.0.0\n")));
        var document = Read("version: 1\nextends:\n  - pack: a\n    version: 1.0.0\n    path: packs/a\n  - pack: b\n    version: 1.0.0\n    path: packs/b\n" + Modules, resolver);
        Assert.Equal(4, document.Layers.Count);
        Assert.Equal(["AG002", "AG006"], document.Rules.Keys.Order(StringComparer.Ordinal));
    }

    /// <summary>Cycles, inconsistent pins and identity mismatches are reported with the referencing entry.</summary>
    /// <param name="scenario">The invalid graph.</param>
    /// <param name="message">A required fragment of the error.</param>
    [Theory]
    [InlineData("cycle", "Pack extends cycle: a@1.0.0 → b@1.0.0 → a@1.0.0")]
    [InlineData("versions", "is referenced as clean-architecture@1.0.0 (built-in) and clean-architecture@2.0.0")]
    [InlineData("mismatch", "The manifest declares 'a@1.0.1', but the policy pins 'a@1.0.0'")]
    [InlineData("missing", "was not found at 'packs/missing/pack.yaml'")]
    public void Invalid_pack_graphs_fail_clearly(string scenario, string message)
    {
        var resolver = new MemoryResolver(
            ("packs/a", scenario switch
            {
                "cycle" => Pack("a", "", "extends:\n  - pack: b\n    version: 1.0.0\n    path: packs/b\n"),
                "versions" => Pack("a", "", "extends:\n  - pack: clean-architecture\n    version: 2.0.0\n"),
                "mismatch" => Pack("a", "").Replace("version: 1.0.0", "version: 1.0.1", StringComparison.Ordinal),
                _ => Pack("a", ""),
            }),
            ("packs/b", Pack("b", "", "extends:\n  - pack: a\n    version: 1.0.0\n    path: packs/a\n")));
        var first = scenario == "missing" ? "  - pack: missing\n    version: 1.0.0\n    path: packs/missing\n"
            : "  - pack: a\n    version: 1.0.0\n    path: packs/a\n";
        var second = scenario == "versions" ? "  - pack: clean-architecture\n    version: 1.0.0\n" : "";
        var error = Assert.Throws<PolicyReadException>(() => Read("version: 1\nextends:\n" + second + first + Modules, resolver));
        Assert.Contains(message, error.Problem, StringComparison.Ordinal);
    }

    /// <summary>The 16-level extends limit holds whether or not a reused subtree was resolved earlier.</summary>
    /// <param name="levels">The chain length below the policy.</param>
    /// <param name="leafFirst">Whether the policy lists the chain's leaf before its head, so the leaf is cached first.</param>
    [Theory]
    [InlineData(16, false)]
    [InlineData(16, true)]
    [InlineData(17, false)]
    [InlineData(17, true)]
    public void Depth_limit_is_independent_of_extends_order(int levels, bool leafFirst)
    {
        var packs = Enumerable.Range(1, levels).Select(index => ($"packs/p{index}", Pack($"p{index}", "",
            index == levels ? "" : $"extends:\n  - pack: p{index + 1}\n    version: 1.0.0\n    path: packs/p{index + 1}\n"))).ToArray();
        var roots = leafFirst ? new[] { levels, 1 } : [1];
        var yaml = "version: 1\nextends:\n" + string.Concat(roots.Select(index => $"  - pack: p{index}\n    version: 1.0.0\n    path: packs/p{index}\n")) + Modules;
        AssertLimit(levels <= 16, () => Read(yaml + "\nlayers:\n  - id: All\n    projects: [\"src/*.csproj\"]\n", new MemoryResolver(packs)));
    }

    /// <summary>The 32-pack limit counts packs whose resolution is still in progress.</summary>
    /// <param name="total">The number of distinct packs in the graph.</param>
    /// <param name="chainFirst">Whether a four-pack chain precedes the standalone packs.</param>
    [Theory]
    [InlineData(32, false)]
    [InlineData(32, true)]
    [InlineData(33, false)]
    [InlineData(33, true)]
    public void Pack_count_limit_is_independent_of_extends_order(int total, bool chainFirst)
    {
        var chain = Enumerable.Range(1, 4).Select(index => ($"packs/c{index}", Pack($"c{index}", "",
            index == 4 ? "" : $"extends:\n  - pack: c{index + 1}\n    version: 1.0.0\n    path: packs/c{index + 1}\n")));
        var standalone = Enumerable.Range(1, total - 4).Select(index => ($"packs/s{index}", Pack($"s{index}", ""))).ToArray();
        var roots = standalone.Select(pack => pack.Item1.Substring(6)).ToList();
        roots.Insert(chainFirst ? 0 : roots.Count, "c1");
        var yaml = "version: 1\nextends:\n" + string.Concat(roots.Select(id => $"  - pack: {id}\n    version: 1.0.0\n    path: packs/{id}\n"))
            + Modules + "\nlayers:\n  - id: All\n    projects: [\"src/*.csproj\"]\n";
        AssertLimit(total <= 32, () => Read(yaml, new MemoryResolver([.. chain, .. standalone])));
    }

    /// <summary>Unknown permission targets inherited from a pack are reported at the manifest field that supplied them.</summary>
    /// <param name="declaration">The pack declaration with an invalid permission.</param>
    /// <param name="path">The expected manifest field path.</param>
    /// <param name="message">A required fragment of the error.</param>
    [Theory]
    [InlineData("layers:\n  - id: Core\n    projects: [\"src/*.csproj\"]\n    allowedDependencies: [Missing]\n",
        "$.layers[0].allowedDependencies", "Unknown layer 'Missing'")]
    [InlineData("modules:\n  - id: Core\n    projects: [\"lib/*.csproj\"]\n    allowedDependencies: [\"module:Missing\"]\n",
        "$.modules[0].allowedDependencies", "Unknown target 'module:Missing'")]
    public void Invalid_inherited_permissions_report_their_manifest(string declaration, string path, string message)
    {
        var resolver = new MemoryResolver(("packs/a", Pack("a", declaration)));
        var yaml = "version: 1\nextends:\n  - pack: a\n    version: 1.0.0\n    path: packs/a\n" + Modules
            + "\nlayers:\n  - id: All\n    projects: [\"app/*.csproj\"]\n";
        var error = Assert.Throws<PolicyReadException>(() => Read(yaml, resolver));
        Assert.Equal("packs/a/pack.yaml", error.SourcePath);
        Assert.Equal(path, error.PolicyPath);
        Assert.Contains(message, error.Problem, StringComparison.Ordinal);
    }

    /// <summary>Unknown packs, unpinned versions and incompatible manifests fail instead of being ignored.</summary>
    /// <param name="oldValue">The valid policy or manifest text to replace.</param>
    /// <param name="newValue">The invalid replacement.</param>
    /// <param name="message">A required fragment of the error.</param>
    [Theory]
    [InlineData("pack: a", "pack: unknown-pack", "Unknown built-in pack 'unknown-pack'")]
    [InlineData("version: 1.0.0\n    path", "version: ^1.0.0\n    path", "Pin an exact MAJOR.MINOR.PATCH version")]
    [InlineData("path: packs/a", "path: ../outside", "relative to the root policy directory")]
    [InlineData("policySchema: 1", "policySchema: 2", "targets policy schema '2'")]
    [InlineData("engine: 0.1.0", "engine: 999.0.0", "requires Architecture Guardian 999.0.0 or later")]
    [InlineData("AG002: error", "AG042: error", "requires rule 'AG042', which this engine does not provide")]
    [InlineData("manifest: 1", "manifest: 2", "Unsupported pack manifest version")]
    [InlineData("manifest: 1", "manifest: 1\nassemblies: [Custom.Rules.dll]", "Unknown or unsupported field 'assemblies'")]
    public void Unknown_or_incompatible_packs_fail_clearly(string oldValue, string newValue, string message)
    {
        var reference = "version: 1\nextends:\n  - pack: a\n    version: 1.0.0\n    path: packs/a\n" + Modules;
        var manifest = Pack("a", "");
        if (reference.Contains(oldValue, StringComparison.Ordinal))
        {
            reference = reference.Replace(oldValue, newValue, StringComparison.Ordinal);
            if (newValue.StartsWith("pack:", StringComparison.Ordinal))
            {
                reference = reference.Replace("    path: packs/a\n", "", StringComparison.Ordinal);
            }
        }
        else
        {
            manifest = manifest.Replace(oldValue, newValue, StringComparison.Ordinal);
        }

        var error = Assert.Throws<PolicyReadException>(() => Read(reference, new MemoryResolver(("packs/a", manifest))));
        Assert.Contains(message, error.Problem, StringComparison.Ordinal);
        Assert.True(error.Position.Line >= 1);
    }

    /// <summary>An unpinned built-in version lists the embedded versions.</summary>
    [Fact]
    public void Missing_built_in_version_lists_available_versions()
    {
        var error = Assert.Throws<PolicyConfigurationException>(() => Load(Extend("clean-architecture").Replace("1.0.0", "9.9.9", StringComparison.Ordinal) + Modules));
        Assert.Contains("has no version 9.9.9. Available versions: 1.0.0", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Partial declarations must be completed by a pack; extending allows omitting top-level collections.</summary>
    [Fact]
    public void Composed_policy_requires_complete_declarations()
    {
        var missingProjects = Assert.Throws<PolicyConfigurationException>(() => Load(Extend("clean-architecture") + "modules:\n  - id: Shop\n"));
        Assert.Contains("Missing required field 'projects' for module 'Shop'", missingProjects.Message, StringComparison.Ordinal);
        Assert.Equal("$.modules[0].projects", missingProjects.PolicyPath);
        var noModules = Assert.Throws<PolicyConfigurationException>(() => Load(Extend("clean-architecture")));
        Assert.Contains("declares no modules", noModules.Message, StringComparison.Ordinal);
        Assert.Throws<PolicyConfigurationException>(() => Load("version: 1\nmodules:\n  - id: Shop\nlayers: []\nrules: {}\n"));
    }

    /// <summary>Hosts without file context accept built-in packs but reject local packs explicitly.</summary>
    [Fact]
    public void Local_packs_require_a_file_host()
    {
        var error = Assert.Throws<PolicyConfigurationException>(() => Load("version: 1\nextends:\n  - pack: a\n    version: 1.0.0\n    path: packs/a\n" + Modules));
        Assert.Contains("requires a host that reads files", error.Message, StringComparison.Ordinal);
    }

    /// <summary>File loading resolves local packs below the policy directory and rejects links.</summary>
    [Fact]
    public async Task File_loader_resolves_confined_local_packs()
    {
        var root = Directory.CreateTempSubdirectory("archguard-packs-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "packs", "team"));
            await File.WriteAllTextAsync(Path.Combine(root, "packs", "team", "pack.yaml"),
                Pack("team", "", "extends:\n  - pack: clean-architecture\n    version: 1.0.0\n"), TestContext.Current.CancellationToken);
            var policyPath = Path.Combine(root, "architecture.guardian.yaml");
            await File.WriteAllTextAsync(policyPath, "version: 1\nextends:\n  - pack: team\n    version: 1.0.0\n    path: packs/team\n" + Modules,
                TestContext.Current.CancellationToken);
            var policy = await new YamlPolicyLoader().LoadAsync(policyPath, TestContext.Current.CancellationToken);
            Assert.Equal(["AG002", "AG006"], policy.SeverityPolicy.RuleSeverities.Keys.Order(StringComparer.Ordinal));

            await File.WriteAllTextAsync(policyPath, "version: 1\nextends:\n  - pack: team\n    version: 1.0.0\n    path: packs/other\n" + Modules,
                TestContext.Current.CancellationToken);
            var missing = await Assert.ThrowsAsync<PolicyConfigurationException>(() => new YamlPolicyLoader().LoadAsync(policyPath, TestContext.Current.CancellationToken));
            Assert.Contains("was not found", missing.Message, StringComparison.Ordinal);

            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "packs", "other"), Path.Combine(root, "packs", "team"));
                var link = await Assert.ThrowsAsync<PolicyConfigurationException>(() => new YamlPolicyLoader().LoadAsync(policyPath, TestContext.Current.CancellationToken));
                Assert.Contains("symbolic link", link.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Requested cancellation propagates before packs are read.</summary>
    [Fact]
    public void Cancellation_propagates_during_composition()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new PolicyDocumentReader().Parse(Extend("clean-architecture") + Modules,
            "architecture.guardian.yaml", null, cancellation.Token));
    }

    private static string Extend(string id, string extra = "") =>
        $"version: 1\nextends:\n  - pack: {id}\n    version: 1.0.0\n{extra}";

    private static string Pack(string id, string body, string extends = "") => $"""
        manifest: 1
        id: {id}
        version: 1.0.0
        requires:
          policySchema: 1
          engine: 0.1.0
        rules:
          AG002: error

        """ + extends + body;

    private static void AssertLimit(bool valid, Func<PolicyDocument> read)
    {
        if (valid)
        {
            Assert.NotEmpty(read().Layers);
            return;
        }

        var error = Assert.Throws<PolicyReadException>(read);
        Assert.Contains("at most 32 packs and 16 levels of extends", error.Problem, StringComparison.Ordinal);
    }

    private static ArchitecturePolicy Load(string yaml) =>
        new YamlPolicyLoader().Parse(yaml, "policy.yaml", TestContext.Current.CancellationToken);

    private static PolicyDocument Read(string yaml, IPolicyPackResolver? resolver = null) =>
        new PolicyDocumentReader().Parse(yaml, "policy.yaml", resolver, TestContext.Current.CancellationToken);

    /// <summary>Serves local manifests from memory, keyed by pack directory.</summary>
    /// <param name="packs">Pack directories and manifest content.</param>
    private sealed class MemoryResolver(params (string Path, string Yaml)[] packs) : IPolicyPackResolver
    {
        /// <inheritdoc />
        public PolicyPackSource? Resolve(PolicyPackRequest request, CancellationToken cancellationToken)
        {
            var match = packs.FirstOrDefault(pack => pack.Path == request.RelativePath);
            return match.Yaml is null ? null : new PolicyPackSource(request.ManifestPath, match.Yaml);
        }
    }
}
