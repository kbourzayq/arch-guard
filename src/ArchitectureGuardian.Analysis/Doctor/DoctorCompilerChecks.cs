using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using Microsoft.CodeAnalysis;

namespace ArchitectureGuardian.Analysis.Doctor;

/// <summary>Diagnoses effective installation and per-source compiler overrides from an already loaded workspace.</summary>
internal static class DoctorCompilerChecks
{
    internal static async Task InspectAsync(WorkspaceLoadResult workspace, WorkspaceProjectContext context, string policyPath,
        ArchitecturePolicy? policy, bool required, List<DoctorCheck> checks, CancellationToken cancellationToken)
    {
        var scope = context.ProjectPath;
        var installation = context.Installation;
        if (installation is null)
        {
            checks.Add(new("analyzer-installation", scope, "evaluated", "notRun", required, "Evaluated package metadata is unavailable.", "Resolve workspace loading and retry."));
            return;
        }
        var packageValid = installation.AnalyzerReferences == 1 && (!installation.CentralPackages || installation.CentralAnalyzerVersions == 1 && installation.AnalyzerVersion.Length == 0);
        var project = workspace.Solution?.Projects.SingleOrDefault(project => project.FilePath == scope);
        var loaded = project?.AnalyzerReferences.Any(reference => Path.GetFileName(reference.FullPath) == "ArchitectureGuardian.Analyzers.dll") == true;
        checks.Add(new("analyzer-installation", scope, "evaluated", packageValid && loaded ? "passed" : required ? "blocked" : "informational", required,
            $"Analyzer PackageReference count={installation.AnalyzerReferences}; loaded analyzer={loaded}; CPM={installation.CentralPackages}; central PackageVersion count={installation.CentralAnalyzerVersions}; PackageReference Version='{installation.AnalyzerVersion}'; PrivateAssets='{installation.PrivateAssets}'.",
            "Add one ArchitectureGuardian.Analyzers PackageReference with PrivateAssets=all. With CPM, omit its Version and declare one PackageVersion centrally; otherwise set Version on PackageReference. Restore explicitly and reload."));
        var enabled = !installation.RunAnalyzers.Equals("false", StringComparison.OrdinalIgnoreCase)
            && !installation.RunAnalyzersDuringBuild.Equals("false", StringComparison.OrdinalIgnoreCase);
        checks.Add(new("analyzer-execution", scope, "evaluated", enabled ? "passed" : required ? "blocked" : "informational", required,
            $"RunAnalyzers='{installation.RunAnalyzers}'; RunAnalyzersDuringBuild='{installation.RunAnalyzersDuringBuild}'; compiler build feedback {(enabled ? "is enabled by these settings" : "is disabled")}.",
            "Enable RunAnalyzers and RunAnalyzersDuringBuild when compiler feedback is required; these build settings do not disable separate solution scans."));
        checks.Add(new("build-props", scope, "evaluated", "informational", false,
            $"Imported Directory.Build.props: {installation.BuildPropsPath}; Directory.Packages.props: {installation.PackagesPropsPath}; evaluated assets: {installation.AssetsPath}.",
            "Nested Directory.Build.props files can hide root installation. Import the root deliberately or install the analyzer/policy in participating projects."));
        // The compiler consumes the post-target inputs, not the pure evaluation's AdditionalFiles.
        var inputs = project?.AnalyzerOptions.AdditionalFiles.Where(file => Path.GetFileName(file.Path) is "architecture.guardian.yaml" or "architecture.guardian.compiler.xml")
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray() ?? [];
        var files = inputs.Select(file => file.Path).ToArray();
        var legacy = files.Length == 1 && Path.GetFileName(files[0]) == "architecture.guardian.compiler.xml";
        var matching = files.Length == 1 && (legacy || SamePath(files[0], policyPath)) && inputs[0].GetText(cancellationToken) is not null;
        var metadata = project is not null && project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.MSBuildProjectFullPath", out var currentPath)
            && SamePath(currentPath, scope);
        var wiringRequired = required || loaded;
        checks.Add(new("analyzer-policy", scope, "evaluated", matching && (legacy || metadata) ? "passed" : wiringRequired ? "blocked" : "informational", wiringRequired,
            $"Compiler policy AdditionalFiles count={files.Length}; readable matching input={matching}; legacy XML={legacy}; build-target project metadata={metadata}. Files: {string.Join(", ", files)}.",
            "Supply exactly one readable architecture.guardian.yaml pointing to --policy, or one supported legacy architecture.guardian.compiler.xml. Do not combine formats. YAML requires package buildTransitive project metadata; restore and reload after correcting installation."));
        if (legacy)
        {
            checks.Add(new("legacy-policy-validation", scope, "evaluated", "notRun", false,
                "Supported legacy XML wiring was inspected. XML schema, assembly mappings and parity with the separate YAML policy were not validated; XML mode selects all compiler rules with warning defaults.",
                "Run an explicit compiler build and check AG000/host diagnostics; maintain XML/YAML parity manually or migrate to shared YAML. Doctor does not run architecture analyzers or certify the legacy snapshot."));
        }
        if (project is null || policy is null)
        {
            checks.Add(new("compiler-severities", scope, "evaluated", "notRun", required, "Severity inspection requires a project and valid YAML.", "Fix workspace/policy blockers and retry."));
            return;
        }
        var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        foreach (var tree in (compilation?.SyntaxTrees ?? []).OrderBy(tree => tree.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = compilation!.Options.SyntaxTreeOptionsProvider;
            foreach (var rule in RuleCatalog.All.Where(rule => rule.Id != "AG003"))
            {
                var selected = policy.SeverityPolicy.RuleSeverities.TryGetValue(rule.Id, out var severity);
                if (legacy) { selected = true; severity = Core.Diagnostics.DiagnosticSeverity.Warning; }
                var configured = ReportDiagnostic.Default;
                var hasOverride = provider is not null && (provider.TryGetDiagnosticValue(tree, rule.Id, cancellationToken, out configured)
                    || provider.TryGetGlobalDiagnosticValue(rule.Id, cancellationToken, out configured));
                if (!selected && !hasOverride) { continue; }
                var effective = !selected ? "inactive (omitted from YAML)" : hasOverride && configured != ReportDiagnostic.Default
                    ? configured.ToString().ToLowerInvariant() : severity.ToString().ToLowerInvariant();
                checks.Add(new("compiler-severity", scope + "::" + tree.FilePath + "::" + rule.Id, "evaluated", "informational", false,
                    $"{rule.Id}: selected={selected}; YAML={(legacy ? "not applied (legacy XML)" : selected ? severity.ToString().ToLowerInvariant() : "omitted")}; editorconfig override={configured.ToString().ToLowerInvariant()}; effective compiler setting={effective}.",
                    legacy ? "Legacy XML selects all supported compiler rules. Review .editorconfig/.globalconfig overrides, including none suppression; solution scans use the separate YAML policy."
                        : "Review .editorconfig/.globalconfig overrides, including none suppression. They do not activate omitted YAML rules or change solution-scan severities/gates."));
            }
        }
        checks.Add(new("compiler-severities", scope, "evaluated", "informational", false,
            "Per-source settings describe the loaded host configuration; no architecture analyzers or rules were run by doctor. Build warning policies may further promote diagnostics.",
            "Use a configured compiler build for editor feedback and a separate scan for solution compliance; an empty source inventory cannot prove compiler feedback."));
    }

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
