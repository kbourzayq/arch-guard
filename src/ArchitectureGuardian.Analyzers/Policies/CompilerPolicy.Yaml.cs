using ArchitectureGuardian.Policy;
using ArchitectureGuardian.Policy.Documents;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ArchitectureGuardian.Analyzers.Policies;

/// <summary>Adapts the shared policy document and host-supplied project identities to compiler ownership.</summary>
internal sealed partial class CompilerPolicy
{
    internal const string YamlFileName = "architecture.guardian.yaml";
    private IReadOnlyDictionary<string, PolicySeverity>? _rules;

    internal bool IsSelected(string ruleId) => _rules is null || _rules.ContainsKey(ruleId);

    internal DiagnosticSeverity Severity(string ruleId) => _rules is null ? DiagnosticSeverity.Warning
        : _rules[ruleId] switch
        {
            PolicySeverity.Info => DiagnosticSeverity.Info,
            PolicySeverity.Warning => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Error,
        };

    internal static CompilerPolicy LoadYaml(AdditionalText file, AnalyzerOptions options, Compilation compilation,
        CancellationToken cancellationToken)
    {
        var text = file.GetText(cancellationToken) ?? throw new ArgumentException("Cannot read YAML policy AdditionalFile.");
        var document = new PolicyDocumentReader().Parse(text.ToString(), file.Path, cancellationToken);
        var policy = new CompilerPolicy { _rules = document.Rules };
        foreach (var module in document.Modules)
        {
            policy._modules.Add(module.Id, module.AllowedDependencies.ToArray());
            policy._restrictions.Add("modules." + module.Id, (module.ForbiddenAssemblies.ToArray(), module.ForbiddenNamespaces.ToArray()));
        }
        foreach (var layer in document.Layers)
        {
            policy._layers.Add(layer.Id, layer.AllowedDependencies.ToArray());
            policy._restrictions.Add("layers." + layer.Id, (layer.ForbiddenAssemblies.ToArray(), layer.ForbiddenNamespaces.ToArray()));
            policy._infrastructure.Add(layer.Id, (layer.DomainInfrastructure?.ForbiddenAssemblies.ToArray() ?? [],
                layer.DomainInfrastructure?.ForbiddenNamespaces.ToArray() ?? [], layer.DomainInfrastructure?.AllowedNamespaces.ToArray() ?? []));
        }

        if (!options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.MSBuildProjectFullPath", out var sourcePath)
            || string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("YAML policy requires project metadata from the ArchitectureGuardian.Analyzers build targets (MSBuildProjectFullPath).");
        }
        Add(compilation.AssemblyName ?? "", sourcePath);
        foreach (var project in options.AdditionalFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = options.AnalyzerConfigOptionsProvider.GetOptions(project);
            if (!metadata.TryGetValue("build_metadata.AdditionalFiles.ArchitectureGuardianReferencePath", out var referencePath)
                || string.IsNullOrWhiteSpace(referencePath)) { continue; }
            metadata.TryGetValue("build_metadata.AdditionalFiles.ArchitectureGuardianAssemblyName", out var name);
            metadata.TryGetValue("build_metadata.AdditionalFiles.ArchitectureGuardianAssemblyIdentity", out var identity);
            var reference = compilation.References.OfType<PortableExecutableReference>().FirstOrDefault(candidate =>
                string.Equals(candidate.FilePath, referencePath, PathComparison));
            if (reference is not null && compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
            {
                name = assembly.Name;
            }
            else if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(identity)
                && AssemblyIdentity.TryParseDisplayName(identity!, out var parsed))
            {
                name = parsed!.Name;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Project reference '" + project.Path + "' has no evaluated assembly identity. Restore and reload the project with the analyzer build targets enabled.");
            }
            Add(name!, project.Path);
        }
        // Unlike an external DLL, a CompilationReference establishes that the target is a source project.
        foreach (var reference in compilation.References.OfType<CompilationReference>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = reference.Compilation.AssemblyName;
            if (name is null || !policy.Contains(name))
            {
                throw new ArgumentException("Project reference '" + name + "' has no YAML ownership metadata. Reload the project with the analyzer build targets enabled.");
            }
        }
        return policy;

        void Add(string name, string projectPath)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = new Uri(Path.GetDirectoryName(Path.GetFullPath(file.Path))! + Path.DirectorySeparatorChar);
            var relative = ProjectPattern.NormalizeIdentity(Uri.UnescapeDataString(root.MakeRelativeUri(new Uri(Path.GetFullPath(projectPath))).ToString()));
            var modules = document.Modules.Where(module => module.Projects.Any(pattern => pattern.IsMatch(relative))).ToArray();
            var layers = document.Layers.Where(layer => layer.Projects.Any(pattern => pattern.IsMatch(relative))).ToArray();
            if (modules.Length != 1 || layers.Length != 1)
            {
                throw new ArgumentException("Project '" + relative + "' must match exactly one module and one layer in YAML.");
            }
            var contracts = document.Modules.SelectMany(module => module.Contracts.Select(contract => (Module: module.Id, Contract: contract)))
                .Where(item => item.Contract.Projects.Any(pattern => pattern.IsMatch(relative))).ToArray();
            if (contracts.Length > 1 || contracts.Any(item => item.Module != modules[0].Id))
            {
                throw new ArgumentException("Project '" + relative + "' has ambiguous or out-of-module public contracts.");
            }
            var ownership = new Ownership(modules[0].Id, layers[0].Id, contracts.SingleOrDefault().Contract?.Id);
            if (policy._assemblies.TryGetValue(name, out var existing))
            {
                if (existing.Module != ownership.Module || existing.Layer != ownership.Layer || existing.Contract != ownership.Contract)
                {
                    throw new ArgumentException("Assembly '" + name + "' has conflicting project ownership in this build context.");
                }
            }
            else { policy._assemblies.Add(name, ownership); }
        }
    }

    private static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
