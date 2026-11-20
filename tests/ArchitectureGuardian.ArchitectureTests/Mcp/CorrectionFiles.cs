using System.Text.Json;
using System.Xml.Linq;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Owns an isolated agent-correction fixture copy and checks that every tool call preserves all non-build inputs byte for byte.</summary>
internal sealed class CorrectionFiles : IDisposable
{
    /// <summary>Gets the absolute root of this fixture copy.</summary>
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "archguard-correction-" + Guid.NewGuid().ToString("N"));

    /// <summary>Copies the agent-correction fixture and the repository SDK selection into a new temporary root.</summary>
    internal CorrectionFiles()
    {
        var source = Path.Combine(Repository.Root, "tests", "Fixtures", "AgentCorrection");
        foreach (var file in Inputs(source))
        {
            var destination = Path.Combine(Root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        File.Copy(Path.Combine(Repository.Root, "global.json"), Path.Combine(Root, "global.json"));
    }

    /// <summary>Restores the fixture solution for the Release configuration and asserts success.</summary>
    /// <returns>A task that completes after restore succeeds.</returns>
    internal async Task Restore()
    {
        var result = await Repository.Dotnet(TimeSpan.FromSeconds(60), "restore", Path.Combine(Root, "Correction.slnx"),
            "-p:Configuration=Release", "--disable-parallel", "--disable-build-servers");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
    }

    /// <summary>Commits the seeded violation to a new Git repository that ignores build outputs.</summary>
    /// <returns>A task that completes after the initial commit.</returns>
    internal async Task InitializeGit()
    {
        File.WriteAllText(Path.Combine(Root, ".gitignore"), "**/bin/\n**/obj/\n");
        await ChangeInputFiles.GitAt(Root, "init", "-b", "main");
        await ChangeInputFiles.GitAt(Root, "add", ".");
        await ChangeInputFiles.GitAt(Root, "-c", "user.name=Architecture Tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "seeded violation");
    }

    /// <summary>Applies the client-side correction: depend on the public contract instead of the implementation.</summary>
    /// <param name="token">Cancels the file writes.</param>
    /// <returns>A task that completes after both inputs are rewritten.</returns>
    internal async Task Correct(CancellationToken token)
    {
        await File.WriteAllTextAsync(Path.Combine(Root, "Consumer", "Checkout.cs"), """
            namespace Consumer;

            public sealed class Checkout
            {
                public void Pay(Library.Contracts.IInvoice invoice) => invoice.Pay();
            }
            """, token);
        var path = Path.Combine(Root, "Consumer", "Consumer.csproj");
        var project = XDocument.Load(path);
        project.Descendants("ProjectReference").Single(reference =>
            reference.Attribute("Include")!.Value == "../Library.Implementation/Library.Implementation.csproj").Remove();
        project.Save(path);
    }

    /// <summary>Calls a tool and asserts that it left every non-build input unchanged.</summary>
    /// <param name="client">The connected MCP client.</param>
    /// <param name="tool">The tool name.</param>
    /// <param name="arguments">The tool arguments.</param>
    /// <param name="token">Cancels the call.</param>
    /// <returns>The unmodified tool result.</returns>
    internal async Task<CallToolResult> Call(McpClient client, string tool, Dictionary<string, object?> arguments, CancellationToken token)
    {
        var before = Snapshot();
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: token);
        var after = Snapshot();
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) { Assert.Equal(bytes, after[path]); }
        return result;
    }

    private Dictionary<string, byte[]> Snapshot() => Inputs(Root).ToDictionary(path => Path.GetRelativePath(Root, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static IEnumerable<string> Inputs(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    /// <summary>Creates the tool arguments that select the fixture solution, active policy and Release configuration.</summary>
    /// <returns>A new mutable argument dictionary owned by the caller.</returns>
    internal static Dictionary<string, object?> Arguments() => new()
    {
        ["inputPath"] = "Correction.slnx",
        ["policyPath"] = "architecture.guardian.yaml",
        ["configuration"] = "Release"
    };

    /// <summary>Asserts that a structured tool result completed without coverage issues.</summary>
    /// <param name="result">The structured tool result.</param>
    internal static void AssertComplete(JsonElement result)
    {
        Assert.True(result.GetProperty("status").GetString() == "completed", result.GetRawText());
        Assert.Empty(result.GetProperty("issues").EnumerateArray());
    }

    /// <summary>Asserts a successful tool result whose text and structured content agree, then parses it.</summary>
    /// <param name="result">The tool result.</param>
    /// <returns>The parsed structured content, owned by the caller.</returns>
    internal static JsonDocument Parse(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.False(result.IsError, text);
        Assert.Equal(text, result.StructuredContent!.Value.GetRawText());
        return JsonDocument.Parse(text);
    }

    /// <summary>Removes fixture inputs and build outputs after the protocol client has shut down.</summary>
    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) { File.SetAttributes(file, FileAttributes.Normal); }
        Directory.Delete(Root, recursive: true);
    }
}
