namespace ArchitectureGuardian.Core.Dependencies;

/// <summary>Identifies the architectural level of a dependency's endpoints.</summary>
public enum DependencyKind
{
    /// <summary>An evaluated source-project reference.</summary>
    Project,
    /// <summary>An assembly reference or semantic use.</summary>
    Assembly,
    /// <summary>A semantic dependency between assembly-scoped namespaces.</summary>
    Namespace,
    /// <summary>A semantic dependency between types.</summary>
    Type,
    /// <summary>An aggregation of dependencies across logical modules.</summary>
    Module,
    /// <summary>An aggregation of dependencies across architectural layers.</summary>
    Layer,
}
