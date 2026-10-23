using System.Security.Cryptography;

namespace ArchitectureGuardian.Analysis.Changes;

/// <summary>Owns isolated base and working-tree snapshots without changing the checkout, index or refs.</summary>
internal sealed class GitSnapshots : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "archguard-changes-" + Guid.NewGuid().ToString("N"));

    internal string BaseDirectory => Path.Combine(_directory, "base");
    internal string CandidateDirectory => Path.Combine(_directory, "candidate");
    internal string Commit { get; private set; } = "";

    internal async Task CaptureAsync(string root, string baseReference, CancellationToken cancellationToken)
    {
        Commit = (await Git(root, ["rev-parse", "--verify", "--end-of-options", baseReference + "^{commit}"],
            cancellationToken).ConfigureAwait(false)).Trim();
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(CandidateDirectory);
        var tree = await Git(root, ["ls-tree", "-rz", "--full-tree", Commit], cancellationToken).ConfigureAwait(false);
        if (tree.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(entry =>
                !entry.StartsWith("100644 ", StringComparison.Ordinal)
                && !entry.StartsWith("100755 ", StringComparison.Ordinal)))
        {
            throw new IOException(
                "Base snapshot contains symbolic links or submodules. Use a repository with regular files for change analysis.");
        }

        // A private index preserves export-ignored files and leaves the operator's index untouched.
        var index = Path.Combine(_directory, "index");
        await SnapshotProcess.RunAsync("git", root, ["read-tree", Commit], cancellationToken, index)
            .ConfigureAwait(false);
        await SnapshotProcess.RunAsync("git", root,
            ["--work-tree=" + BaseDirectory, "checkout-index", "--all", "--prefix=" + BaseDirectory + Path.DirectorySeparatorChar],
            cancellationToken, index).ConfigureAwait(false);
        var stages = await Git(root, ["ls-files", "--stage", "-z"], cancellationToken).ConfigureAwait(false);
        if (stages.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Any(entry => entry.StartsWith("160000 ", StringComparison.Ordinal)
                || !entry[..entry.IndexOf('\t')].EndsWith(" 0", StringComparison.Ordinal)))
        {
            throw new IOException(
                "Candidate snapshot contains a submodule or unresolved merge; change analysis requires regular, unambiguous repository files.");
        }

        var paths = await CandidatePaths(root, cancellationToken).ConfigureAwait(false);
        var hashes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Inside(root, path);
            RejectLinks(root, path);
            if (!File.Exists(source))
            {
                continue;
            } // Working-tree deletions override staged/HEAD contents.

            var destination = Inside(CandidateDirectory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var bytes = await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(destination, bytes, cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
            }

            hashes.Add(path, SHA256.HashData(bytes));
        }

        var capturedPaths = await CandidatePaths(root, cancellationToken).ConfigureAwait(false);
        if (!paths.SequenceEqual(capturedPaths))
        {
            throw new IOException(
                "Repository inventory changed while capturing the candidate. Stop concurrent edits and retry.");
        }

        foreach (var path in paths)
        {
            RejectLinks(root, path);
            var source = Inside(root, path);
            var currentHash = File.Exists(source)
                ? SHA256.HashData(await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false))
                : null;
            if (hashes.TryGetValue(path, out var hash)
                    ? currentHash is null || !hash.SequenceEqual(currentHash)
                    : currentHash is not null)
            {
                throw new IOException(
                    "Working-tree files changed while capturing the candidate. Stop concurrent edits and retry.");
            }
        }
    }

    internal static Task<string> Git(string root, string[] arguments, CancellationToken token) =>
        SnapshotProcess.RunAsync("git", root, arguments, token);

    private static async Task<string[]> CandidatePaths(string root, CancellationToken token) =>
        (await Git(root, ["ls-files", "-z", "--cached", "--others", "--exclude-standard"], token).ConfigureAwait(false))
        .Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal).ToArray();

    internal static string Inside(string root, string path)
    {
        var full = Path.GetFullPath(path, root);
        var relative = Path.GetRelativePath(root, full);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new IOException("Snapshot inputs must stay inside the Git repository (and Workspace:Root for MCP).");
        }

        return full;
    }

    private static void RejectLinks(string root, string path)
    {
        var current = root;
        foreach (var part in path.Split('/', '\\'))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Candidate snapshot cannot traverse symbolic links or reparse points.");
            }
        }
    }

    /// <summary>Removes only this operation's temporary snapshots and private index.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
