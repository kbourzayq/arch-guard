using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ArchitectureGuardian.Core.Canonicalization;

/// <summary>Builds culture-independent representations and immutable orderings for diagnostic contracts.</summary>
internal static class CanonicalData
{
    /// <summary>Encodes a sequence without ambiguity between nulls, empty strings, delimiters or nested values.</summary>
    /// <param name="values">Values in their semantic field order; individual entries may be null.</param>
    /// <returns>A concatenation of invariant length-prefixed values, with a distinct marker for null.</returns>
    public static string Encode(params string?[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            // String lengths count UTF-16 code units; the complete representation is later hashed as UTF-8.
            builder.Append(value is null ? "-1:" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value);
        }

        return builder.ToString();
    }

    /// <summary>Produces a reproducible SHA-256 identity without process-dependent hash seeds.</summary>
    /// <param name="value">The canonical representation to hash as UTF-8.</param>
    /// <returns>The 64-character lowercase hexadecimal digest.</returns>
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Copies and canonically orders a collection so later caller mutations cannot change a result.</summary>
    /// <typeparam name="T">The immutable reference type stored in the snapshot.</typeparam>
    /// <param name="values">The entries to copy; duplicate entries are retained.</param>
    /// <param name="key">A stable canonical key compared using ordinal string ordering.</param>
    /// <returns>A read-only, ordered snapshot of the supplied entries.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentException">An entry in <paramref name="values"/> is null.</exception>
    public static IReadOnlyList<T> Snapshot<T>(IEnumerable<T> values, Func<T, string> key) where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Any(value => value is null))
        {
            throw new ArgumentException("Collection entries cannot be null.", nameof(values));
        }

        return Array.AsReadOnly(copy.OrderBy(key, StringComparer.Ordinal).ToArray());
    }
}
