using Microsoft.CodeAnalysis;

namespace FixtureGenerators;

/// <summary>Reuses another generator's hint and file-local type names to exercise generator-scoped identities.</summary>
[Generator]
public sealed class OtherFixtureGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context) => context.RegisterPostInitializationOutput(
        output => output.AddSource("Contract.g.cs", "namespace Generated { file class LocalHelper { public System.Text.StringBuilder Value => new(); } }"));
}
