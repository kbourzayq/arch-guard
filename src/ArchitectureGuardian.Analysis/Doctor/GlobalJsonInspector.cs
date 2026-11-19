using System.Text.Json;

namespace ArchitectureGuardian.Analysis.Doctor;

/// <summary>Reads an input's SDK selection as data so untrusted diagnosis never asks the host to run repository-selected SDK code.</summary>
internal static class GlobalJsonInspector
{
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Finds the global.json the .NET host would use for <paramref name="directory"/> and extracts its SDK request.</summary>
    /// <exception cref="JsonException">The nearest global.json is not valid JSON or has an invalid sdk section.</exception>
    internal static GlobalJsonSelection? Inspect(string directory)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(directory)); current is not null; current = current.Parent)
        {
            var path = Path.Combine(current.FullName, "global.json");
            if (!File.Exists(path))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path), Options);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sdk", out var sdk))
            {
                return new(path, null, null, []);
            }

            if (sdk.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException($"{path}: the sdk property must be an object.");
            }

            var paths = new List<string>();
            if (sdk.TryGetProperty("paths", out var entries))
            {
                if (entries.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException($"{path}: sdk.paths must be an array.");
                }

                paths.AddRange(entries.EnumerateArray().Select(entry => entry.ValueKind == JsonValueKind.String
                    ? entry.GetString()!
                    : throw new JsonException($"{path}: sdk.paths entries must be strings.")));
            }

            return new(path, Text(sdk, "version", path), Text(sdk, "rollForward", path), paths);
        }

        return null;
    }

    private static string? Text(JsonElement sdk, string name, string path) =>
        !sdk.TryGetProperty(name, out var value) ? null
        : value.ValueKind == JsonValueKind.String ? value.GetString()
        : throw new JsonException($"{path}: sdk.{name} must be a string.");
}

/// <summary>An unevaluated global.json SDK request.</summary>
/// <param name="Path">The global.json file the host would select.</param>
/// <param name="Version">Requested SDK version, if any.</param>
/// <param name="RollForward">Requested roll-forward policy, if any.</param>
/// <param name="Paths">SDK search locations; anything other than <c>$host$</c> may select repository-local SDK code.</param>
internal sealed record GlobalJsonSelection(string Path, string? Version, string? RollForward, IReadOnlyList<string> Paths)
{
    /// <summary>Whether the request adds SDK search locations beyond the host's own installation.</summary>
    internal bool HasCustomPaths => Paths.Any(path => path != "$host$");
}
