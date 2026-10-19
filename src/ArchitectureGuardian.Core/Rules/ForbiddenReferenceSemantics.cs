namespace ArchitectureGuardian.Core.Rules;

/// <summary>Portable matching and validation for explicitly configured semantic reference restrictions.</summary>
internal static class ForbiddenReferenceSemantics
{
    internal const string Id = "AG005";
    internal const string Title = "Forbidden assembly/namespace references";
    internal const string Anchor = "ag005-forbidden-references";

    internal static bool MatchesNamespace(string name, string restriction) =>
        name == restriction || name.StartsWith(restriction + ".", StringComparison.Ordinal);

    internal static bool IsAssemblyName(string name) => !string.IsNullOrWhiteSpace(name)
        && name.All(character => IsLetter(character) || character is >= '0' and <= '9' || character is '.' or '_' or '-');

    private static bool IsLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    internal static bool IsNamespaceName(string name) => !string.IsNullOrWhiteSpace(name)
        && name.Split('.').All(segment => segment.Length > 0 && (IsLetter(segment[0]) || segment[0] == '_')
            && segment.All(character => IsLetter(character) || character is >= '0' and <= '9' || character == '_'));
}
