namespace ArchitectureGuardian.Core.Policies;

/// <summary>Validates and snapshots the small immutable policy collections.</summary>
internal static class PolicyData
{
    internal static string Id(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "\\A[A-Za-z][A-Za-z0-9_.-]*\\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new ArgumentException($"Invalid identity '{id}'; use a letter followed by letters, digits, _, . or -.");
        }

        return id;
    }

    internal static IReadOnlyList<ProjectPattern> Patterns(IEnumerable<string> patterns)
    {
        var values = Strings(patterns);
        if (values.Count == 0)
        {
            throw new ArgumentException("Declare at least one project selector.");
        }

        return Array.AsReadOnly(values.Select(pattern => new ProjectPattern(pattern)).ToArray());
    }

    internal static IReadOnlyList<string> Strings(IEnumerable<string> values) => Unique(values, value =>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }, "value");

    internal static IReadOnlyList<T> Unique<T>(IEnumerable<T> values, Func<T, string> key, string kind)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException($"A {kind} cannot be null.");
        }

        var duplicate = copy.GroupBy(key, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate {kind} '{duplicate.Key}'.");
        }

        return Array.AsReadOnly(copy.OrderBy(key, StringComparer.Ordinal).ToArray());
    }
}
