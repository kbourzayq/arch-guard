using System.Text.Json;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ModelContextProtocol.Client;
using Xunit;
using static ArchitectureGuardian.ArchitectureTests.Mcp.CorrectionFiles;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Verifies manual onboarding without discovery: a separate candidate is validated before explicit adoption.</summary>
/// <remarks>Each interface derives its own class, and therefore its own collection, so CLI and MCP scenarios run in parallel.</remarks>
public abstract class OnboardingWorkflowTests
{
    /// <summary>Gets whether the scenario drives the MCP server instead of the CLI process.</summary>
    protected abstract bool UseMcp { get; }

    /// <summary>Manual onboarding works in the selected interface and revalidates the adopted path after a deliberate client decision.</summary>
    /// <param name="existingPolicy">Whether an approved active policy already exists beside the candidate.</param>
    /// <returns>A task that completes after the scenario has been verified.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Onboarding_validates_a_separate_candidate_before_explicit_adoption(bool existingPolicy)
    {
        using var fixture = new CorrectionFiles();
        var active = Path.Combine(fixture.Root, "architecture.guardian.yaml");
        var proposed = Path.Combine(fixture.Root, "architecture.guardian.proposed.yaml");
        var candidate = await File.ReadAllBytesAsync(active, TestContext.Current.CancellationToken);
        File.Move(active, proposed);
        byte[]? approved = null;
        if (existingPolicy)
        {
            // This approved policy deliberately allows implementation access. Review tightens only the candidate.
            var previous = System.Text.Encoding.UTF8.GetString(candidate).Replace("contract:Library/api", "module:Library", StringComparison.Ordinal);
            approved = System.Text.Encoding.UTF8.GetBytes(previous);
            await File.WriteAllBytesAsync(active, approved, TestContext.Current.CancellationToken);
        }

        async Task Run(McpClient? client, CancellationToken token)
        {
            async Task<JsonDocument> Scan(string policy, bool complete, bool passed)
            {
                if (client is not null)
                {
                    var arguments = Arguments();
                    arguments["policyPath"] = policy;
                    using var rules = Parse(await fixture.Call(client, "list_rules", new() { ["policyPath"] = policy }, token));
                    Assert.Equal("AG001", Assert.Single(rules.RootElement.GetProperty("rules").EnumerateArray(), rule => rule.GetProperty("selected").GetBoolean()).GetProperty("id").GetString());
                    using var architecture = Parse(await fixture.Call(client, "get_architecture", arguments, token));
                    var response = Parse(await fixture.Call(client, "analyze_solution", arguments, token));
                    Assert.Equal(complete, response.RootElement.GetProperty("status").GetString() == "completed");
                    Assert.Equal(complete ? passed ? "passed" : "failed" : "not-evaluated", response.RootElement.GetProperty("gateOutcome").GetString());
                    return response;
                }

                var common = new[] { Path.Combine(fixture.Root, "Correction.slnx"), "--policy", Path.Combine(fixture.Root, policy), "--configuration", "Release", "--trusted" };
                var inspection = await Repository.Dotnet(TimeSpan.FromSeconds(60), [Path.Combine(AppContext.BaseDirectory, "ArchitectureGuardian.Cli.dll"), "inspect", .. common]);
                Assert.Equal(complete ? 0 : 2, inspection.ExitCode);
                var result = await Repository.Dotnet(TimeSpan.FromSeconds(60), [Path.Combine(AppContext.BaseDirectory, "ArchitectureGuardian.Cli.dll"), "scan", .. common, "--format", "json"]);
                Assert.True(result.ExitCode == (complete ? passed ? 0 : 1 : 2), result.Output + result.Error);
                var json = JsonDocument.Parse(result.Output);
                Assert.Equal(complete, json.RootElement.GetProperty("status").GetString() == "completed");
                Assert.Equal(complete ? passed ? "passed" : "failed" : "notEvaluated", json.RootElement.GetProperty("gateOutcome").GetString());
                return json;
            }

            // No restored assets: an empty/partial result must stay unresolved in the chosen interface.
            using var unavailable = await Scan("architecture.guardian.proposed.yaml", complete: false, passed: false);
            Assert.NotEmpty(unavailable.RootElement.GetProperty("issues").EnumerateArray());
            await fixture.Restore();
            using var before = await Scan("architecture.guardian.proposed.yaml", complete: true, passed: false);
            Assert.Equal("AG001", Assert.Single(before.RootElement.GetProperty("diagnostics").EnumerateArray()).GetProperty("ruleId").GetString());
            Assert.Equal(candidate, await File.ReadAllBytesAsync(proposed, token));
            if (existingPolicy) { Assert.Equal(approved, await File.ReadAllBytesAsync(active, token)); }
            else { Assert.False(File.Exists(active)); }

            // Refusing replacement preserves an existing policy; no helper may silently choose adoption.
            if (existingPolicy)
            {
                Assert.Throws<IOException>(() => File.Copy(proposed, active, overwrite: false));
                Assert.Equal(approved, await File.ReadAllBytesAsync(active, token));
            }

            // Scripted developer approval: adopt the reviewed bytes, then validate the active path too.
            File.Copy(proposed, active, overwrite: existingPolicy);
            using var adopted = await Scan("architecture.guardian.yaml", complete: true, passed: false);
            await fixture.Correct(token);
            await fixture.Restore();
            using var after = await Scan("architecture.guardian.yaml", complete: true, passed: true);
            Assert.Empty(after.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(candidate, await File.ReadAllBytesAsync(active, token));
        }

        if (UseMcp) { await ToolRegistrationTests.WithServer(Run, root: fixture.Root, trusted: true); }
        else { await Run(null, TestContext.Current.CancellationToken); }
    }
}
