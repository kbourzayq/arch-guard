namespace ArchitectureGuardian.Policy.Documents;

/// <summary>A one-based position in the policy document.</summary>
/// <param name="line">The one-based line.</param>
/// <param name="column">The one-based column.</param>
public sealed class PolicyPosition(int line, int column)
{
    /// <summary>Gets the line.</summary>
    public int Line { get; } = line;
    /// <summary>Gets the column.</summary>
    public int Column { get; } = column;
}
