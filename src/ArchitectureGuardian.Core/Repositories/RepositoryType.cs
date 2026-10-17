using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Describes one explicit source type, including nested, partial and generated declarations.</summary>
public sealed class RepositoryType
{
    /// <summary>Creates a type with a project-scoped identity independent of assembly versions and checkout paths.</summary>
    /// <param name="projectPath">The project identity relative to the policy directory.</param>
    /// <param name="namespaceName">The full namespace name; empty denotes the global namespace.</param>
    /// <param name="metadataName">The namespace-qualified name, using + for nesting and backtick arity for generics.</param>
    /// <param name="kind">The neutral declaration kind.</param>
    /// <param name="isPublic">Whether this type and all its containing types are public.</param>
    /// <param name="declaringFile">The policy-relative authored source path for a file-local type or its nested types; otherwise null.</param>
    /// <param name="generatedFile">The logical generator/hint identity for a generated file-local type or its nested types; otherwise null.</param>
    /// <exception cref="ArgumentException">A required identity is invalid or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The type kind is undefined.</exception>
    public RepositoryType(string projectPath, string namespaceName, string metadataName, RepositoryTypeKind kind,
        bool isPublic, string? declaringFile = null, RepositoryGeneratedFile? generatedFile = null)
    {
        ProjectPath = ProjectPattern.NormalizeIdentity(projectPath);
        ArgumentNullException.ThrowIfNull(namespaceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadataName);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (declaringFile is not null && generatedFile is not null)
        {
            throw new ArgumentException("A file-local type must use either an authored source path or a logical generated file.");
        }

        DeclaringFile = declaringFile is null ? null : ProjectPattern.NormalizeIdentity(declaringFile);
        GeneratedFile = generatedFile;
        NamespaceName = namespaceName;
        MetadataName = metadataName;
        Kind = kind;
        IsPublic = isPublic;
        Id = RepositoryData.Identity("type", ProjectPath, MetadataName,
            generatedFile is null ? DeclaringFile : "generated:" + generatedFile.Id);
    }

    /// <summary>Gets the stable project-scoped type identity.</summary>
    public string Id { get; }
    /// <summary>Gets the owning project identity.</summary>
    public string ProjectPath { get; }
    /// <summary>Gets the full namespace name, or empty for the global namespace.</summary>
    public string NamespaceName { get; }
    /// <summary>Gets the qualified name with generic arity and nested-type separators; file-local compiler prefixes are excluded.</summary>
    public string MetadataName { get; }
    /// <summary>Gets the declaration kind.</summary>
    public RepositoryTypeKind Kind { get; }
    /// <summary>Gets whether the type and all containing types are public; this does not grant policy access.</summary>
    public bool IsPublic { get; }
    /// <summary>Gets the policy-relative authored file-local discriminator, or null for ordinary or generated types.</summary>
    public string? DeclaringFile { get; }
    /// <summary>Gets the logical file-local generator/hint discriminator, or null for ordinary or authored types.</summary>
    public RepositoryGeneratedFile? GeneratedFile { get; }
}
