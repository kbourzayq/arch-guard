namespace ArchitectureGuardian.Core.Rules;

/// <summary>A rule-observed endpoint in an ordered explanation path.</summary>
/// <param name="Kind">The endpoint category: project, module or symbol.</param>
/// <param name="Identity">The policy-relative project path, module ID or bound symbol.</param>
public sealed record ViolationPathNode(string Kind, string Identity);
