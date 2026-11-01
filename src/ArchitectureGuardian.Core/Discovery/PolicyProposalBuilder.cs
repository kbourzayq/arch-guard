using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Discovery;

/// <summary>Infers conservative, project-based intentions without treating observed dependencies as permissions.</summary>
public sealed class PolicyProposalBuilder
{
    private static readonly IReadOnlyDictionary<string, string> Roles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Domain"] = "Domain",
        ["Application"] = "Application",
        ["Infrastructure"] = "Infrastructure",
        ["Persistence"] = "Infrastructure",
        ["Contracts"] = "Contracts",
        ["Abstractions"] = "Contracts",
        ["Host"] = "Host",
        ["Api"] = "Host",
        ["Web"] = "Host",
        ["Tests"] = "Tests",
    };

    /// <summary>Suggests ownership and styles from declarations; incomplete evidence never produces YAML intentions.</summary>
    /// <param name="observations">Policy-independent, verified declarations and dependencies.</param>
    /// <param name="cancellationToken">Cooperative cancellation during project inference.</param>
    /// <returns>A reviewable policy or actionable unresolved ownership, retaining every project in the inventory.</returns>
    public PolicyProposal Build(DependencyInventoryResult observations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observations);
        var ownership = new List<DiscoveryOwnership>();
        var inferences = new List<DiscoveryInference>();
        var questions = new List<string>();
        foreach (var project in observations.Inventory?.Projects ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = project.Id.Split('/')[^1];
            var name = fileName[..^".csproj".Length];
            var namespaces = project.Assembly.Namespaces.Where(ns => ns.Types.Count > 0 && ns.Name.Length > 0).Select(ns => ns.Name).ToArray();
            var signals = new[] { name }.Concat(project.Id.Split('/').SkipLast(1)).Concat(namespaces).ToArray();
            var roles = signals.SelectMany(signal => signal.Split('.', '-', '_')).Where(Roles.ContainsKey)
                .Select(token => Roles[token]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)) { roles = ["Tests"]; }
            if (name != project.Assembly.Name)
            {
                inferences.Add(new("naming-inconsistency", project.Id, "high", [$"Project filename: {name}", $"Evaluated assembly: {project.Assembly.Name}"]));
            }
            if (roles.Length != 1)
            {
                questions.Add($"{project.Id}: review ownership; architectural roles are {(roles.Length == 0 ? "unrecognized" : "ambiguous (" + string.Join(", ", roles) + ")")}. Supply explicit project-based module/layer mappings manually.");
                continue;
            }
            var role = roles[0];
            var moduleCandidates = new[] { name }.Concat(namespaces).Select(ModulePrefix)
                .Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
            // The project name is the strongest naming signal. Namespaces can corroborate legacy layouts,
            // but disagreement is left to the developer instead of silently choosing one owner.
            var module = ModulePrefix(name);
            if (module is null && moduleCandidates.Length == 1) { module = moduleCandidates[0]; }
            if (module is null || !ValidId(module) || moduleCandidates.Any(candidate => candidate != module && !candidate.EndsWith("." + module, StringComparison.Ordinal)))
            {
                questions.Add($"{project.Id}: module naming is ambiguous or absent; review namespaces and choose an explicit module identity.");
                continue;
            }
            var contract = role == "Contracts" && project.Assembly.Namespaces.SelectMany(ns => ns.Types).Any(type => type.IsPublic) ? "api" : null;
            ownership.Add(new(project.Id, module, role, contract));
            inferences.Add(new("ownership", project.Id, "medium", signals.Where(signal => signal.Split('.', '-', '_').Any(Roles.ContainsKey))
                .Select(signal => "Role-bearing name: " + signal).Append($"Candidate: module {module}, layer {role}")));
            if (contract is not null)
            {
                inferences.Add(new("contract", project.Id, "medium", ["Explicit Contracts/Abstractions role and public source declarations; review the entire project's public API before adoption."]));
                var consumers = observations.Graph?.Edges.Where(edge => edge.Target.ProjectPath == project.Id && edge.Source.ProjectPath != project.Id)
                    .Select(edge => edge.Source.ProjectPath).Where(path => path is not null).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [];
                if (consumers.Length > 1) { inferences.Add(new("shared-abstractions", project.Id, "low", consumers.Select(path => "Observed consumer: " + path))); }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var layerIds = ownership.Select(item => item.Layer).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var moduleIds = ownership.Select(item => item.Module).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (new[] { "Domain", "Application", "Infrastructure" }.All(layerIds.Contains))
        {
            inferences.Add(new("style", "Clean Architecture", "medium", ownership.Select(item => $"{item.ProjectPath}: {item.Layer}")));
        }
        if (moduleIds.Length > 1 && ownership.GroupBy(item => item.Module).Any(group => group.Count() > 1))
        {
            inferences.Add(new("style", "Modular Monolith", "medium", ownership.Select(item => $"{item.ProjectPath}: module {item.Module}")));
        }
        var featureSignals = observations.Inventory?.Projects.SelectMany(project => project.Assembly.Namespaces
            .Where(ns => ns.Name.Split('.').Contains("Features", StringComparer.Ordinal)).Select(ns => $"{project.Id}: {ns.Name}")).ToArray() ?? [];
        if (featureSignals.Length > 0) { inferences.Add(new("style", "Vertical Slice", "low", featureSignals)); }
        if (!inferences.Any(item => item.Kind == "style"))
        {
            inferences.Add(new("style", "Custom", "low", ownership.Select(item => $"{item.ProjectPath}: {item.Layer}")));
        }
        questions.Add("Review module ownership, public contracts, technology restrictions and directional permissions; observed edges never authorize access.");
        questions.Add("The v1 schema maps whole projects. Namespace/feature slices and runtime/reflection dependencies require manual review and cannot be encoded as project ownership here.");
        if (observations.Status != AnalysisStatus.Completed)
        {
            questions.Add("Complete the selected configuration/framework inventory before generating a policy proposal; restore prerequisites and resolve every coverage issue.");
            return new(null, ownership, inferences, questions);
        }
        if (observations.Inventory is null || ownership.Count == 0 || ownership.Count != observations.Inventory.Projects.Count)
        {
            return new(null, ownership, inferences, questions);
        }
        var modules = moduleIds.Select(id => new ModulePolicy(id, ownership.Where(item => item.Module == id).Select(item => item.ProjectPath),
            ownership.Where(item => item.Module == id && item.Contract is not null).GroupBy(item => item.Contract!)
                .Select(group => new ContractPolicy(group.Key, group.Select(item => item.ProjectPath)))));
        var layers = layerIds.Select(id => new LayerPolicy(id, ownership.Where(item => item.Layer == id).Select(item => item.ProjectPath),
            Directions(id).Where(layerIds.Contains),
            forbiddenNamespaces: id == "Domain" ? ["Microsoft.Build", "Microsoft.CodeAnalysis"] : [],
            domainInfrastructure: id == "Domain" ? new DomainInfrastructurePolicy(forbiddenNamespaces: ["System.Data", "Microsoft.EntityFrameworkCore"]) : null));
        var policy = new ArchitecturePolicy(modules, layers, new Dictionary<string, DiagnosticSeverity>(StringComparer.Ordinal)
        {
            ["AG001"] = DiagnosticSeverity.Error,
            ["AG002"] = DiagnosticSeverity.Error,
            ["AG003"] = DiagnosticSeverity.Error,
            ["AG004"] = DiagnosticSeverity.Error,
            ["AG005"] = DiagnosticSeverity.Error,
            ["AG006"] = DiagnosticSeverity.Error,
        });
        // Resolve every exact selector now, independently of whether any architecture rule passes.
        _ = new ResolvedPolicy(policy, observations.Inventory.Projects.Select(project => project.Id));
        return new(policy, ownership, inferences, questions);
    }

    private static string? ModulePrefix(string name)
    {
        var parts = name.Split('.', '-', '_');
        var index = Array.FindIndex(parts, Roles.ContainsKey);
        return index > 0 ? string.Join('.', parts.Take(index)) : null;
    }

    private static bool ValidId(string value) => value.Length > 0 && char.IsAsciiLetter(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static string[] Directions(string layer) => layer switch
    {
        "Application" => ["Domain", "Contracts"],
        "Infrastructure" => ["Domain", "Application", "Contracts"],
        "Host" => ["Domain", "Application", "Infrastructure", "Contracts"],
        "Tests" => ["Domain", "Application", "Infrastructure", "Contracts", "Host"],
        _ => [],
    };
}
