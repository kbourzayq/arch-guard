using System.Text;

namespace ArchitectureGuardian.Reporting;

/// <summary>Writes complete UTF-8 reports through a sibling temporary file and atomic replacement.</summary>
public static class ReportWriter
{
    /// <summary>Replaces the destination only after the entire report has been written.</summary>
    /// <param name="path">Destination whose parent directory must exist.</param>
    /// <param name="report">A serialized report, including its final newline.</param>
    /// <param name="cancellationToken">Cancellation before replacement preserves the previous file.</param>
    /// <exception cref="IOException">Writing or replacement failed.</exception>
    /// <exception cref="UnauthorizedAccessException">The destination is not writable.</exception>
    public static async Task WriteAsync(string path, string report, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, report, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) { File.Delete(temporary); }
        }
    }
}
