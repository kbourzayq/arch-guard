using System.Xml.Linq;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Configuration.Validation;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Tests.Diagnostics;
using Xunit;

namespace ArchitectureGuardian.Tests.Policies;

/// <summary>Verifies strict YAML loading, deterministic project resolution and independent policy decisions.</summary>
public sealed class PolicyTests
{
    /// <summary>AG006 configuration is optional, immutable and scoped to each declared layer.</summary>
    [Fact]
    public void Domain_infrastructure_configuration_snapshots_explicit_names()
    {
        var yaml = ValidYaml.Replace("id: Application", "id: Application\n    domainInfrastructure:\n      forbiddenAssemblies: [Legacy.Data]\n      forbiddenNamespaces: [System.Data]\n      allowedNamespaces: [System.Data.Contracts]", StringComparison.Ordinal);
        var policy = new YamlPolicyLoader().Parse(yaml);
        var restrictions = policy.Layers.Single(layer => layer.Id == "Application").DomainInfrastructure!;
        Assert.Equal(new[] { "Legacy.Data" }, restrictions.ForbiddenAssemblies);
        Assert.Equal(new[] { "System.Data" }, restrictions.ForbiddenNamespaces);
        Assert.Equal(new[] { "System.Data.Contracts" }, restrictions.AllowedNamespaces);
        Assert.Null(policy.Layers.Single(layer => layer.Id == "Contracts").DomainInfrastructure);
        var names = new[] { "System.Data" };
        var snapshot = new DomainInfrastructurePolicy(allowedNamespaces: names);
        names[0] = "Changed";
        Assert.Equal("System.Data", Assert.Single(snapshot.AllowedNamespaces));
    }

    /// <summary>Malformed AG006 objects and exceptions fail with the nested field path.</summary>
    /// <param name="fields">Invalid nested configuration.</param>
    [Theory]
    [InlineData("forbiddenAssemblies: [Data.*]")]
    [InlineData("forbiddenNamespaces: [System..Data]")]
    [InlineData("allowedNamespaces: [System.Data, System.Data]")]
    [InlineData("allowedNamespaces: [System.*]")]
    [InlineData("allowedNamespaces: System.Data")]
    [InlineData("unknown: []")]
    public void Invalid_domain_infrastructure_configuration_is_rejected(string fields)
    {
        var yaml = ValidYaml.Replace("id: Application", "id: Application\n    domainInfrastructure:\n      " + fields, StringComparison.Ordinal);
        var exception = Assert.Throws<PolicyConfigurationException>(() => new YamlPolicyLoader().Parse(yaml));
        Assert.Contains("domainInfrastructure", exception.Message);
    }

    private const string ValidYaml = """
        version: 1
        modules:
          - id: Orders
            projects: ["src/Orders/*.csproj"]
            allowedDependencies: ["contract:Billing/api"]
          - id: Billing
            projects: ["src/Billing/*.csproj"]
            contracts:
              - id: api
                projects: ["src/Billing/Billing.Contracts.csproj"]
        layers:
          - id: Contracts
            projects: ["src/**/Billing.Contracts.csproj"]
          - id: Application
            projects: ["src/**/Orders.Application.csproj"]
            allowedDependencies: [Contracts]
          - id: Infrastructure
            projects: ["src/**/Billing.Infrastructure.csproj"]
            allowedDependencies: [Contracts]
        rules:
          AG001: warning
          AG002: error
        gate:
          failOn: warning
        """;

    private static readonly string[] ProjectPaths = [
        "src/Orders/Orders.Application.csproj",
        "src/Billing/Billing.Contracts.csproj",
        "src/Billing/Billing.Infrastructure.csproj",
    ];

    /// <summary>Checks direct contract permissions, directional layer permissions and normalization across inventory orders.</summary>
    [Fact]
    public void Contract_and_layer_permissions_are_independent_directional_and_deterministic()
    {
        var policy = new YamlPolicyLoader().Parse(ValidYaml);
        var resolved = new ResolvedPolicy(policy, ProjectPaths.Reverse());
        var windowsPaths = new ResolvedPolicy(policy, ProjectPaths.Select(path => path.Replace('/', '\\')));

        Assert.Equal(1, policy.Version);
        Assert.Equal(resolved.Projects.Values, windowsPaths.Projects.Values);
        Assert.True(resolved.AllowsModuleDependency(ProjectPaths[0], ProjectPaths[1]));
        Assert.False(resolved.AllowsModuleDependency(ProjectPaths[0], ProjectPaths[2]));
        Assert.False(resolved.AllowsModuleDependency(ProjectPaths[1], ProjectPaths[0]));
        Assert.True(resolved.AllowsModuleDependency(ProjectPaths[1], ProjectPaths[2]));
        Assert.True(resolved.AllowsLayerDependency(ProjectPaths[0], ProjectPaths[1]));
        Assert.False(resolved.AllowsLayerDependency(ProjectPaths[1], ProjectPaths[0]));
        Assert.False(resolved.AllowsLayerDependency(ProjectPaths[1], ProjectPaths[2]));
        Assert.Equal("api", resolved.Projects[ProjectPaths[1]].ContractId);
        Assert.Null(resolved.Projects[ProjectPaths[2]].ContractId);
        Assert.Throws<ArgumentException>(() => resolved.AllowsModuleDependency("missing.csproj", ProjectPaths[0]));
    }

