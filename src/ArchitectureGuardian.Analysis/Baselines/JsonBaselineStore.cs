using System.Text.Json;
using System.Text.Json.Serialization;
using ArchitectureGuardian.Core.Baselines;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Analysis.Baselines;

/// <summary>Reads strict, reviewable baseline JSON and atomically replaces files only on explicit update.</summary>
public sealed class JsonBaselineStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
    };

    /// <summary>Loads and validates schema, required fields and every semantic fingerprint.</summary>
    /// <param name="path">Explicit baseline file path; missing files never become an empty inventory.</param>
    /// <param name="cancellationToken">Cancellation forwarded to file reading.</param>
    /// <exception cref="InvalidDataException">The document is malformed, unsupported or inconsistent.</exception>
    /// <exception cref="IOException">The baseline cannot be read.</exception>
    public async Task<BaselineSnapshot> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = File.OpenRead(path);
        try
        {
            var document = await JsonSerializer.DeserializeAsync<BaselineDocument>(stream, Options, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Expected a baseline JSON object.");
            if (document.Version != BaselineSnapshot.SchemaVersion)
            {
                throw new InvalidDataException($"Unsupported baseline version '{document.Version}'; expected {BaselineSnapshot.SchemaVersion}.");
            }

            return new(document.Entries.Select(item =>
            {
                if (item is null || item.DependencyPath is null || item.Constraints is null
                    || item.DependencyPath.Any(node => node is null) || item.Constraints.Any(constraint => constraint is null))
                {
                    throw new InvalidDataException("Baseline entries and semantic context cannot contain null.");
                }

                var entry = new BaselineEntry(item.RuleId, new DiagnosticTarget(item.Module, item.Symbol), item.DependencyPath,
                    item.Constraints.Select(constraint => new PolicyConstraint(constraint.Scope, constraint.Setting, constraint.Values)), item.SnapshotId);
                if (!StringComparer.Ordinal.Equals(entry.Fingerprint, item.Fingerprint))
                {
                    throw new InvalidDataException("Baseline fingerprint does not match its rule, target and context. Run an explicit baseline update to regenerate it.");
                }

                return entry;
            }));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new InvalidDataException("Invalid baseline: " + exception.Message, exception);
        }
    }

    /// <summary>Writes a deterministic document through a sibling temporary file and atomic replacement.</summary>
    /// <param name="path">Explicit output path; its parent directory must already exist.</param>
    /// <param name="baseline">Accepted findings captured from completed analysis.</param>
    /// <param name="cancellationToken">Cancellation leaves the prior baseline intact before replacement.</param>
    /// <exception cref="IOException">The baseline cannot be written or replaced.</exception>
    public async Task SaveAsync(string path, BaselineSnapshot baseline, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(path);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var document = new BaselineDocument(BaselineSnapshot.SchemaVersion, baseline.Entries.Select(entry => new EntryDocument(
            entry.Fingerprint, entry.RuleId, entry.Target.Module, entry.Target.Symbol, entry.DependencyPath.ToArray(),
            entry.Constraints.Select(constraint => new ConstraintDocument(constraint.Scope, constraint.Setting, constraint.Values.ToArray())).ToArray(),
            entry.SnapshotId)).ToArray());
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, document, Options, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) { File.Delete(temporary); }
        }
    }

    /// <summary>Defines the persistence envelope independently of Core contracts.</summary>
    private sealed record BaselineDocument(int Version, EntryDocument[] Entries);
    /// <summary>Retains readable identity fields alongside their verified digest.</summary>
    private sealed record EntryDocument(string Fingerprint, string RuleId, string? Module, string? Symbol,
        ViolationPathNode[] DependencyPath, ConstraintDocument[] Constraints, string? SnapshotId);
    /// <summary>Maps policy context at the serialization boundary.</summary>
    private sealed record ConstraintDocument(string Scope, string Setting, string[] Values);
}
