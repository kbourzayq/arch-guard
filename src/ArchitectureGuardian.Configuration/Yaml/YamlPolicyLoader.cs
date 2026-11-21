using ArchitectureGuardian.Configuration.Validation;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Policy;
using ArchitectureGuardian.Policy.Packs;

namespace ArchitectureGuardian.Configuration.Yaml;

/// <summary>Loads strict YAML through the portable reader and adapts it into Core policy contracts.</summary>
public sealed class YamlPolicyLoader : IPolicyLoader
{
    /// <summary>Reads and validates a UTF-8 policy file asynchronously, composing the rule packs it extends.</summary>
    /// <param name="path">The explicit policy path. Local packs resolve below its directory.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A validated Core architecture policy.</returns>
    /// <exception cref="PolicyConfigurationException">The policy or a pack is invalid, unavailable, incompatible or conflicting.</exception>
    /// <exception cref="IOException">The policy or a local pack manifest cannot be read.</exception>
    public async Task<ArchitecturePolicy> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var yaml = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(yaml, path, new FilePolicyPackResolver(Path.GetDirectoryName(Path.GetFullPath(path))!), cancellationToken);
    }

    /// <summary>Parses YAML with the same schema, validation and selector semantics used by the compiler.</summary>
    /// <param name="yaml">The document content.</param>
    /// <param name="sourcePath">The path used in configuration errors.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A validated Core architecture policy.</returns>
    /// <exception cref="PolicyConfigurationException">
    /// The policy is invalid, or extends a local pack, which requires <see cref="LoadAsync"/> and a policy file location.
    /// </exception>
    public ArchitecturePolicy Parse(string yaml, string sourcePath = "architecture.guardian.yaml",
        CancellationToken cancellationToken = default) => Parse(yaml, sourcePath, null, cancellationToken);

    private static ArchitecturePolicy Parse(string yaml, string sourcePath, IPolicyPackResolver? packResolver,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = new PolicyDocumentReader().Parse(yaml, sourcePath, packResolver, cancellationToken);
            var modules = document.Modules.Select(module => new ModulePolicy(module.Id, module.Projects.Select(pattern => pattern.Value),
                module.Contracts.Select(contract => new ContractPolicy(contract.Id, contract.Projects.Select(pattern => pattern.Value))),
                module.AllowedDependencies, module.ForbiddenAssemblies, module.ForbiddenNamespaces));
            var layers = document.Layers.Select(layer => new LayerPolicy(layer.Id, layer.Projects.Select(pattern => pattern.Value),
                layer.AllowedDependencies, layer.ForbiddenAssemblies, layer.ForbiddenNamespaces,
                layer.DomainInfrastructure is { } restrictions ? new DomainInfrastructurePolicy(restrictions.ForbiddenAssemblies,
                    restrictions.ForbiddenNamespaces, restrictions.AllowedNamespaces) : null));
            return new ArchitecturePolicy(modules, layers,
                document.Rules.ToDictionary(pair => pair.Key, pair => (DiagnosticSeverity)pair.Value, StringComparer.Ordinal),
                new GatePolicy((DiagnosticSeverity)document.FailOn),
                document.EntityFrameworkCore is { } entityFrameworkCore
                    ? new EntityFrameworkCorePolicy(entityFrameworkCore.PersistenceLayers, entityFrameworkCore.ForbiddenApis) : null,
                document.Abp is { } abp ? new AbpPolicy(abp.RepositoryLayers, abp.TenantFilterBypassLayers) : null);
        }
        catch (PolicyReadException exception)
        {
            throw new PolicyConfigurationException(exception.Problem, exception.PolicyPath,
                new SourceLocation(exception.SourcePath, exception.Position.Line, exception.Position.Column), exception);
        }
    }
}
