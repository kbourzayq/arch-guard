namespace ArchitectureGuardian.Reporting;

/// <summary>Identifies the versioned machine-readable report encodings.</summary>
public enum ReportFormat
{
    /// <summary>Architecture Guardian's version-one JSON contract.</summary>
    Json,
    /// <summary>OASIS SARIF version 2.1.0 with Architecture Guardian properties version one.</summary>
    Sarif,
}
