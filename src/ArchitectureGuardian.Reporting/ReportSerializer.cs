using System.Text.Json;
using System.Text.Json.Nodes;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Baselines;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Rules;

namespace ArchitectureGuardian.Reporting;

/// <summary>Maps neutral analysis contracts to deterministic JSON v1 and SARIF 2.1.0 documents.</summary>
public static class ReportSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, NewLine = "\n" };

    /// <summary>Serializes available findings without changing severities, identities or baseline matching.</summary>
    /// <param name="analysis">Coverage, diagnostics and operational issues from the analysis service.</param>
    /// <param name="format">The machine-readable encoding.</param>
    /// <param name="repositoryRoot">Policy directory used to normalize absolute source paths.</param>
    /// <param name="gatePassed">The host's evaluated gate, or null when coverage is not complete.</param>
    /// <param name="comparison">Optional classification of this exact analysis against a baseline or Git base.</param>
    /// <param name="baseCommit">Resolved Git base commit, when available.</param>
    /// <returns>A complete document with a final LF and no added timestamps or machine-local roots.</returns>
    /// <exception cref="ArgumentException">Gate/comparison context is inconsistent or a source path escapes the repository.</exception>
    public static string Serialize(AnalysisResult analysis, ReportFormat format, string repositoryRoot,
        bool? gatePassed, BaselineComparison? comparison = null, string? baseCommit = null)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (analysis.IsComplete != gatePassed.HasValue || comparison is not null && !ReferenceEquals(comparison.Analysis, analysis))
        {
            throw new ArgumentException("Supply an evaluated gate only for completed analysis and a comparison of the same result.");
        }

        var root = Path.GetFullPath(repositoryRoot);
        // Snapshot IDs omit semantic context; preserve the comparison's established instance membership.
        var existing = comparison is null ? null : new HashSet<Diagnostic>(comparison.Existing, ReferenceEqualityComparer.Instance);
        // Core's snapshot sort key predates semantic context. Break equal-key ties by full report
        // content so distinct contexts with a shared snapshot ID do not inherit enumeration order.
        var diagnostics = analysis.Diagnostics.GroupBy(item => (item.RuleId, item.Id, item.Severity))
            .SelectMany(group => group.Select(item => Diagnostic(item, root,
                    existing is null ? null : existing.Contains(item) ? "existing" : "new"))
                .OrderBy(item => item.ToJsonString(), StringComparer.Ordinal)).ToArray();
        var resolved = comparison?.Resolved.Select(Entry).ToArray() ?? [];
        var issues = analysis.Issues.Select(item => new JsonObject
        {
            ["code"] = item.Code,
            ["message"] = item.Message,
            ["ruleId"] = item.RuleId,
            ["location"] = Location(item.Location, root),
        }).ToArray();
        var rules = analysis.Diagnostics.Select(item => item.Violation.Rule).Concat(RuleCatalog.All)
            .DistinctBy(rule => rule.Id, StringComparer.Ordinal).OrderBy(rule => rule.Id, StringComparer.Ordinal).ToArray();
        var metadata = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["status"] = analysis.Status.ToString().ToLowerInvariant(),
            ["gateOutcome"] = gatePassed is null ? "notEvaluated" : gatePassed.Value ? "passed" : "failed",
            ["baselineApplied"] = comparison is not null,
            ["baseCommit"] = baseCommit,
        };
        JsonObject document;
        switch (format)
        {
            case ReportFormat.Json:
                document = metadata;
                document["rules"] = new JsonArray(rules.Select(rule => (JsonNode)new JsonObject
                {
                    ["id"] = rule.Id,
                    ["title"] = rule.Title,
                    ["documentationUrl"] = rule.DocumentationUrl.AbsoluteUri,
                }).ToArray());
                document["diagnostics"] = new JsonArray(diagnostics);
                document["resolved"] = new JsonArray(resolved);
                document["issues"] = new JsonArray(issues);
                break;
            case ReportFormat.Sarif:
                document = Sarif(analysis, rules, diagnostics, resolved, issues, metadata);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }

        return document.ToJsonString(Options) + "\n";
    }

    private static JsonObject Diagnostic(Diagnostic diagnostic, string root, string? state)
    {
        var entry = BaselineEntry.FromDiagnostic(diagnostic);
        var result = Entry(entry);
        result["id"] = diagnostic.Id;
        result["severity"] = diagnostic.Severity.ToString().ToLowerInvariant();
        result["message"] = diagnostic.Message;
        result["location"] = Location(diagnostic.Location, root);
        result["evidence"] = new JsonArray(diagnostic.Evidence.Select(item => (JsonNode)new JsonObject
        {
            ["description"] = item.Description,
            ["location"] = Location(item.Location, root),
        }).ToArray());
        result["publicContracts"] = new JsonArray((diagnostic.Violation.Context?.PublicContracts ?? [])
            .Select(value => (JsonNode)JsonValue.Create(value)!).ToArray());
        result["remediation"] = diagnostic.Remediation;
        result["documentationUrl"] = diagnostic.DocumentationUrl.AbsoluteUri;
        result["baselineState"] = state;
        return result;
    }

    private static JsonObject Entry(BaselineEntry entry) => new()
    {
        ["fingerprint"] = entry.Fingerprint,
        ["ruleId"] = entry.RuleId,
        ["target"] = new JsonObject { ["module"] = entry.Target.Module, ["symbol"] = entry.Target.Symbol },
        ["dependencyPath"] = new JsonArray(entry.DependencyPath.Select(item => (JsonNode)new JsonObject
        {
            ["kind"] = item.Kind,
            ["identity"] = item.Identity,
        }).ToArray()),
        ["constraints"] = new JsonArray(entry.Constraints.Select(item => (JsonNode)new JsonObject
        {
            ["scope"] = item.Scope,
            ["setting"] = item.Setting,
            ["values"] = new JsonArray(item.Values.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
        }).ToArray()),
        ["snapshotId"] = entry.SnapshotId,
    };

    private static JsonObject? Location(SourceLocation? location, string root)
    {
        if (location is null) { return null; }
        var path = location.Path.Replace('\\', '/');
        // Unix treats a Windows drive root as a relative filename; reject it before normalization.
        // Colons elsewhere remain valid Unix filename characters and are escaped in SARIF URIs.
        if (!OperatingSystem.IsWindows() && path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/')
        {
            throw new ArgumentException("Report source locations cannot use a foreign drive root.");
        }
        path = Path.GetRelativePath(root, Path.GetFullPath(path, root)).Replace('\\', '/');
        if (Path.IsPathRooted(path) || path == ".." || path.StartsWith("../", StringComparison.Ordinal))
        {
            throw new ArgumentException("Report source locations must be within the policy directory.");
        }

        return new()
        {
            ["path"] = path,
            ["startLine"] = location.StartLine,
            ["startColumn"] = location.StartColumn,
            ["endLine"] = location.EndLine,
            ["endColumn"] = location.EndColumn,
        };
    }

    private static JsonObject Sarif(AnalysisResult analysis, RuleDescriptor[] rules, JsonObject[] diagnostics,
        JsonObject[] resolved, JsonObject[] issues, JsonObject metadata)
    {
        var results = new JsonArray();
        foreach (var diagnostic in diagnostics)
        {
            var result = new JsonObject
            {
                ["ruleId"] = diagnostic["ruleId"]!.GetValue<string>(),
                ["level"] = diagnostic["severity"]!.GetValue<string>() == "info" ? "note" : diagnostic["severity"]!.GetValue<string>(),
                ["message"] = new JsonObject { ["text"] = diagnostic["message"]!.GetValue<string>() },
                ["partialFingerprints"] = new JsonObject { ["architectureGuardian/v1"] = diagnostic["fingerprint"]!.GetValue<string>() },
                ["properties"] = diagnostic,
            };
            if (diagnostic["baselineState"] is { } state)
            {
                result["baselineState"] = state.GetValue<string>() == "existing" ? "unchanged" : "new";
            }
            if (diagnostic["location"] is JsonObject location) { result["locations"] = new JsonArray(SarifLocation(location)); }
            var evidence = diagnostic["evidence"]!.AsArray().OfType<JsonObject>().Where(item => item["location"] is not null).ToArray();
            if (evidence.Length > 0)
            {
                result["relatedLocations"] = new JsonArray(evidence.Select((item, index) =>
                {
                    var related = SarifLocation(item["location"]!.AsObject());
                    related["id"] = index + 1;
                    related["message"] = new JsonObject { ["text"] = item["description"]!.GetValue<string>() };
                    return (JsonNode)related;
                }).ToArray());
            }
            results.Add(result);
        }

        foreach (var entry in resolved)
        {
            results.Add(new JsonObject
            {
                ["ruleId"] = entry["ruleId"]!.GetValue<string>(),
                ["level"] = "none",
                ["kind"] = "notApplicable",
                ["baselineState"] = "absent",
                ["message"] = new JsonObject { ["text"] = "Accepted finding is absent from completed analysis." },
                ["partialFingerprints"] = new JsonObject { ["architectureGuardian/v1"] = entry["fingerprint"]!.GetValue<string>() },
                ["properties"] = entry,
            });
        }
        metadata["issues"] = new JsonArray(issues);
        return new JsonObject
        {
            ["$schema"] = "https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json",
            ["version"] = "2.1.0",
            ["runs"] = new JsonArray(new JsonObject
            {
                ["tool"] = new JsonObject
                {
                    ["driver"] = new JsonObject
                    {
                        ["name"] = "Architecture Guardian",
                        ["semanticVersion"] = typeof(ReportSerializer).Assembly.GetName().Version!.ToString(3),
                        ["informationUri"] = "https://github.com/kbourzayq/arch-guard",
                        ["rules"] = new JsonArray(rules.Select(rule => (JsonNode)new JsonObject
                        {
                            ["id"] = rule.Id,
                            ["shortDescription"] = new JsonObject { ["text"] = rule.Title },
                            ["helpUri"] = rule.DocumentationUrl.AbsoluteUri,
                        }).ToArray()),
                    },
                },
                ["columnKind"] = "utf16CodeUnits",
                ["results"] = results,
                ["properties"] = metadata,
                ["invocations"] = new JsonArray(new JsonObject
                {
                    ["executionSuccessful"] = analysis.IsComplete,
                    ["toolExecutionNotifications"] = new JsonArray(issues.Select(issue => (JsonNode)new JsonObject
                    {
                        ["descriptor"] = new JsonObject { ["id"] = issue["code"]!.GetValue<string>() },
                        ["level"] = "error",
                        ["message"] = new JsonObject { ["text"] = issue["message"]!.GetValue<string>() },
                        ["locations"] = issue["location"] is JsonObject location ? new JsonArray(SarifLocation(location)) : new JsonArray(),
                    }).ToArray()),
                }),
            }),
        };
    }

    private static JsonObject SarifLocation(JsonObject location)
    {
        var region = new JsonObject
        {
            ["startLine"] = location["startLine"]!.GetValue<int>(),
            ["startColumn"] = location["startColumn"]!.GetValue<int>(),
        };
        // SARIF end coordinates describe a nonempty span; preserve point ranges in the versioned properties.
        if (location["endLine"]!.GetValue<int>() != location["startLine"]!.GetValue<int>()
            || location["endColumn"]!.GetValue<int>() != location["startColumn"]!.GetValue<int>())
        {
            region["endLine"] = location["endLine"]!.GetValue<int>();
            region["endColumn"] = location["endColumn"]!.GetValue<int>();
        }
        return new()
        {
            ["physicalLocation"] = new JsonObject
            {
                ["artifactLocation"] = new JsonObject
                {
                    ["uri"] = string.Join('/', location["path"]!.GetValue<string>().Split('/').Select(Uri.EscapeDataString)),
                    ["uriBaseId"] = "%SRCROOT%",
                },
                ["region"] = region,
            },
        };
    }
}
