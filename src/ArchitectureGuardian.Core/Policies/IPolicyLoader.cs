namespace ArchitectureGuardian.Core.Policies;

/// <summary>Loads external architecture policy into validated neutral contracts.</summary>
public interface IPolicyLoader
{
    /// <summary>Loads and validates a policy, preserving read, validation and cancellation failures for the host.</summary>
    /// <param name="path">The policy file location supplied by the host.</param>
    /// <param name="cancellationToken">A cooperative cancellation token.</param>
    /// <returns>The validated policy; project-inventory resolution is a separate step.</returns>
    Task<ArchitecturePolicy> LoadAsync(string path, CancellationToken cancellationToken = default);
}
