namespace ArchitectureGuardian.Core.Diagnostics;

/// <summary>Identifies the logical module, symbol or both affected by a rule finding.</summary>
/// <remarks>Adapters should provide stable identifiers rather than workspace-specific objects or display names.</remarks>
public sealed record DiagnosticTarget
{
    /// <summary>Creates a target with at least one known module or symbol identifier.</summary>
    /// <param name="module">The affected logical module ID, or <see langword="null"/> if unknown.</param>
    /// <param name="symbol">The affected symbol ID, or <see langword="null"/> for a module-level finding.</param>
    /// <exception cref="ArgumentException">Both identifiers are null, or a supplied identifier is blank.</exception>
    public DiagnosticTarget(string? module = null, string? symbol = null)
    {
        if (module is null && symbol is null)
        {
            throw new ArgumentException("An affected module or symbol is required.");
        }

        if (module is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(module);
        }

        if (symbol is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        }

        Module = module;
        Symbol = symbol;
    }

    /// <summary>Gets the logical module ID, or <see langword="null"/> when only a symbol is known.</summary>
    public string? Module { get; }

    /// <summary>Gets the symbol ID, or <see langword="null"/> for a module-level finding.</summary>
    public string? Symbol { get; }
}