    /// <summary>Checks that whole-module access includes implementation projects and is never inferred transitively.</summary>
    [Fact]
    public void Whole_module_permissions_require_an_explicit_module_reference()
    {
        var policy = new YamlPolicyLoader().Parse(ValidYaml.Replace("contract:Billing/api", "module:Billing", StringComparison.Ordinal));
        var resolved = new ResolvedPolicy(policy, ProjectPaths);

        Assert.True(resolved.AllowsModuleDependency(ProjectPaths[0], ProjectPaths[2]));
        Assert.False(resolved.AllowsLayerDependency(ProjectPaths[0], ProjectPaths[2]));
    }

    /// <summary>Fixes portable glob semantics, including zero directories for ** and one character for ?.</summary>
    /// <param name="pattern">The complete path selector.</param>
    /// <param name="path">The identity being matched.</param>
    /// <param name="matches">The expected case-sensitive result.</param>
    [Theory]
    [InlineData("src/**/*.csproj", "src/A.csproj", true)]
    [InlineData("src/**/*.csproj", "src/a/b/A.csproj", true)]
    [InlineData("src/*.csproj", "src/a/A.csproj", false)]
    [InlineData("src/A?.csproj", "src/A1.csproj", true)]
    [InlineData("src/A?.csproj", "src/A10.csproj", false)]
    [InlineData("src/A.csproj", "src/a.csproj", false)]
    [InlineData("**/A.csproj", "A.csproj", true)]
    [InlineData("src/**", "src/a/A.csproj", true)]
    [InlineData("src/A*.csproj", "src/Another.csproj", true)]
    [InlineData("src/A*.csproj", "src/B.csproj", false)]
    [InlineData("*.csproj", "A.csproj", true)]
    [InlineData("src/A.csproj", "src\\A.csproj", true)]
    public void Globs_have_defined_segment_and_case_semantics(string pattern, string path, bool matches) =>
        Assert.Equal(matches, new ProjectPattern(pattern).IsMatch(path));

    /// <summary>Prefix rejection must not bypass validation of a caller-supplied identity.</summary>
    [Fact]
    public void Nonmatching_prefix_still_rejects_invalid_identity() =>
        Assert.Throws<ArgumentException>(() => new ProjectPattern("src/*.csproj").IsMatch("other/../A.csproj"));

    /// <summary>Rejects unsupported glob forms instead of assigning broader or platform-dependent meanings.</summary>
    /// <param name="pattern">An invalid selector.</param>
    [Theory]
    [InlineData("/src/*.csproj")]
    [InlineData("C:/src/*.csproj")]
    [InlineData("src\\*.csproj")]
    [InlineData("./src/*.csproj")]
    [InlineData("src/../*.csproj")]
    [InlineData("src//*.csproj")]
    [InlineData("src/**.csproj")]
    [InlineData("src/[AB].csproj")]
    [InlineData("!src/*.csproj")]
    [InlineData("src/{A,B}.csproj")]
    public void Unsupported_selectors_are_rejected(string pattern) => Assert.Throws<ArgumentException>(() => new ProjectPattern(pattern));

