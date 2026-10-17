using System.Globalization;
using ArchitectureGuardian.Core.Canonicalization;

namespace ArchitectureGuardian.Core.Diagnostics;

/// <summary>A neutral, one-based source range with an inclusive start and exclusive end.</summary>
/// <remarks>
/// Paths normalize backslashes to forward slashes without accessing the filesystem. Adapters should
/// supply repository-relative paths so the same source has the same identity across machines.
/// </remarks>
public sealed record SourceLocation
{
    /// <summary>Creates a validated range, or a point location when both end coordinates are omitted.</summary>
    /// <param name="path">The source path, preferably repository-relative.</param>
    /// <param name="startLine">The one-based line at which the range starts.</param>
    /// <param name="startColumn">The one-based column at which the range starts.</param>
    /// <param name="endLine">The one-based exclusive end line; must be supplied together with the end column.</param>
    /// <param name="endColumn">The one-based exclusive end column; must be supplied together with the end line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A start coordinate is less than one.</exception>
    /// <exception cref="ArgumentException">The path is blank, an end coordinate is missing, or the range is invalid.</exception>
    public SourceLocation(string path, int startLine, int startColumn, int? endLine = null, int? endColumn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startLine);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startColumn);
        if (endLine.HasValue != endColumn.HasValue)
        {
            throw new ArgumentException("Supply both end coordinates or neither.");
        }

        // A missing span is represented as a point rather than guessed from source that Core does not load.
        var lastLine = endLine ?? startLine;
        var lastColumn = endColumn ?? startColumn;
        if (lastLine < startLine || lastColumn < 1 || (lastLine == startLine && lastColumn < startColumn))
        {
            throw new ArgumentException("The source range must end at or after its start.");
        }

        Path = path.Replace('\\', '/');
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = lastLine;
        EndColumn = lastColumn;
    }

    /// <summary>Gets the supplied source path with forward-slash separators.</summary>
    public string Path { get; }

    /// <summary>Gets the one-based inclusive start line.</summary>
    public int StartLine { get; }

    /// <summary>Gets the one-based inclusive start column.</summary>
    public int StartColumn { get; }

    /// <summary>Gets the one-based exclusive end line, equal to the start line for a point location.</summary>
    public int EndLine { get; }

    /// <summary>Gets the one-based exclusive end column, equal to the start column for a point location.</summary>
    public int EndColumn { get; }

    internal string CanonicalKey => CanonicalData.Encode(Path,
        StartLine.ToString(CultureInfo.InvariantCulture), StartColumn.ToString(CultureInfo.InvariantCulture),
        EndLine.ToString(CultureInfo.InvariantCulture), EndColumn.ToString(CultureInfo.InvariantCulture));
}
