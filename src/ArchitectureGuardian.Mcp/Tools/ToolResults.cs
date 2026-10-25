using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Produces bounded, matching structured and text payloads without silently truncating context.</summary>
internal static class ToolResults
{
    internal const int MaxPayloadBytes = 65_536;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static CallToolResult Create<T>(T response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        if (bytes.Length > MaxPayloadBytes)
        {
            throw new McpException(
                "Context exceeds 65536 UTF-8 bytes. Narrow the module/layer or ruleId filter, or use a smaller policy/input scope.");
        }

        var content = JsonSerializer.Deserialize<JsonElement>(bytes);
        cancellationToken.ThrowIfCancellationRequested();
        return new()
        {
            IsError = false,
            StructuredContent = content,
            Content = [new TextContentBlock { Text = content.GetRawText() }]
        };
    }
}
