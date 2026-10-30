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
            var advice = response switch
            {
                RulesResponse => "Narrow the ruleId filter, or use a smaller complete policy scope.",
                ArchitectureResponse => "Narrow the module or layer filter, or use a smaller complete input/policy scope.",
                ExplanationResponse { Scope: "rule" } => "Use a smaller complete policy scope.",
                AnalysisResponse or ExplanationResponse => "Use a smaller complete input/policy scope.",
                _ => "Use a smaller complete scope."
            };
            throw new McpException(
                $"Context exceeds 65536 UTF-8 bytes. {advice}");
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