    /// <summary>Requires actionable document and field context for unsupported versions, settings, types and references.</summary>
    /// <param name="oldValue">The valid text to replace.</param>
    /// <param name="newValue">The invalid replacement.</param>
    /// <param name="message">A useful fragment required in the error.</param>
    [Theory]
    [InlineData("version: 1", "version: 2", "version")]
    [InlineData("version: 1", "version: \"1\"", "version")]
    [InlineData("AG001: warning", "AG999: warning", "AG999")]
    [InlineData("AG001: warning", "ag001: warning", "ag001")]
    [InlineData("AG001: warning", "AG001: off", "severities")]
    [InlineData("AG001: warning", "AG001: 1", "string")]
    [InlineData("AG001: warning", "AG001: null", "string")]
    [InlineData("contract:Billing/api", "contract:Billing/missing", "Unknown target")]
    [InlineData("contract:Billing/api", "Billing", "module:ID")]
    [InlineData("allowedDependencies: [Contracts]", "allowedDependencies: [Missing]", "Unknown layer")]
    [InlineData("failOn: warning", "failOn: none", "severities")]
    [InlineData("failOn: warning", "failOn: warning\n  allowIncomplete: true", "allowIncomplete")]
    [InlineData("version: 1", "version: 1\nextends: base.yaml", "extends")]
    [InlineData("AG001: warning", "AG001: { severity: warning }", "string")]
    [InlineData("id: Orders", "id: Billing", "Duplicate module")]
    [InlineData("projects: [\"src/Orders/*.csproj\"]", "projects: []", "nonempty")]
    [InlineData("id: Orders", "id: true", "string")]
    public void Invalid_configuration_has_actionable_errors(string oldValue, string newValue, string message)
    {
        var exception = Assert.Throws<PolicyConfigurationException>(() => new YamlPolicyLoader()
            .Parse(ValidYaml.Replace(oldValue, newValue, StringComparison.Ordinal), "policy.yaml"));

        Assert.Contains(message, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("policy.yaml", exception.Location.Path);
        Assert.True(exception.Location.StartLine >= 1);
        Assert.True(exception.Location.StartColumn >= 1);
        Assert.StartsWith("$", exception.PolicyPath);
    }

    /// <summary>Rejects ambiguous YAML constructs and malformed documents before model construction.</summary>
    /// <param name="yaml">A document outside the supported subset.</param>
    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("version: [")]
    [InlineData("version: 1\nversion: 1")]
    [InlineData("version: 1\n---\nversion: 1")]
    [InlineData("version: &v 1\nother: *v")]
    [InlineData("version: !!int 1")]
    [InlineData("%YAML 1.1\n---\nversion: 1")]
    [InlineData("<<: {}")]
    public void Ambiguous_or_malformed_yaml_is_rejected(string yaml) =>
        Assert.Throws<PolicyConfigurationException>(() => new YamlPolicyLoader().Parse(yaml));

    /// <summary>Confirms positions are one-based and identify the unsupported field.</summary>
    [Fact]
    public void Errors_retain_exact_field_positions()
    {
        var exception = Assert.Throws<PolicyConfigurationException>(() => new YamlPolicyLoader().Parse("version: 1\nunknown: true"));

        Assert.Equal(2, exception.Location.StartLine);
        Assert.Equal(1, exception.Location.StartColumn);
        Assert.Equal("$.unknown", exception.PolicyPath);
    }

    /// <summary>Rejects inventories that would silently reduce coverage or assign ambiguous ownership.</summary>
    [Fact]
    public void Inventory_resolution_rejects_unmatched_overlapping_empty_and_duplicate_projects()
    {
        var loader = new YamlPolicyLoader();
        var policy = loader.Parse(ValidYaml);
        Assert.Throws<ArgumentException>(() => new ResolvedPolicy(policy, []));
        Assert.Throws<ArgumentException>(() => new ResolvedPolicy(policy, ProjectPaths.Append(ProjectPaths[0])));
        Assert.Throws<ArgumentException>(() => new ResolvedPolicy(policy, ProjectPaths.Append("extra.csproj")));
        Assert.Throws<ArgumentException>(() => new ResolvedPolicy(policy, ProjectPaths.Skip(1)));
        var overlap = loader.Parse(ValidYaml.Replace("src/Orders/*.csproj", "src/**/*.csproj", StringComparison.Ordinal));
        Assert.Contains("exactly one", Assert.Throws<ArgumentException>(() => new ResolvedPolicy(overlap, ProjectPaths)).Message);
        var foreignContract = loader.Parse(ValidYaml.Replace("projects: [\"src/Billing/Billing.Contracts.csproj\"]",
            "projects: [\"src/Orders/Orders.Application.csproj\"]", StringComparison.Ordinal));
        Assert.Contains("outside its module", Assert.Throws<ArgumentException>(() => new ResolvedPolicy(foreignContract, ProjectPaths)).Message);
    }

