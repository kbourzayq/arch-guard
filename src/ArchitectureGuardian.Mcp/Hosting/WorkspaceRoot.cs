namespace ArchitectureGuardian.Mcp.Hosting;

/// <summary>Constrains explicit file inputs to one physical, operator-selected workspace directory.</summary>
internal sealed class WorkspaceRoot
{
    internal WorkspaceRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path))
        {
            throw new ArgumentException("The workspace root must be an existing directory.");
        }

        Path = ResolvePhysicalPath(System.IO.Path.GetFullPath(path));
    }

    internal string Path { get; }

    internal string ResolveFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = ResolvePhysicalPath(System.IO.Path.GetFullPath(path, Path));
        var relative = System.IO.Path.GetRelativePath(Path, full);
        if (System.IO.Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Input must remain inside the configured workspace root.");
        }

        if (!File.Exists(full))
        {
            throw new ArgumentException("Input must be an existing workspace file.");
        }

        return full;
    }

    private static string ResolvePhysicalPath(string path, int linkDepth = 0)
    {
        if (linkDepth > 64)
        {
            throw new ArgumentException("Input contains a symbolic-link cycle or exceeds 64 link expansions.");
        }

        var current = System.IO.Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.Combine(current, part);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.LinkTarget is not null)
            {
                var target = entry.ResolveLinkTarget(returnFinalTarget: false)?.FullName
                    ?? throw new ArgumentException("Input contains an unresolved symbolic link.");
                // A link's target may itself contain links in parent directories, not only at its final entry.
                current = ResolvePhysicalPath(target, linkDepth + 1);
            }
        }

        return System.IO.Path.TrimEndingDirectorySeparator(current);
    }
}
