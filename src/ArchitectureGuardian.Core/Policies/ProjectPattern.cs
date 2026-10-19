using System.Text.RegularExpressions;

namespace ArchitectureGuardian.Core.Policies;

/// <summary>Matches an ordinal, repository-relative project path using the v1 glob grammar.</summary>
public sealed class ProjectPattern
{
    private readonly Regex? _matcher;
    private readonly string _literalPrefix;

    /// <summary>Creates a selector; * and ? stay within a segment, and a whole ** segment spans directories.</summary>
    /// <param name="pattern">A slash-separated relative path or glob without empty, dot or parent segments.</param>
    /// <exception cref="ArgumentException">The selector is blank, nonrelative or uses unsupported syntax.</exception>
    public ProjectPattern(string pattern)
    {
        ValidatePath(pattern, allowWildcards: true);
        Value = pattern;
        var wildcard = pattern.IndexOfAny(['*', '?']);
        _literalPrefix = wildcard < 0 ? pattern : pattern[..wildcard];
        if (wildcard < 0)
        {
            return;
        }
        var segments = pattern.Split('/');
        var expressions = segments.Select((segment, index) => segment == "**"
            ? (index == segments.Length - 1 ? ".*" : "(?:[^/]+/)*")
            : Regex.Escape(segment).Replace("\\*", "[^/]*", StringComparison.Ordinal)
                .Replace("\\?", "[^/]", StringComparison.Ordinal) + (index == segments.Length - 1 ? "" : "/"));
        _matcher = new Regex("\\A" + string.Concat(expressions) + "\\z",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    }

    /// <summary>Gets the original selector.</summary>
    public string Value { get; }

    /// <summary>Matches a normalized project identity without consulting the filesystem.</summary>
    /// <param name="projectPath">A repository-relative project path; incoming backslashes normalize to slashes.</param>
    /// <returns>Whether the complete, case-sensitive path matches.</returns>
    /// <exception cref="ArgumentException">The project identity is blank or noncanonical.</exception>
    public bool IsMatch(string projectPath) => IsMatchNormalized(NormalizeIdentity(projectPath));

    // Policy inventories are validated once before matching their paths against every selector.
    internal bool IsMatchNormalized(string projectPath) => _matcher is null
        ? projectPath == Value
        : projectPath.StartsWith(_literalPrefix, StringComparison.Ordinal) && _matcher.IsMatch(projectPath);

    /// <summary>Normalizes an adapter-provided identity and rejects rooted or noncanonical paths.</summary>
    /// <param name="projectPath">A project path relative to the policy directory.</param>
    /// <returns>A slash-separated identity retaining its original case.</returns>
    /// <exception cref="ArgumentException">The identity is blank, rooted or contains dot segments or wildcard syntax.</exception>
    public static string NormalizeIdentity(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        var normalized = projectPath.Replace('\\', '/');
        ValidatePath(normalized, allowWildcards: false);
        return normalized;
    }

    private static void ValidatePath(string path, bool allowWildcards)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Any(character => char.IsWhiteSpace(character) && character != ' '
                || char.IsControl(character) || ":\\[]{}!".Contains(character))
            || path.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."
                || segment.Contains("**", StringComparison.Ordinal) && segment != "**")
            || !allowWildcards && path.IndexOfAny(['*', '?']) >= 0)
        {
            throw new ArgumentException($"Invalid relative project {(allowWildcards ? "selector" : "identity")} '{path}'. Use / separators, *, ?, or a whole ** segment; rooted, dot and parent paths are unsupported.");
        }
    }
}
