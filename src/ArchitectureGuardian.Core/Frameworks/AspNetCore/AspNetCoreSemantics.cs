using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Shares the persistence vocabulary used by ASP001, ASP004 and ASP005.</summary>
internal static class AspNetCoreSemantics
{
    /// <summary>Namespaces, including descendants, whose types represent data access technology.</summary>
    private static readonly string[] PersistenceNamespaces =
    [
        "Dapper", "Microsoft.Azure.Cosmos", "Microsoft.Data", "Microsoft.EntityFrameworkCore", "MongoDB", "MySqlConnector",
        "Npgsql", "Oracle.ManagedDataAccess", "System.Data",
    ];

    /// <summary>Determines whether a dependency, or any type it derives from, belongs to persistence technology.</summary>
    internal static bool IsPersistence(AspNetCoreDependency dependency)
        => dependency.Lineage.Any(origin => PersistenceNamespaces.Any(name => ForbiddenReferenceSemantics.MatchesNamespace(origin.Namespace, name)));

    /// <summary>Gets the matching persistence namespace for explanation, or null when the dependency is not persistence.</summary>
    internal static string? PersistenceNamespace(AspNetCoreDependency dependency)
        => PersistenceNamespaces.FirstOrDefault(name => dependency.Lineage.Any(origin => ForbiddenReferenceSemantics.MatchesNamespace(origin.Namespace, name)));

    /// <summary>Gets the configured persistence vocabulary in ordinal order for constraints.</summary>
    internal static IReadOnlyList<string> Namespaces => PersistenceNamespaces;
}
