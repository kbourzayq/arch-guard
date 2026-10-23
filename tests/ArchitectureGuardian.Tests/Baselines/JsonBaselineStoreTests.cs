using System.Text.Json.Nodes;
using ArchitectureGuardian.Analysis.Baselines;
using ArchitectureGuardian.Core.Baselines;
using Xunit;

namespace ArchitectureGuardian.Tests.Baselines;

/// <summary>Verifies strict persistence, deterministic reviewable output and preservation on cancellation/failure.</summary>
public sealed class JsonBaselineStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "archguard-baselines-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates an isolated persistence directory.</summary>
    public JsonBaselineStoreTests() => Directory.CreateDirectory(_directory);

    /// <summary>Round-trips semantic identities and verifies deterministic byte output across input permutations.</summary>
    [Fact]
    public async Task Roundtrip_is_deterministic_and_does_not_mutate_on_read()
    {
        var path = Path.Combine(_directory, "baseline.json");
        var store = new JsonBaselineStore();
        var first = BaselineTests.Finding();
        var second = BaselineTests.Finding(symbol: "Other");
        await store.SaveAsync(path, BaselineSnapshot.Capture(BaselineTests.Completed(first, second)), TestContext.Current.CancellationToken);
        var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal(2, new BaselineComparison(BaselineTests.Completed(first, second), loaded).Existing.Count);
        await store.SaveAsync(path, BaselineSnapshot.Capture(BaselineTests.Completed(second, first)), TestContext.Current.CancellationToken);
        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Contains("Library.Contract", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(_directory));
    }

    /// <summary>Malformed and unsupported inventories never become a passing empty baseline.</summary>
    /// <param name="json">An invalid persisted document.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"entries\":[]}")]
    [InlineData("{\"version\":1,\"entries\":null}")]
    [InlineData("{\"version\":1,\"entries\":[null]}")]
    [InlineData("{\"version\":1,\"entries\":[],\"typo\":true}")]
    [InlineData("{\"version\":1,\"version\":1,\"entries\":[]}")]
    public async Task Invalid_documents_fail_explicitly(string json)
    {
        var path = Path.Combine(_directory, "invalid.json");
        await File.WriteAllTextAsync(path, json, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonBaselineStore().LoadAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(json, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>Changing digest or identity fields without explicit regeneration is rejected.</summary>
    /// <param name="field">The field to corrupt.</param>
    [Theory]
    [InlineData("fingerprint")]
    [InlineData("ruleId")]
    [InlineData("symbol")]
    [InlineData("dependencyPath")]
    [InlineData("constraints")]
    public async Task Tampered_identity_is_rejected(string field)
    {
        var path = Path.Combine(_directory, "baseline.json");
        var store = new JsonBaselineStore();
        await store.SaveAsync(path, BaselineSnapshot.Capture(BaselineTests.Completed(BaselineTests.Finding())), TestContext.Current.CancellationToken);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
        var entry = document["entries"]![0]!;
        entry[field] = field is "dependencyPath" or "constraints" ? null : JsonValue.Create("changed");
        await File.WriteAllTextAsync(path, document.ToJsonString(), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>Missing files and cancelled writes preserve prior content and create no temporary litter.</summary>
    [Fact]
    public async Task Missing_baseline_and_cancelled_update_do_not_accept_findings()
    {
        var store = new JsonBaselineStore();
        var path = Path.Combine(_directory, "baseline.json");
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync(path, TestContext.Current.CancellationToken));
        await File.WriteAllTextAsync(path, "prior baseline", TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(path, new BaselineSnapshot([]), cancelled.Token));
        Assert.Equal("prior baseline", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(_directory));
    }

    /// <summary>Removes test output.</summary>
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
