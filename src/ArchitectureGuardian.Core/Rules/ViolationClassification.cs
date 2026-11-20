namespace ArchitectureGuardian.Core.Rules;

/// <summary>Distinguishes how much a static finding establishes, for rules that cannot prove everything from source.</summary>
public enum ViolationClassification
{
    /// <summary>Source evidence proves a violation of the rule's policy.</summary>
    Proven,
    /// <summary>Source evidence indicates a concern that the supported static analysis cannot confirm.</summary>
    Potential,
    /// <summary>Static analysis cannot decide; the application must be validated at runtime or in tests.</summary>
    RuntimeValidationRequired,
}
