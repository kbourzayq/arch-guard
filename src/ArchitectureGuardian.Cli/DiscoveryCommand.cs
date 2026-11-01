using System.CommandLine;
using System.Text;
using ArchitectureGuardian.Analysis.Discovery;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Reporting;

namespace ArchitectureGuardian.Cli;

/// <summary>Guides input/context selection and writes create-only discovery artifacts for developer review.</summary>
internal static class DiscoveryCommand
{
    internal static Command Create(TextWriter output, TextWriter error)
    {
        var discover = new Option<string>("--discover") { Required = true, Description = "Solution/C# project, or directory to list input choices without executing builds." };
        var trusted = new Option<bool>("--trusted") { Description = "Authorize evaluated inventory and execution of repository MSBuild logic." };
        var configuration = new Option<string?>("--configuration") { Description = "Explicit build configuration; required with --trusted." };
        var framework = new Option<string?>("--framework") { Description = "Explicit target framework; required for multi-targeted projects." };
        var directory = new Option<string?>("--output-directory") { Description = "Existing directory above the entire inventory; defaults to the selected input's directory. Outputs are never overwritten." };
        var command = new Command("init", "Discover architecture and propose review-only YAML plus a separate JSON evidence report.")
            { discover, trusted, configuration, framework, directory };
        command.Validators.Add(result =>
        {
            if (result.GetResult(trusted) is not null && result.GetResult(configuration) is null)
            {
                result.AddError("--trusted requires an explicit --configuration. Explore each configuration/framework separately.");
            }
            foreach (var option in command.Options)
            {
                if (result.GetResult(option) is { } value && value.IdentifierTokenCount > 1) { result.AddError($"Duplicate option: {option.Name}"); }
            }
        });
        command.SetAction(async (result, token) =>
        {
            var input = Path.GetFullPath(result.GetValue(discover)!);
            if (Directory.Exists(input))
            {
                var choices = DiscoveryInputs.Find(input, token);
                if (choices.Count != 1)
                {
                    error.WriteLine("Choose one .sln, .slnx or C# .csproj with --discover <path>; no build was evaluated.");
                    foreach (var choice in choices) { error.WriteLine("  " + Path.GetRelativePath(input, choice)); }
                    error.WriteLine("Then select --configuration <name> and, for multi-targeting, --framework <tfm>; review build logic before --trusted.");
                    return 2;
                }
                input = choices[0];
            }
            var root = Path.GetFullPath(result.GetValue(directory) ?? Path.GetDirectoryName(input)!);
            if (!Directory.Exists(root)) { throw new ArgumentException("The output directory must already exist above the complete input inventory."); }
            var proposalPath = Path.Combine(root, "architecture.guardian.proposed.yaml");
            var reportPath = Path.Combine(root, "architecture.guardian.discovery.json");
            foreach (var path in new[] { proposalPath, reportPath })
            {
                if (File.Exists(path) || Directory.Exists(path)) { throw new IOException($"Discovery output already exists: {path}. Review and move it before rerunning; outputs are never overwritten."); }
            }
            var request = new WorkspaceLoadRequest(input, result.GetValue(trusted), result.GetValue(configuration) ?? "Debug", result.GetValue(framework));
            var discovery = await new DiscoveryService().DiscoverAsync(request, root, token);
            var ready = discovery.Status == AnalysisStatus.Completed && discovery.Proposal.Policy is not null;
            var yaml = ready ? ProposedPolicyYaml.Serialize(discovery.Proposal.Policy!, token) : null;
            // Even cooperative analysis cancellation retains an explicit cancelled report. No proposal is written.
            await WriteAsync(reportPath, DiscoveryReportSerializer.Serialize(discovery), proposalPath, yaml);
            output.WriteLine($"Discovery coverage: {discovery.Status.ToString().ToLowerInvariant()}; report: {reportPath}");
            foreach (var issue in discovery.Observations.Issues.Concat(discovery.Compliance?.Issues ?? []).Distinct())
            {
                error.WriteLine($"{issue.Code}: {issue.Message}");
            }
            if (!ready)
            {
                error.WriteLine("No safe policy proposal was generated. Review unresolvedQuestions and coverage issues in the report; select an explicit input/configuration/framework and authorize builds with --trusted when ready.");
                return 2;
            }
            output.WriteLine($"Review required: {proposalPath}");
            output.WriteLine("Configuration is valid; architecture compliance is a separate result. Observed forbidden edges remain forbidden.");
            output.WriteLine($"Candidate compliance: {(discovery.Proposal.Policy!.Gate.Passes(discovery.Compliance!) ? "passed" : "failed")}; {discovery.Compliance!.Diagnostics.Count} finding(s). Inspect report evidence and candidate intentions.");
            output.WriteLine("Review ownership, styles, contracts and permissions. Run inspect and scan with --policy architecture.guardian.proposed.yaml in the same context, then manually adopt the reviewed policy. The active policy has not been changed.");
            return 0;
        });
        return command;
    }

    private static async Task WriteAsync(string reportPath, string report, string proposalPath, string? yaml)
    {
        var temporaryReport = reportPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var temporaryProposal = proposalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryReport, report, new UTF8Encoding(false));
            if (yaml is not null) { await File.WriteAllTextAsync(temporaryProposal, yaml, new UTF8Encoding(false)); }
            // Create-only moves also protect files created after preflight. A retained report remains useful
            // if a concurrent proposal collision prevents the second move; the command reports that failure.
            File.Move(temporaryReport, reportPath, overwrite: false);
            if (yaml is not null) { File.Move(temporaryProposal, proposalPath, overwrite: false); }
        }
        finally
        {
            File.Delete(temporaryReport);
            File.Delete(temporaryProposal);
        }
    }
}
