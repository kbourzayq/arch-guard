namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Identifies generated source by its generator and hint rather than a physical output path.</summary>
public sealed class RepositoryGeneratedFile
{
    /// <summary>Creates a logical generated-file identity independent of assembly versions and output directories.</summary>
    /// <param name="generatorAssemblyName">The generator's assembly simple name.</param>
    /// <param name="generatorTypeName">The generator's full type name, unwrapping incremental-generator adapters.</param>
    /// <param name="hintName">The source hint supplied by the generator, including any logical folders.</param>
    /// <exception cref="ArgumentException">A required identity component is blank.</exception>
    public RepositoryGeneratedFile(string generatorAssemblyName, string generatorTypeName, string hintName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorAssemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatorTypeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(hintName);
        GeneratorAssemblyName = generatorAssemblyName;
        GeneratorTypeName = generatorTypeName;
        HintName = hintName.Replace('\\', '/');
        Id = RepositoryData.Identity("generated-file", GeneratorAssemblyName, GeneratorTypeName, HintName);
    }

    /// <summary>Gets the stable logical generated-file identity.</summary>
    public string Id { get; }
    /// <summary>Gets the generator's assembly simple name, excluding its version and physical path.</summary>
    public string GeneratorAssemblyName { get; }
    /// <summary>Gets the underlying generator's full type name.</summary>
    public string GeneratorTypeName { get; }
    /// <summary>Gets the generator-supplied hint with slash separators.</summary>
    public string HintName { get; }
}
