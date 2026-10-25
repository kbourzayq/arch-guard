using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Configuration.Validation;
using ArchitectureGuardian.Mcp.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Validates explicit tool inputs under the operator root before reading policy or loading a workspace.</summary>
internal sealed class WorkspaceFiles(IOptions<WorkspaceOptions> options, IPolicyLoader loader)
{
    internal string Resolve(string path)
    {
        ValidateText(path, "path", 4096);
        var root = Path.GetFullPath(options.Value.Root);
        var fullPath = Path.GetFullPath(path, root);
        var relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new McpException("Input must be inside Workspace:Root.");
        }

        var current = root;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new McpException(
                    "Input paths cannot traverse symbolic links or reparse points under Workspace:Root.");
            }
        }

        if (!File.Exists(fullPath))
        {
            throw new McpException("Input must name an existing file.");
        }

        return fullPath;
    }

    internal async Task<ArchitecturePolicy> LoadPolicyAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (new FileInfo(path).Length > 262_144)
        {
            throw new McpException("Policy exceeds 262144 bytes.");
        }

        try
        {
            return await loader.LoadAsync(path, cancellationToken);
        }
        catch (PolicyConfigurationException)
        {
            throw new McpException(
                "Invalid or unsupported YAML policy. Validate it with the CLI and the v1 policy schema; no context was returned.");
        }
    }

    internal static void ValidateText(string? value, string name, int limit = 128)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit)
        {
            throw new McpException($"{name} must contain 1–{limit} characters and cannot be blank.");
        }
    }
}
