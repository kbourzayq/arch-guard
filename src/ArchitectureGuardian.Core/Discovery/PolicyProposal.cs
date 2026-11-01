using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Discovery;

/// <summary>Review-only policy intentions, their evidence and questions that automation cannot settle.</summary>
public sealed class PolicyProposal
{
    internal PolicyProposal(ArchitecturePolicy? policy, IEnumerable<DiscoveryOwnership> ownership,
        IEnumerable<DiscoveryInference> inferences, IEnumerable<string> questions)
    {
        Policy = policy;
        Ownership = Array.AsReadOnly(ownership.OrderBy(item => item.ProjectPath, StringComparer.Ordinal).ToArray());
        Inferences = Array.AsReadOnly(inferences.OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Subject, StringComparer.Ordinal).ToArray());
        Questions = Array.AsReadOnly(questions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets a candidate policy only when every verified project has unambiguous candidate ownership.</summary>
    public ArchitecturePolicy? Policy { get; }
    /// <summary>Gets candidate ownership; missing projects remain explicit unresolved questions.</summary>
    public IReadOnlyList<DiscoveryOwnership> Ownership { get; }
    /// <summary>Gets evidence-backed suggestions, independently of observations.</summary>
    public IReadOnlyList<DiscoveryInference> Inferences { get; }
    /// <summary>Gets review questions, including any reason a safe proposal could not be generated.</summary>
    public IReadOnlyList<string> Questions { get; }
}
