using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ArchitectureGuardian.Core.Analysis;

namespace ArchitectureGuardian.Analysis.Discovery;

/// <summary>Finds input choices and literal project paths without evaluating MSBuild or loading assemblies.</summary>
public static class DiscoveryInputs
{
    /// <summary>Lists top-level solutions, or recursively lists C# projects when no solution is present.</summary>
    /// <param name="directory">Directory in which the developer is choosing an input.</param>
    /// <param name="cancellationToken">Cooperative directory traversal cancellation.</param>
    /// <returns>Ordinal absolute paths; multiple choices require explicit developer selection.</returns>
    public static IReadOnlyList<string> Find(string directory, CancellationToken cancellationToken = default)
    {
        var solutions = Directory.EnumerateFiles(directory).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx")
            .Order(StringComparer.Ordinal).ToArray();
        if (solutions.Length > 0) { return Array.AsReadOnly(solutions); }
        var projects = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            projects.AddRange(Directory.EnumerateFiles(current).Where(path => Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)));
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                if (Path.GetFileName(child) is not ("bin" or "obj" or ".git" or ".codegraph" or "node_modules")
                    && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
        return Array.AsReadOnly(projects.Order(StringComparer.Ordinal).ToArray());
    }

    internal static IReadOnlyList<string> LiteralProjects(string input, string root, List<AnalysisIssue> issues, CancellationToken cancellationToken)
    {
        var pending = new Queue<string>();
        var extension = Path.GetExtension(input).ToLowerInvariant();
        if (extension == ".csproj") { pending.Enqueue(input); }
        else if (extension == ".slnx")
        {
            foreach (var project in ReadXml(input).Descendants("Project"))
            {
                if (project.Attribute("Path") is { } path) { Add(input, path.Value); }
            }
        }
        else if (extension == ".sln")
        {
            foreach (var line in File.ReadLines(input))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = Regex.Match(line, "^Project\\(.*\\)\\s*=\\s*\"[^\"]*\",\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (match.Success) { Add(input, match.Groups[1].Value); }
            }
        }
        else { throw new ArgumentException("Choose a .sln, .slnx or .csproj input."); }
        var paths = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var project))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, project).Replace('\\', '/');
            if (!paths.Add(relative)) { continue; }
            // A literal reference can be inactive in the selected build context. Static hints must
            // neither enforce policy ownership nor prevent trusted evaluation from resolving conditions.
            if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                issues.Add(new("static-project-outside-root", $"{relative}: unevaluated literal reference is outside the discovery root and was not traversed. Its participation depends on the selected build context."));
                continue;
            }
            if (!File.Exists(project))
            {
                issues.Add(new("static-project-unavailable", $"{relative}: literal project path is unavailable; its participation is unevaluated."));
                continue;
            }
            XDocument document;
            try { document = ReadXml(project); }
            catch (Exception exception) when (project != input && (exception is IOException or UnauthorizedAccessException or XmlException))
            {
                issues.Add(new("static-project-unreadable", $"{relative}: literal project XML could not be read ({exception.GetType().Name}); evaluate the selected context to determine participation."));
                continue;
            }
            foreach (var reference in document.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
            {
                if (reference.Attribute("Include") is { } include) { Add(project, include.Value); }
            }
        }
        return Array.AsReadOnly(paths.Order(StringComparer.Ordinal).ToArray());

        void Add(string source, string value)
        {
            // These are unevaluated hints only. Never expand expressions, globs, imports or conditions.
            if (value.IndexOfAny(['$', '@', '*', '?', ';']) >= 0 || !value.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) { return; }
            try { pending.Enqueue(Path.GetFullPath(value.Replace('\\', Path.DirectorySeparatorChar), Path.GetDirectoryName(source)!)); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                issues.Add(new("static-reference-invalid", $"Literal project reference '{value}' could not be resolved as a path; verify its participation in the selected build context."));
            }
        }
    }

    private static XDocument ReadXml(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
}
