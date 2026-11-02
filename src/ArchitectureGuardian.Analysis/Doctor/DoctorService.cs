using System.ComponentModel;
using System.Xml;
using ArchitectureGuardian.Analysis.Discovery;
using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Analysis.Doctor;

/// <summary>Composes read-only installation checks and existing trusted loading/ownership services without evaluating architecture rules.</summary>
public sealed class DoctorService
{
    /// <summary>Diagnoses an explicit input and policy; only trusted requests may evaluate repository build logic or generators.</summary>
    /// <param name="request">Input and explicit build context.</param>
    /// <param name="policyPath">Policy to validate, never modified or discovered implicitly.</param>
    /// <param name="workflow">scan, compiler, mcp or ci; missing optional integrations are informational.</param>
    /// <param name="policyLoader">The host's existing policy reader.</param>
    /// <param name="cancellationToken">Cooperative cancellation, retained as cancelled diagnostic coverage.</param>
    /// <returns>Stable checks and partial evaluated contexts; health never certifies architecture compliance.</returns>
    public async Task<DoctorResult> DiagnoseAsync(WorkspaceLoadRequest request, string policyPath, string workflow,
        IPolicyLoader policyLoader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policyLoader);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyPath);
        if (workflow is not ("scan" or "compiler" or "mcp" or "ci")) { throw new ArgumentException("Choose scan, compiler, mcp or ci.", nameof(workflow)); }
        policyPath = Path.GetFullPath(policyPath);
        var root = Path.GetDirectoryName(policyPath)!;
        var checks = new List<DoctorCheck>();
        var contexts = new List<WorkspaceProjectContext>();
        AnalysisStatus? workspaceStatus = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputValid = File.Exists(request.InputPath) && Path.GetExtension(request.InputPath).ToLowerInvariant() is ".sln" or ".slnx" or ".csproj";
            checks.Add(new("input", request.InputPath, "static", inputValid ? "passed" : "blocked", true,
                inputValid ? "Explicit workspace input exists." : "The input is missing or is not a supported solution/C# project.",
                "Select an existing .sln, .slnx or .csproj; use init --discover <directory> to list choices."));
            try
            {
                var identity = RepositoryModelBuilder.RelativePath(root, request.InputPath);
                checks.Add(new("input-root", request.InputPath, "static", "passed", true, $"Policy-relative input: {identity}.", "Keep the policy directory above the input and all participating projects/source files."));
            }
            catch (ArgumentException exception)
            {
                checks.Add(new("input-root", request.InputPath, "static", "blocked", true, exception.Message, "Move the policy above the input and all participating projects, then adjust its relative selectors."));
            }
            ArchitecturePolicy? policy = null;
            try
            {
                policy = await policyLoader.LoadAsync(policyPath, cancellationToken).ConfigureAwait(false);
                checks.Add(new("policy", policyPath, "static", "passed", true, "Policy YAML and selected rule IDs are valid.", "Review ownership separately against the evaluated inventory."));
                foreach (var rule in policy.SeverityPolicy.RuleSeverities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    checks.Add(new("rule-selection", rule.Key, "static", "informational", false,
                        $"Solution severity: {rule.Value.ToString().ToLowerInvariant()}; gate threshold: {policy.Gate.FailOn.ToString().ToLowerInvariant()}.",
                        rule.Key == "AG003" ? "AG003 requires a separate whole-solution scan; the compiler cannot check cycles." : "Compiler overrides apply only to selected rules; solution scans use YAML severity."));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                checks.Add(new("policy", policyPath, "static", "blocked", true, exception.Message,
                    "Fix the reported YAML location/schema or supply a readable --policy path. No policy was changed."));
            }

            var workingDirectory = Path.GetDirectoryName(request.InputPath)!;
            if (Directory.Exists(workingDirectory))
            {
                await EnvironmentChecksAsync(workingDirectory, checks, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                checks.Add(new("sdk", workingDirectory, "static", "notRun", true, "The input directory does not exist.", "Correct the input path, then retry SDK discovery."));
            }
            DoctorStaticChecks.Inspect(root, request, workflow, checks, cancellationToken);
            if (inputValid)
            {
                try
                {
                    var issues = new List<AnalysisIssue>();
                    foreach (var path in DiscoveryInputs.LiteralProjects(request.InputPath, root, issues, cancellationToken))
                    {
                        var project = Path.GetFullPath(path, root);
                        var assets = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
                        checks.Add(new("assets-hint", project, "static", File.Exists(assets) ? "informational" : "warning", false,
                            $"Unevaluated default assets path {(File.Exists(assets) ? "exists" : "is missing")}: {assets}. Conditions/imports may change participation and asset paths.",
                            "After reviewing build trust, restore explicitly for the selected configuration/framework. Use --trusted to check evaluated assets and coverage."));
                    }
                    foreach (var issue in issues) { checks.Add(new(issue.Code, request.InputPath, "static", "warning", false, issue.Message, "Use trusted evaluation to resolve literal project hints.")); }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
                {
                    checks.Add(new("static-inventory", request.InputPath, "static", "warning", false, exception.Message, "Correct unreadable literal XML/paths; trusted loading determines actual participation."));
                }
            }

            if (!request.IsTrusted || !inputValid || checks.Any(check => check.Id is "sdk" or "runtime" or "input-root" && check.Status == "blocked"))
            {
                foreach (var id in new[] { "workspace", "ownership", "analyzer-installation", "compiler-severities" })
                {
                    checks.Add(new(id, request.InputPath, "evaluated", "notRun", id is "workspace" or "ownership" || workflow == "compiler",
                        "Project/build checks were not run. Static observations do not establish effective settings or ownership coverage.",
                        "Fix static blockers, review repository build logic, then rerun with --trusted --configuration <name> and --framework <tfm> for multi-targeting."));
                }
                return new(request, policyPath, workflow, AnalysisStatus.Incomplete, null, checks, contexts);
            }

            using var workspace = await new MSBuildWorkspaceLoader().LoadAsync(request, cancellationToken).ConfigureAwait(false);
            workspaceStatus = workspace.Status;
            contexts.AddRange(workspace.Contexts);
            checks.Add(new("workspace", request.InputPath, "evaluated", workspace.Status == AnalysisStatus.Completed ? "passed" : "blocked", true,
                $"Workspace coverage: {workspace.Status.ToString().ToLowerInvariant()}; {contexts.Count} evaluated project context(s).",
                "Resolve workspace issues, restore explicitly in the same configuration/framework, then rerun. Partial contexts are retained."));
            foreach (var issue in workspace.Issues)
            {
                checks.Add(new(issue.Code, request.InputPath, "evaluated", "blocked", true, issue.Message,
                    "Resolve the named SDK/framework/assets/reference/compiler/generator problem; restore/build explicitly in this trusted context and retry."));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (policy is not null && workspace.Solution is { } solution)
            {
                try
                {
                    _ = new ResolvedPolicy(policy, solution.Projects.Select(project => ProjectPattern.NormalizeIdentity(Path.GetRelativePath(root, project.FilePath
                        ?? throw new ArgumentException("Project path is unavailable.")).Replace('\\', '/'))));
                    checks.Add(new("ownership", policyPath, "evaluated", workspace.Status == AnalysisStatus.Completed ? "passed" : "warning", true,
                        "Available projects have unique module/layer/contract ownership and selectors match this inventory. Workspace coverage remains independent.",
                        "Review inspect output; only completed workspace coverage establishes a complete ownership inventory."));
                }
                catch (ArgumentException exception)
                {
                    checks.Add(new("ownership", policyPath, "evaluated", "blocked", true, exception.Message,
                        "Adjust stale, overlapping or missing selectors relative to the policy directory, then rerun inspect/doctor. Review discovery proposals manually if needed."));
                }
            }
            else
            {
                checks.Add(new("ownership", policyPath, "evaluated", "notRun", true, "Ownership requires valid YAML and an available workspace.", "Fix policy/workspace blockers and retry; doctor does not synthesize policy."));
            }
            foreach (var context in contexts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DoctorCompilerChecks.InspectAsync(workspace, context, policyPath, policy, workflow == "compiler", checks, cancellationToken).ConfigureAwait(false);
            }
            return new(request, policyPath, workflow, workspace.Status, workspaceStatus, checks, contexts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            checks.Add(new("cancelled", request.InputPath, "static", "notRun", true, "Doctor was cancelled; retained checks and contexts are partial.", "Rerun doctor to obtain complete diagnostics."));
            return new(request, policyPath, workflow, AnalysisStatus.Cancelled, workspaceStatus, checks, contexts);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException or Win32Exception)
        {
            checks.Add(new("doctor-failed", request.InputPath, "static", "blocked", true, exception.Message, "Correct the reported access or host problem and rerun."));
            return new(request, policyPath, workflow, AnalysisStatus.Failed, workspaceStatus, checks, contexts);
        }
    }

    private static async Task EnvironmentChecksAsync(string directory, List<DoctorCheck> checks, CancellationToken cancellationToken)
    {
        foreach (var id in new[] { "sdk", "runtime" })
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                if (id == "sdk")
                {
                    var sdk = await SdkDiscovery.ResolveAsync(directory, deadline.Token).ConfigureAwait(false);
                    checks.Add(new(id, directory, "static", "passed", true, $"Input-selected SDK: {sdk}; global.json selection is honored without evaluating projects.", "Install the SDK/reference/workload packs required by this input. Selection alone does not prove workspace compatibility."));
                }
                else
                {
                    var runtimes = await SdkDiscovery.QueryAsync("--list-runtimes", directory, deadline.Token).ConfigureAwait(false);
                    var available = runtimes.Split('\n').Any(line => line.StartsWith("Microsoft.NETCore.App 10.", StringComparison.Ordinal));
                    checks.Add(new(id, "dotnet", "static", available ? "passed" : "blocked", true,
                        available ? ".NET 10 runtime is available for CLI/MCP." : "The selected dotnet host does not list the .NET 10 runtime required by CLI/MCP.",
                        "Install a .NET 10 runtime/SDK matching the tool's CPU architecture; check dotnet --list-runtimes in the same environment."));
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                checks.Add(new(id, directory, "static", "blocked", true, ".NET metadata query exceeded its 15-second deadline.", "Check the dotnet host/PATH and global.json, then retry."));
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException)
            {
                checks.Add(new(id, directory, "static", "blocked", true, exception.Message, "Install the input's SDK and .NET 10 runtime; check PATH, DOTNET_HOST_PATH and global.json. No restore/install was attempted."));
            }
        }
    }
}
