#if PORTABLE_POLICY
namespace ArchitectureGuardian.Policy.Documents;
#else
namespace ArchitectureGuardian.Core.Policies;
#endif

/// <summary>Matches an ordinal, repository-relative project path using the v1 glob grammar.</summary>
public sealed class ProjectPattern
{
    private readonly string[] _segments;

    /// <summary>Creates a selector; * and ? stay within a segment, and a whole ** segment spans directories.</summary>
    /// <param name="pattern">A slash-separated relative path or glob without empty, dot or parent segments.</param>
    /// <exception cref="ArgumentException">The selector is blank, nonrelative or uses unsupported syntax.</exception>
    public ProjectPattern(string pattern)
    {
        ValidatePath(pattern, allowWildcards: true);
        Value = pattern;
        _segments = pattern.Split('/');
    }

    /// <summary>Gets the original selector.</summary>
    public string Value { get; }

    /// <summary>Matches a normalized project identity without consulting the filesystem.</summary>
    /// <param name="projectPath">A repository-relative project path; incoming backslashes normalize to slashes.</param>
    /// <returns>Whether the complete, case-sensitive path matches.</returns>
    /// <exception cref="ArgumentException">The project identity is blank or noncanonical.</exception>
    public bool IsMatch(string projectPath) => IsMatchNormalized(NormalizeIdentity(projectPath));

    // Policy inventories are validated once before matching their paths against every selector.
    internal bool IsMatchNormalized(string projectPath)
    {
        // Dynamic programming bounds work by selector/path segments, including consecutive ** selectors.
        var parts = projectPath.Split('/');
        var previous = new bool[parts.Length + 1];
        previous[0] = true;
        for (var index = 0; index < _segments.Length; index++)
        {
            var segment = _segments[index];
            var current = new bool[parts.Length + 1];
            if (segment == "**" && index != _segments.Length - 1) { current[0] = previous[0]; }
            for (var i = 1; i <= parts.Length; i++)
            {
                current[i] = segment == "**" ? current[i - 1] || (index == _segments.Length - 1 ? previous[i - 1] : previous[i])
                    : previous[i - 1] && MatchSegment(segment, parts[i - 1]);
            }
            previous = current;
        }
        return previous[parts.Length];
    }

    private static bool MatchSegment(string pattern, string value)
    {
        var p = 0;
        var v = 0;
        var star = -1;
        var retry = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == value[v])) { p++; v++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = v; }
            else if (star >= 0) { p = star + 1; v = ++retry; }
            else { return false; }
        }
        while (p < pattern.Length && pattern[p] == '*') { p++; }
        return p == pattern.Length;
    }

    /// <summary>Normalizes an adapter-provided identity and rejects rooted or noncanonical paths.</summary>
    /// <param name="projectPath">A project path relative to the policy directory.</param>
    /// <returns>A slash-separated identity retaining its original case.</returns>
    /// <exception cref="ArgumentException">The identity is blank, rooted or contains dot segments or wildcard syntax.</exception>
    public static string NormalizeIdentity(string projectPath)
    {
        if (projectPath is null) { throw new ArgumentNullException(nameof(projectPath)); }
        var normalized = projectPath.Replace('\\', '/');
        ValidatePath(normalized, allowWildcards: false);
        return normalized;
    }

    private static void ValidatePath(string path, bool allowWildcards)
    {
        if (string.IsNullOrWhiteSpace(path)) { throw new ArgumentException("Project path cannot be blank."); }
        if (path.Any(character => char.IsWhiteSpace(character) && character != ' '
                || char.IsControl(character) || ":\\[]{}!".IndexOf(character) >= 0)
            || path.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."
                || segment.IndexOf("**", StringComparison.Ordinal) >= 0 && segment != "**")
            || !allowWildcards && path.IndexOfAny(['*', '?']) >= 0)
        {
            throw new ArgumentException($"Invalid relative project {(allowWildcards ? "selector" : "identity")} '{path}'. Use / separators, *, ?, or a whole ** segment; rooted, dot and parent paths are unsupported.");
        }
    }
}
