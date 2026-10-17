namespace ArchitectureGuardian.ArchitectureTests.Infrastructure;

/// <summary>Captures process completion separately from standard output and error for test assertions.</summary>
/// <param name="ExitCode">The process exit status.</param>
/// <param name="Output">The complete standard output stream.</param>
/// <param name="Error">The complete standard error stream.</param>
internal sealed record CommandResult(int ExitCode, string Output, string Error);
