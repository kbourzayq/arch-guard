namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Identifies the programming model that declares an HTTP endpoint.</summary>
public enum AspNetCoreEndpointKind
{
    /// <summary>An MVC controller type, which owns constructor, field and property dependencies shared by its actions.</summary>
    Controller,
    /// <summary>A public action method on an MVC controller.</summary>
    ControllerAction,
    /// <summary>A minimal API endpoint mapped with a Map* call.</summary>
    MinimalApi,
}
