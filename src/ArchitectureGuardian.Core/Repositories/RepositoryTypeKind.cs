namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Identifies a declared type without exposing compiler-specific values.</summary>
public enum RepositoryTypeKind
{
    /// <summary>A class, including a record class.</summary>
    Class,
    /// <summary>A structure, including a record structure.</summary>
    Struct,
    /// <summary>An interface.</summary>
    Interface,
    /// <summary>An enumeration.</summary>
    Enum,
    /// <summary>A delegate.</summary>
    Delegate,
}
