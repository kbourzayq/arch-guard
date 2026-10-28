using ArchitectureGuardian.Configuration.Validation;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Policy;

namespace ArchitectureGuardian.Configuration.Yaml;

/// <summary>Loads strict YAML through the portable reader and adapts it into Core policy contracts.</summary>
public sealed class YamlPolicyLoader : IPolicyLoader
{
    /// <summary>Reads and validates a UTF-8 policy file asynchronously.</summary>
    /// <param name="path">The explicit policy path.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A validated Core architecture policy.</returns>
    public async Task<ArchitecturePolicy> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), path, cancellationToken);
    }

    /// <summary>Parses YAML with the same schema, validation and selector semantics used by the compiler.</summary>
    /// <param name="yaml">The document content.</param>
    /// <param name="sourcePath">The path used in configuration errors.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A validated Core architecture policy.</returns>
    public ArchitecturePolicy Parse(string yaml, string sourcePath = "architecture.guardian.yaml",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = new PolicyDocumentReader().Parse(yaml, sourcePath, cancellationToken);
            var modules = document.Modules.Select(module => new ModulePolicy(module.Id, module.Projects.Select(pattern => pattern.Value),
                module.Contracts.Select(contract => new ContractPolicy(contract.Id, contract.Projects.Select(pattern => pattern.Value))),
                module.AllowedDependencies, module.ForbiddenAssemblies, module.ForbiddenNamespaces));
            var layers = document.Layers.Select(layer => new LayerPolicy(layer.Id, layer.Projects.Select(pattern => pattern.Value),
                layer.AllowedDependencies, layer.ForbiddenAssemblies, layer.ForbiddenNamespaces,
                layer.DomainInfrastructure is { } restrictions ? new DomainInfrastructurePolicy(restrictions.ForbiddenAssemblies,
                    restrictions.ForbiddenNamespaces, restrictions.AllowedNamespaces) : null));
            return new ArchitecturePolicy(modules, layers,
                document.Rules.ToDictionary(pair => pair.Key, pair => (DiagnosticSeverity)pair.Value, StringComparer.Ordinal),
                new GatePolicy((DiagnosticSeverity)document.FailOn));
        }
        catch (PolicyReadException exception)
        {
            throw new PolicyConfigurationException(exception.Problem, exception.PolicyPath,
                new SourceLocation(exception.SourcePath, exception.Position.Line, exception.Position.Column), exception);
        }
    }
}
