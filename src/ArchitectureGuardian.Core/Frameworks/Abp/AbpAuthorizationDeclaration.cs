namespace ArchitectureGuardian.Core.Frameworks.Abp;

/// <summary>Classifies how an exposed application service method declares its authorization in source.</summary>
public enum AbpAuthorizationDeclaration
{
    /// <summary>No attribute, permission check or CRUD policy name was found.</summary>
    None,
    /// <summary>An [Authorize] attribute on the method, its class or a base class.</summary>
    Authorize,
    /// <summary>An explicit [AllowAnonymous] attribute.</summary>
    AllowAnonymous,
    /// <summary>An imperative permission check, such as AuthorizationService.CheckAsync, in the method body.</summary>
    PermissionCheck,
    /// <summary>A CRUD application service policy name, such as GetListPolicyName, assigned in source.</summary>
    PolicyName,
}
