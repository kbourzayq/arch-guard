using System.CommandLine;
using System.Text.Json;
using ArchitectureGuardian.Analysis.Doctor;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Yaml;

namespace ArchitectureGuardian.Cli;

/// <summary>Exposes installation diagnostics as text or a versioned JSON envelope on stdout.</summary>
internal static class DoctorCommand
{
    internal static Command Create(TextWriter output)
    {
        var input = new Argument<string>("input") { Description = "Explicit .sln, .slnx or C# project path." };
        var policy = new Option<string>("--policy") { Required = true, Description = "Explicit YAML policy; never modified." };
        var trusted = new Option<bool>("--trusted") { Description = "Authorize workspace evaluation and repository build logic/generators. No restore or install." };
        var configuration = new Option<string?>("--configuration") { Description = "Build configuration; required explicitly with --trusted." };
        var framework = new Option<string?>("--framework") { Description = "Explicit framework; required for multi-targeted projects." };
        var workflow = new Option<string>("--workflow") { DefaultValueFactory = _ => "scan", Description = "Required integration: scan (default), compiler, mcp or ci." };
        var format = new Option<string>("--format") { DefaultValueFactory = _ => "text", Description = "text (default) or json (doctor schema v1)." };
        var command = new Command("doctor", "Diagnose installation and policy health; architecture compliance requires a separate scan.")
            { input, policy, trusted, configuration, framework, workflow, format };
        command.Validators.Add(result =>
        {
            if (result.GetResult(trusted) is not null && result.GetResult(configuration) is null)
            {
                result.AddError("--trusted requires an explicit --configuration; select --framework for multi-targeting.");
            }
            foreach (var option in command.Options)
            {
                if (result.GetResult(option) is not { } supplied) { continue; }
                if (supplied.IdentifierTokenCount > 1) { result.AddError($"Duplicate option: {option.Name}"); }
                if (supplied.Tokens.Count == 1 && string.IsNullOrWhiteSpace(supplied.Tokens[0].Value)) { result.AddError($"{option.Name} cannot be blank."); }
            }
            if (result.GetResult(format)?.Tokens is { Count: 1 } formats && formats[0].Value is not ("text" or "json"))
            {
                result.AddError("--format must be text or json (case-sensitive).");
            }
            if (result.GetResult(workflow)?.Tokens is { Count: 1 } workflows && workflows[0].Value is not ("scan" or "compiler" or "mcp" or "ci"))
            {
                result.AddError("--workflow must be scan, compiler, mcp or ci.");
            }
        });
        command.SetAction(async (result, token) =>
        {
            var request = new WorkspaceLoadRequest(result.GetValue(input)!, result.GetValue(trusted), result.GetValue(configuration) ?? "Debug", result.GetValue(framework));
            var diagnosis = await new DoctorService().DiagnoseAsync(request, result.GetValue(policy)!, result.GetValue(workflow)!, new YamlPolicyLoader(), token);
            if (result.GetValue(format) == "json")
            {
                var report = JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    kind = "architecture-doctor",
                    status = diagnosis.Status.ToString().ToLowerInvariant(),
                    workspaceStatus = diagnosis.WorkspaceStatus?.ToString().ToLowerInvariant(),
                    exitCode = diagnosis.ExitCode,
                    compliance = "notEvaluated",
                    requested = new
                    {
                        input = request.InputPath,
                        policy = diagnosis.PolicyPath,
                        workflow = diagnosis.Workflow,
                        trusted = request.IsTrusted,
                        configuration = request.Configuration,
                        framework = request.TargetFramework,
                    },
                    exploredContexts = diagnosis.Contexts.Select(context => new
                    {
                        project = context.ProjectPath,
                        configuration = context.Configuration,
                        framework = context.TargetFramework,
                        declaredFrameworks = context.DeclaredFrameworks,
                    }),
                    checks = diagnosis.Checks.Select(check => new
                    {
                        id = check.Id,
                        scope = check.Scope,
                        mode = check.Mode,
                        status = check.Status,
                        required = check.Required,
                        message = check.Message,
                        remediation = check.Remediation,
                    }),
                }, new JsonSerializerOptions { WriteIndented = true });
                await output.WriteAsync(report.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
            }
            else
            {
                output.WriteLine($"Doctor: {diagnosis.Status.ToString().ToLowerInvariant()}; workflow={diagnosis.Workflow}; exit={diagnosis.ExitCode}");
                output.WriteLine($"Input: {request.InputPath}; policy: {diagnosis.PolicyPath}; trusted={request.IsTrusted}; configuration={request.Configuration}; framework={request.TargetFramework ?? "single declared framework"}");
                foreach (var context in diagnosis.Contexts) { output.WriteLine($"Evaluated: {context.ProjectPath}; configuration={context.Configuration}; framework={context.TargetFramework}; declared=[{string.Join(", ", context.DeclaredFrameworks)}]"); }
                foreach (var check in diagnosis.Checks)
                {
                    output.WriteLine($"[{check.Mode}/{check.Status}/{(check.Required ? "required" : "optional")}] {check.Id} ({check.Scope}): {check.Message}");
                    output.WriteLine("  Next: " + check.Remediation);
                }
                output.WriteLine("Environment/configuration health does not imply architecture compliance. Run a separate archguard scan with the same input, policy, configuration/framework and explicit trust.");
            }
            return diagnosis.ExitCode;
        });
        return command;
    }
}
