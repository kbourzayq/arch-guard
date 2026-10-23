using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Baselines;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Analysis.Changes;

/// <summary>Loads and evaluates independent full architecture contexts for a Git base and the working tree.</summary>
internal static class ChangeAnalysisService
{
    internal static async Task<ChangeAnalysisResult> AnalyzeAsync(WorkspaceLoadRequest request, string policyPath,
        string baseReference, IPolicyLoader loader, string? allowedRoot, CancellationToken cancellationToken)
    {
        using var snapshots = new GitSnapshots();
        ArchitecturePolicy? candidatePolicy = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSupportedBuildValue(request.Configuration)
                || request.TargetFramework is not null && !IsSupportedBuildValue(request.TargetFramework))
            {
                return new(new(AnalysisStatus.Incomplete, issues:
                [new("change-build-context-invalid", "Change-analysis configuration/framework values may contain only letters, digits, spaces, '.', '_' and '-'. MSBuild separators and expressions cannot be safely preserved through solution child builds.")]));
            }
            if (!request.IsTrusted)
            {
                return new(new(AnalysisStatus.Incomplete, issues:
                [
                    new("workspace-untrusted",
                        "Change analysis executes MSBuild logic from both the base and candidate. Explicitly trust both snapshots before loading.")
                ]));
            }

            var root = Path.GetDirectoryName(request.InputPath)!;
            var prefix = await GitSnapshots.Git(root, ["rev-parse", "--show-prefix"], cancellationToken).ConfigureAwait(false);
            // Preserve the caller's lexical path (for example /var versus /private/var on macOS).
            foreach (var part in prefix.TrimEnd('\r', '\n').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                root = Path.GetDirectoryName(root) ?? throw new IOException("Cannot establish the Git repository root.");
            }
            if (allowedRoot is not null)
            {
                GitSnapshots.Inside(allowedRoot, root);
            }

            GitSnapshots.Inside(root, request.InputPath);
            GitSnapshots.Inside(root, Path.GetFullPath(policyPath));
            await snapshots.CaptureAsync(root, baseReference, cancellationToken).ConfigureAwait(false);
            var input = Path.GetRelativePath(root, request.InputPath);
            var policy = Path.GetRelativePath(root, Path.GetFullPath(policyPath));
            var candidate = await ScanAsync(snapshots.CandidateDirectory).ConfigureAwait(false);
            candidatePolicy = candidate.Policy;
            var baseline = await ScanAsync(snapshots.BaseDirectory).ConfigureAwait(false);
            var issues = baseline.Result.Issues.Select(issue =>
                    new AnalysisIssue(issue.Code, "Base snapshot: " + issue.Message, issue.RuleId, issue.Location))
                .Concat(candidate.Result.Issues.Select(issue => new AnalysisIssue(issue.Code,
                    "Candidate snapshot: " + issue.Message, issue.RuleId, issue.Location)));
            var status =
                baseline.Result.Status == AnalysisStatus.Cancelled ||
                candidate.Result.Status == AnalysisStatus.Cancelled
                    ? AnalysisStatus.Cancelled
                    : baseline.Result.Status == AnalysisStatus.Failed ||
                      candidate.Result.Status == AnalysisStatus.Failed
                        ? AnalysisStatus.Failed
                        : baseline.Result.IsComplete && candidate.Result.IsComplete
                            ? AnalysisStatus.Completed
                            : AnalysisStatus.Incomplete;
            var analysis = new AnalysisResult(status, candidate.Result.Diagnostics, issues);
            var comparison = baseline.Result.IsComplete
                ? new BaselineComparison(analysis, BaselineSnapshot.Capture(baseline.Result))
                : null;
            return new(analysis, snapshots.Commit, comparison, candidatePolicy);

