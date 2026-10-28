using System.Text.Json;
using ArchitectureGuardian.Analysis.Telemetry;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Changes;

/// <summary>Checks that local measurements preserve complete-scan and Git-comparison agreement.</summary>
[Trait("Category", "Integration")]
[Collection("Telemetry")]
public sealed class TelemetryParityTests
{
    /// <summary>Collects every scan phase while retaining deterministic diagnostics and existing-debt classification.</summary>
    [Fact]
    public async Task Instrumented_changes_agree_with_full_scan()
    {
        using var fixture = new ChangeInputFiles();
        fixture.AddReference();
        await fixture.Initialize();
        var expected = await fixture.FullScan();
        using var output = new StringWriter();
        using var telemetry = new LocalTelemetryListener(output);
        var actual = await fixture.FullScan();
        var changes = await fixture.Changes();
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
        Assert.Equal(actual.Diagnostics.Select(item => item.Id), changes.Analysis.Diagnostics.Select(item => item.Id));
        Assert.True(changes.GatePassed, string.Join('\n', changes.Analysis.Issues.Select(item => item.Message)));
        Assert.NotEmpty(changes.Comparison!.Existing);
        Assert.Empty(changes.Comparison.New);
        var phases = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line).GetProperty("tags").GetProperty("phase").GetString()).ToHashSet();
        Assert.Subset(phases, new HashSet<string?> { "workspace", "graph", "rules", "rule", "analysis", "changes" });
    }
}
