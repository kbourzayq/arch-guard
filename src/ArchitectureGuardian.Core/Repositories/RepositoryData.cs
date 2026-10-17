using ArchitectureGuardian.Core.Canonicalization;

namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Validates immutable model collections and computes portable, scoped identities.</summary>
internal static class RepositoryData
{
    internal static string Identity(params string?[] parts) => CanonicalData.Hash(CanonicalData.Encode(parts));

    internal static IReadOnlyList<T> Unique<T>(IEnumerable<T> values, Func<T, string> key) where T : class
    {
        var snapshot = CanonicalData.Snapshot(values, key);
        var duplicate = snapshot.GroupBy(key, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        return duplicate is not null ? throw new ArgumentException($"Duplicate repository identity '{duplicate.Key}'.") : snapshot;
    }
}
