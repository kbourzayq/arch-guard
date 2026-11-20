using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Core.Baselines;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Changes;

/// <summary>Verifies that candidate-only policy and project inventory changes match independent full-scan comparisons.</summary>
[Trait("Category", "Integration")]
[Collection(nameof(ChangeInventoryTests))]
public sealed class ChangeInventoryTests
{
    /// <summary>Each tree uses its own inventory and policy; renamed identities remain conservative new findings.</summary>
    /// <param name="scenario">The candidate-only policy or project inventory change.</param>
    [Theory]
    [InlineData("policy")]
    [InlineData("rename")]
    [InlineData("remove")]
    public async Task Policy_and_project_changes_match_full_scan_comparison(string scenario)
    {
        using var fixture = new ChangeInputFiles();
        fixture.AddReference();
        await fixture.Initialize();
        var baseline = await fixture.FullScan();
        Assert.NotEmpty(baseline.Diagnostics);
        if (scenario == "policy")
        {
            fixture.Write("policy.yaml", ChangeInputFiles.Policy.Replace("- id: App", "- id: App\n    allowedDependencies: [\"module:Library\"]", StringComparison.Ordinal));
        }
        else if (scenario == "rename")
        {
            Directory.Move(Path.Combine(fixture.Root, "Library"), Path.Combine(fixture.Root, "Renamed"));
            foreach (var path in new[] { "Input.slnx", "policy.yaml", "Directory.Build.targets" })
            {
                var content = File.ReadAllText(Path.Combine(fixture.Root, path)).Replace("Library/", "Renamed/", StringComparison.Ordinal);
                fixture.Write(path, content);
            }
        }
        else
        {
            fixture.Write("Input.slnx", "<Solution><Project Path=\"Library/Library.csproj\" /></Solution>");
            fixture.Write("policy.yaml", ChangeInputFiles.Policy.Replace("  - id: App\n    projects: [\"App/*.csproj\"]\n", "", StringComparison.Ordinal));
        }

        var candidate = await fixture.FullScan();
        var expected = new BaselineComparison(candidate, BaselineSnapshot.Capture(baseline));
        var changes = await fixture.Changes();
        Assert.True(changes.Analysis.IsComplete, string.Join('\n', changes.Analysis.Issues.Select(item => item.Message)));
        Assert.Equal(candidate.Diagnostics.Select(item => item.Id), changes.Analysis.Diagnostics.Select(item => item.Id));
        Assert.Equal(expected.Existing.Select(item => item.Id), changes.Comparison!.Existing.Select(item => item.Id));
        Assert.Equal(expected.New.Select(item => item.Id), changes.Comparison.New.Select(item => item.Id));
        Assert.Equal(expected.Resolved.Select(item => item.Fingerprint), changes.Comparison.Resolved.Select(item => item.Fingerprint));
        Assert.Equal(scenario != "rename", changes.GatePassed);
        Assert.NotEmpty(changes.Comparison.Resolved);
    }
}
