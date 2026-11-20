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

    /// <summary>Cycles, inconsistent pins and identity mismatches are reported with the referencing entry.</summary>
    /// <param name="scenario">The invalid graph.</param>
    /// <param name="message">A required fragment of the error.</param>
    [Theory]
    [InlineData("cycle", "Pack extends cycle: a@1.0.0 → b@1.0.0 → a@1.0.0")]
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

    /// <summary>Hosts without file context accept built-in packs but reject local packs explicitly.</summary>
    [Fact]
    public void Local_packs_require_a_file_host()
    {
        var error = Assert.Throws<PolicyConfigurationException>(() => Load("version: 1\nextends:\n  - pack: a\n    version: 1.0.0\n    path: packs/a\n" + Modules));
        Assert.Contains("requires a host that reads files", error.Message, StringComparison.Ordinal);
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
