using System.Xml;
using System.Xml.Linq;
using ArchitectureGuardian.Analysis.Workspaces;

namespace ArchitectureGuardian.Analysis.Doctor;

/// <summary>Reads literal installation hints without expanding imports, properties, conditions or tool manifests.</summary>
internal static class DoctorStaticChecks
{
    internal static void Inspect(string root, WorkspaceLoadRequest request, string workflow, List<DoctorCheck> checks, CancellationToken cancellationToken)
    {
        foreach (var (file, id) in new[] { ("Directory.Build.props", "build-props"), ("Directory.Packages.props", "central-packages") })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(root, file);
            if (!File.Exists(path))
            {
                checks.Add(new(id, path, "static", "informational", false, "No root file at the policy directory; per-project or imported installation may still be valid.",
                    "For shared analyzer installation, put PackageReference and AdditionalFiles in root Directory.Build.props; CPM versions belong in Directory.Packages.props."));
                continue;
            }
            try
            {
                using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                var document = XDocument.Load(reader);
                var references = document.Descendants().Count(element => element.Name.LocalName is "PackageReference" or "PackageVersion"
                    && ((string?)element.Attribute("Include") ?? (string?)element.Attribute("Update"))?.Equals("ArchitectureGuardian.Analyzers", StringComparison.OrdinalIgnoreCase) == true);
                checks.Add(new(id, path, "static", "informational", false,
                    $"Readable XML; {references} literal analyzer package declaration(s). Conditions, imports and version expressions are unevaluated.",
                    "With CPM, omit Version on PackageReference and declare PackageVersion centrally. Use --trusted to validate the selected context."));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
            {
                checks.Add(new(id, path, "static", "warning", false, exception.Message, "Fix the reported XML/access problem if this file participates in the selected context; evaluated loading determines coverage."));
            }
        }
        var executable = FindExecutable("archguard-mcp");
        checks.Add(new("mcp-integration", "PATH", "static", executable is not null ? "informational" : workflow == "mcp" ? "blocked" : "informational", workflow == "mcp",
            executable is null ? "archguard-mcp was not found on this process's PATH; clients may use an absolute path or local tool manifest." : $"MCP executable exists on PATH: {executable}. Client connectivity was not tested.",
            "For the mcp workflow, expose the installed archguard-mcp on PATH, then configure the client's absolute executable, Workspace:Root and operator trust separately. No tool was launched or installed."));
        checks.Add(new("compiler-host", request.InputPath, "static", "informational", false,
            "Analyzer host floor: Roslyn 4.8+ (.NET Standard 2.0); CLI/MCP runtime requirement: .NET 10. Interactive IDE compatibility is not probed.",
            "Check the actual compiler/IDE API version and reload AdditionalFiles. Keep a whole-solution scan for AG003 and full coverage."));
        checks.Add(new("ci-prerequisites", request.InputPath, "static", "informational", false,
            $"Requested context: configuration={request.Configuration}, framework={request.TargetFramework ?? "single declared framework"}. CI checkout, feed credentials and restore freshness are not certified.",
            "In CI, check out the complete input/policy, install its SDK/packs, restore explicitly in this context, and retain scan JSON/SARIF plus normal build/tests. Fetch an explicit base/history for change scans."));
        var cli = FindExecutable("archguard");
        checks.Add(new("cli-integration", "PATH", "static", "informational", false,
            cli is null ? "archguard was not found on this process's PATH; an absolute tool path or source DLL may still be in use." : $"CLI executable exists on PATH: {cli}.",
            "Other shells/agents may have different PATH values; configure absolute tool paths or an explicit local tool manifest when required."));
    }

    private static string? FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), name + (OperatingSystem.IsWindows() ? ".exe" : ""));
            if (File.Exists(candidate)) { return Path.GetFullPath(candidate); }
        }
        return null;
    }
}