    /// <summary>Rejects overlapping layer and contract ownership while allowing selector unions within a single owner.</summary>
    [Fact]
    public void Layer_and_contract_ownership_are_unambiguous()
    {
        var projects = new[] { "src/App.csproj" };
        var module = new ModulePolicy("App", ["src/*.csproj", "src/App.csproj"]);
        var layer = new LayerPolicy("All", ["**/*.csproj"]);
        var rules = new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Error };
        Assert.Single(new ResolvedPolicy(new([module], [layer], rules), projects).Projects);
        var overlappingLayers = new ArchitecturePolicy([module], [layer, new("Other", ["src/*.csproj"])], rules);
        Assert.Contains("exactly one", Assert.Throws<ArgumentException>(() => new ResolvedPolicy(overlappingLayers, projects)).Message);
        var overlappingContracts = new ModulePolicy("App", ["src/*.csproj"],
            [new("first", ["src/*.csproj"]), new("second", ["src/*.csproj"])]);
        Assert.Contains("multiple public contracts", Assert.Throws<ArgumentException>(() => new ResolvedPolicy(
            new([overlappingContracts], [layer], rules), projects)).Message);
    }

    /// <summary>Loads both published examples and resolves their intended complete inventories.</summary>
    [Fact]
    public async Task Published_examples_load_and_resolve()
    {
        var root = RepositoryRoot();
        var loader = new YamlPolicyLoader();
        var repositoryPolicy = await loader.LoadAsync(Path.Combine(root, "architecture.guardian.yaml"));
        var projects = XDocument.Load(Path.Combine(root, "ArchitectureGuardian.slnx")).Descendants("Project")
            .Select(project => project.Attribute("Path")!.Value);
        var resolved = new ResolvedPolicy(repositoryPolicy, projects);
        Assert.Equal(9, resolved.Projects.Count);
        Assert.Equal("Mcp", resolved.Projects["src/ArchitectureGuardian.Mcp/ArchitectureGuardian.Mcp.csproj"].LayerId);
        var example = await loader.LoadAsync(Path.Combine(root, "docs", "examples", "modular-monolith", "architecture.guardian.yaml"));
        Assert.Equal(3, new ResolvedPolicy(example, ProjectPaths).Projects.Count);
    }

    /// <summary>Preserves read failures and cancellation instead of returning a fallback policy.</summary>
    [Fact]
    public async Task Loading_preserves_io_failures_and_cancellation()
    {
        var loader = new YamlPolicyLoader();
        await Assert.ThrowsAsync<FileNotFoundException>(() => loader.LoadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yaml")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => loader.Parse(ValidYaml, cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loader.LoadAsync(
            Path.Combine(RepositoryRoot(), "architecture.guardian.yaml"), cancellation.Token));
    }

    /// <summary>Applies explicit rule selection before discovery and severity afterward; unavailable selected rules fail.</summary>
    [Fact]
    public async Task Rule_selection_and_severity_are_separate_from_discovery()
    {
        var policy = new YamlPolicyLoader().Parse(ValidYaml.Replace("  AG002: error\n", "", StringComparison.Ordinal));
        var first = new FindingRule(RuleCatalog.ModuleDependencies);
        var excluded = new FindingRule(RuleCatalog.ModuleCycles);
        var selected = policy.SelectRules<string>([excluded, first]);
        var service = new AnalysisService<string>(selected);
        var result = await service.AnalyzeAsync(new("context", policy.SeverityPolicy));

        Assert.Equal("AG001", Assert.Single(selected).Descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(result.Diagnostics).Severity);
        Assert.False(policy.Gate.Passes(result));
        Assert.Equal(0, excluded.Calls);
        Assert.Throws<ArgumentException>(() => policy.SelectRules<string>([excluded]));
    }

    /// <summary>Keeps coverage failures separate from threshold settings and checks inclusive severity boundaries.</summary>
    [Fact]
    public void Gates_never_accept_missing_coverage_and_do_not_mutate_results()
    {
        var warning = new Diagnostic(DiagnosticTests.Finding(), DiagnosticSeverity.Warning);
        var complete = new AnalysisResult(AnalysisStatus.Completed, [warning]);
        Assert.True(new GatePolicy().Passes(complete));
        Assert.False(new GatePolicy(DiagnosticSeverity.Warning).Passes(complete));
        Assert.False(new GatePolicy(DiagnosticSeverity.Info).Passes(complete));
        Assert.True(new GatePolicy(DiagnosticSeverity.Info).Passes(new(AnalysisStatus.Completed)));
        foreach (var status in new[] { AnalysisStatus.Incomplete, AnalysisStatus.Failed, AnalysisStatus.Cancelled })
        {
            var result = new AnalysisResult(status, issues: [new("coverage", "Coverage is unavailable.")]);
            Assert.False(new GatePolicy().Passes(result));
        }

        Assert.Equal(DiagnosticSeverity.Warning, Assert.Single(complete.Diagnostics).Severity);
    }

    /// <summary>Prevents callers from changing policy input after construction.</summary>
    [Fact]
    public void Policies_snapshot_input_collections()
    {
        var patterns = new List<string> { "**/*.csproj" };
        var modules = new List<ModulePolicy> { new("App", patterns) };
        var rules = new Dictionary<string, DiagnosticSeverity> { ["AG001"] = DiagnosticSeverity.Warning };
        var policy = new ArchitecturePolicy(modules, [new("All", patterns)], rules);
        patterns.Clear();
        modules.Clear();
        rules.Clear();

        Assert.Single(policy.Modules);
        Assert.Single(policy.Modules[0].Projects);
        Assert.Single(policy.SeverityPolicy.RuleSeverities);
        Assert.Throws<NotSupportedException>(() => ((IList<ModulePolicy>)policy.Modules).Clear());
    }

    /// <summary>Loads module/layer restrictions, snapshots inputs and keeps existing policies compatible.</summary>
    [Fact]
    public void Forbidden_references_are_explicit_immutable_policy_data()
    {
        var yaml = ValidYaml.Replace("allowedDependencies: [\"contract:Billing/api\"]",
            "allowedDependencies: [\"contract:Billing/api\"]\n    forbiddenAssemblies: [Forbidden.Library]\n    forbiddenNamespaces: [System.Net]", StringComparison.Ordinal)
            .Replace("allowedDependencies: [Contracts]", "allowedDependencies: [Contracts]\n    forbiddenNamespaces: [System.Data]", StringComparison.Ordinal);
        var policy = new YamlPolicyLoader().Parse(yaml);
        var orders = policy.Modules.Single(module => module.Id == "Orders");
        Assert.Equal("Forbidden.Library", Assert.Single(orders.ForbiddenAssemblies));
        Assert.Equal("System.Net", Assert.Single(orders.ForbiddenNamespaces));
        Assert.All(policy.Layers.Where(layer => layer.AllowedDependencies.Count > 0),
            layer => Assert.Equal("System.Data", Assert.Single(layer.ForbiddenNamespaces)));
        var names = new List<string> { "System.Net" };
        var module = new ModulePolicy("App", ["App.csproj"], forbiddenNamespaces: names);
        names.Clear();
        Assert.Single(module.ForbiddenNamespaces);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)module.ForbiddenNamespaces).Clear());
        Assert.All(new YamlPolicyLoader().Parse(ValidYaml).Modules, item => Assert.Empty(item.ForbiddenNamespaces));
    }

    /// <summary>Rejects ambiguous patterns, malformed identities, duplicates and non-array restriction fields.</summary>
    /// <param name="field">An invalid module restriction declaration.</param>
    [Theory]
    [InlineData("forbiddenAssemblies: [System.*]")]
    [InlineData("forbiddenAssemblies: ['Library, Version=1.0.0.0']")]
    [InlineData("forbiddenAssemblies: [Library, Library]")]
    [InlineData("forbiddenNamespaces: [System..Net]")]
    [InlineData("forbiddenNamespaces: [System.Net.*]")]
    [InlineData("forbiddenNamespaces: [global::System.Net]")]
    [InlineData("forbiddenNamespaces: [1Invalid.Name]")]
    [InlineData("forbiddenNamespaces: System.Net")]
    public void Invalid_reference_restrictions_are_actionable(string field)
    {
        var yaml = ValidYaml.Replace("allowedDependencies: [\"contract:Billing/api\"]", field, StringComparison.Ordinal);
        var error = Assert.Throws<PolicyConfigurationException>(() => new YamlPolicyLoader().Parse(yaml));
        Assert.Contains("modules", error.Message);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArchitectureGuardian.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Cannot find the repository root.");
    }

    /// <summary>Supplies a finding without consulting severity or gate policy.</summary>
    private sealed class FindingRule(RuleDescriptor descriptor) : IArchitectureRule<string>
    {
        /// <inheritdoc/>
        public RuleDescriptor Descriptor { get; } = descriptor;

        /// <summary>Gets the number of discovery invocations.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc/>
        public Task<RuleEvaluationResult> EvaluateAsync(string context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new RuleEvaluationResult(AnalysisStatus.Completed, [DiagnosticTests.Finding(Descriptor)]));
        }
    }
}
