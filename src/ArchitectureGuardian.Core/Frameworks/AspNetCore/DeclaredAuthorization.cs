namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Describes the authorization that source code statically declares for an endpoint.</summary>
/// <remarks>Fallback policies, middleware and policy contents are runtime concerns and are never inferred.</remarks>
public enum DeclaredAuthorization
{
    /// <summary>Authorization is required by an attribute or endpoint convention.</summary>
    Required,
    /// <summary>Anonymous access is explicitly declared.</summary>
    AllowAnonymous,
    /// <summary>No authorization or anonymous declaration was found in the supported static locations.</summary>
    Undeclared,
    /// <summary>A convention source, such as a route group created elsewhere, could not be resolved statically.</summary>
    Indeterminate,
}