            async Task<(AnalysisResult Result, ArchitecturePolicy? Policy)> ScanAsync(string directory)
            {
                ArchitecturePolicy? snapshotPolicy = null;
                try
                {
                    var snapshotInput = GitSnapshots.Inside(directory, input);
                    var snapshotPolicyPath = GitSnapshots.Inside(directory, policy);
                    if (!File.Exists(snapshotInput) || !File.Exists(snapshotPolicyPath))
                    {
                        throw new IOException(
                            "The requested input and policy must exist at the same repository-relative paths in both snapshots. Renames of projects inside the input are supported.");
                    }

                    // The host-supplied loader retains policy validation and size limits for BOTH snapshots.
                    snapshotPolicy =
                        await loader.LoadAsync(snapshotPolicyPath, cancellationToken).ConfigureAwait(false);
                    var workingDirectory = Path.GetDirectoryName(snapshotInput)!;
                    var sdkVersion = await SdkDiscovery.ResolveAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
                    if (sdkVersion.Major < 6)
                    {
                        throw new IOException("Change snapshots require SDK 6 or later with NuGet package source mapping support.");
                    }
                    var config = Path.Combine(directory,
                        "archguard-offline-" + Guid.NewGuid().ToString("N") + ".config");
                    var feed = Directory.CreateDirectory(Path.ChangeExtension(config, ".feed")).FullName;
                    // SDK imports can mark additional sources TreatAsLocalProperty and override global properties.
                    // Source mapping also prevents those sources from supplying any uncached package.
                    var settings = new XElement("configuration",
                        new XElement("packageSources", new XElement("clear"),
                            new XElement("add", new XAttribute("key", "archguard-cache-only"), new XAttribute("value", feed))),
                        new XElement("packageSourceMapping",
                            new XElement("packageSource", new XAttribute("key", "archguard-cache-only"),
                                new XElement("package", new XAttribute("pattern", "*")))));
                    await File.WriteAllTextAsync(config, settings.ToString(), cancellationToken).ConfigureAwait(false);
                    try
                    {
                        var arguments = new List<string>
                        {
                            "restore", snapshotInput, "--configfile", config, "--source", feed, "--disable-parallel",
                            "--disable-build-servers",
                            "-p:Configuration=" + EscapeProperty(request.Configuration), "-p:NuGetAudit=false",
                            "-p:RestoreLockedMode=true", "-p:RestoreAdditionalProjectSources=" + EscapeProperty(feed)
                        };
                        if (request.TargetFramework is not null)
                        {
                            arguments.Add("-p:TargetFramework=" + EscapeProperty(request.TargetFramework));
                        }

                        await SnapshotProcess.RunAsync(
                            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", workingDirectory,
                            arguments, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        File.Delete(config);
                    }

                    var analysis = await new SolutionAnalysisService().AnalyzeAsync(
                            new(snapshotInput, true, request.Configuration, request.TargetFramework),
                            Path.GetDirectoryName(snapshotPolicyPath)!, snapshotPolicy, cancellationToken)
                        .ConfigureAwait(false);
                    return (analysis, snapshotPolicy);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    return (new(AnalysisStatus.Incomplete, issues:
                    [
                        new("change-snapshot-load-failed",
                            $"{exception.Message} Prepare the required SDK and cached packages for this snapshot; comparison never downloads packages.")
                    ]), snapshotPolicy);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(new(AnalysisStatus.Cancelled), candidatePolicy: candidatePolicy);
        }
        catch (Exception exception)
        {
            return new(new(AnalysisStatus.Incomplete, issues:
            [
                new("change-snapshot-unavailable",
                    $"{exception.Message} Verify the Git base and repository inputs. In shallow clones fetch the required base/history explicitly before retrying.")
            ]), candidatePolicy: candidatePolicy);
        }
    }

    private static bool IsSupportedBuildValue(string value) =>
        value.All(character => char.IsLetterOrDigit(character) || character is ' ' or '.' or '_' or '-');

    private static string EscapeProperty(string value)
    {
        // MSBuild parses property separators after process argument splitting, so shell-safe arguments alone are insufficient.
        var escaped = new StringBuilder();
        foreach (var character in value)
        {
            if (character is '%' or '*' or '?' or '@' or '$' or '(' or ')' or ';' or ',' or '\'' or '"')
            {
                escaped.Append('%').Append(((int)character).ToString("X2", CultureInfo.InvariantCulture));
            }
            else { escaped.Append(character); }
        }

        return escaped.ToString();
    }
}
