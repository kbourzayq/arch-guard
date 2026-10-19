namespace ArchitectureGuardian.Core.Rules;

/// <summary>Portable AG006 metadata and matching shared by solution and compiler hosts.</summary>
internal static class DomainInfrastructureSemantics
{
    internal const string Id = "AG006";
    internal const string Title = "Domain infrastructure references";
    internal const string Anchor = "ag006-domain-infrastructure";

    internal static IEnumerable<(string Kind, string Name)> Restrictions(string assembly, string ns,
        IEnumerable<string> assemblies, IEnumerable<string> namespaces, IEnumerable<string> allowedNamespaces)
    {
        if (allowedNamespaces.Any(name => ForbiddenReferenceSemantics.MatchesNamespace(ns, name)))
        {
            yield break;
        }

        foreach (var name in assemblies)
        {
            if (assembly == name) { yield return ("forbiddenAssemblies", name); }
        }

        foreach (var name in namespaces)
        {
            if (ForbiddenReferenceSemantics.MatchesNamespace(ns, name)) { yield return ("forbiddenNamespaces", name); }
        }
    }
}
