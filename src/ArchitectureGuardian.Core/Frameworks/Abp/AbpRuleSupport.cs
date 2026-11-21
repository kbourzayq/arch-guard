namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Publishes what an ABP Framework rule can establish statically, its confidence and what it cannot see.</summary>
/// <param name="RuleId">The ABP rule ID this support statement describes.</param>
/// <param name="Confidence">How findings are classified, such as proven or potential, and why.</param>
/// <param name="SupportedCases">Static source shapes the rule evaluates.</param>
/// <param name="RuntimeBlindSpots">Runtime behavior the rule never infers; applications must validate these separately.</param>
public sealed record AbpRuleSupport(string RuleId, string Confidence, IReadOnlyList<string> SupportedCases, IReadOnlyList<string> RuntimeBlindSpots);
