using System.CommandLine;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Configuration.Validation;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Cli;

/// <summary>Defines CLI syntax and maps parsing, configuration and cancellation failures to exit two.</summary>
internal static class CliApplication
{
    internal static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
    {
        var renderer = new ConsoleRenderer(output, error);
        var root = new RootCommand("Architecture Guardian for .NET — archguard\nRestore inputs before loading; MSBuild may execute repository tasks.");
        root.Add(WorkspaceCommand("scan", "Evaluate selected architecture rules and the severity gate.", renderer));
        root.Add(WorkspaceCommand("inspect", "List resolved modules, contracts, layers and policy constraints.", renderer));
        root.Add(WorkspaceCommand("load", "Validate workspace coverage without evaluating architecture rules.", renderer));
        var ruleId = new Argument<string>("rule-id") { Description = "Canonical, case-sensitive rule ID (AG001–AG006)." };
        var explain = new Command("explain", "Show canonical rule metadata and its documentation URL.") { ruleId };
        explain.SetAction(result =>
        {
            var id = result.GetValue(ruleId);
            var descriptor = RuleCatalog.All.SingleOrDefault(rule => rule.Id == id);
            if (descriptor is null)
            {
                error.WriteLine($"Unknown rule ID '{id}'. Use a canonical ID: AG001–AG006 (case-sensitive).");
                return 2;
            }

            renderer.Explain(descriptor);
            return 0;
        });
        root.Add(explain);
        var parsed = root.Parse(args.Length == 0 ? ["--help"] : args);
        if (parsed.Errors.Count > 0)
        {
            foreach (var problem in parsed.Errors)
            {
                error.WriteLine(problem.Message);
            }

            error.WriteLine("Use --help or <command> --help for usage.");
            return 2;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await parsed.InvokeAsync(new InvocationConfiguration
            {
                Output = output,
                Error = error,
                EnableDefaultExceptionHandler = false,
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Command cancelled. Analysis did not complete; rerun to obtain reliable results.");
            return 2;
        }
        catch (Exception exception) when (exception is PolicyConfigurationException or ArgumentException
            or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"Invalid command input: {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Command failed: {exception.GetType().Name}: {exception.Message}");
            return 2;
        }
    }

    private static Command WorkspaceCommand(string name, string description, ConsoleRenderer renderer)
    {
        var input = new Argument<string>("input") { Description = "Path to a .sln, .slnx or .csproj file." };
        var trusted = new Option<bool>("--trusted") { Description = "Authorize execution of this repository's MSBuild logic." };
        var configuration = new Option<string>("--configuration")
        {
            Description = "Build configuration.",
            DefaultValueFactory = _ => "Debug",
        };
        var framework = new Option<string?>("--framework") { Description = "Target framework; required for multi-targeted projects." };
        var policy = new Option<string>("--policy") { Description = "Explicit YAML policy path; identities are relative to its directory.", Required = true };
        var command = new Command(name, description) { input, trusted, configuration, framework };
        if (name != "load")
        {
            command.Add(policy);
        }

        // Reject repeated flags as well as repeated value options instead of silently accepting a typo.
        command.Validators.Add(result =>
        {
            foreach (var option in command.Options)
            {
                if (result.GetResult(option) is { } optionResult && optionResult.IdentifierTokenCount > 1)
                {
                    result.AddError($"Duplicate option: {option.Name}");
                }
            }
        });
        command.SetAction(async (result, token) =>
        {
            var request = new WorkspaceLoadRequest(result.GetValue(input)!, result.GetValue(trusted),
                result.GetValue(configuration)!, result.GetValue(framework));
            return await WorkspaceCommands.RunAsync(name, request, name == "load" ? null : result.GetValue(policy), renderer, token);
        });
        return command;
    }
}
