using Microsoft.CodeAnalysis;

namespace FixtureGenerators;

/// <summary>Exercises generator diagnostics without relying on compiler errors in generated source.</summary>
[Generator]
public sealed class FixtureGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(context.AdditionalTextsProvider.Collect());
        context.RegisterSourceOutput(inputs, (production, input) =>
        {
            var ((compilation, options), files) = input;
            if (!options.GlobalOptions.TryGetValue("build_property.FixtureGeneratorMode", out var mode) ||
                files.Single().GetText(production.CancellationToken)?.ToString().Trim() != "fixture input" ||
                compilation.GetTypeByMetadataName("Generated.Contract") is not null)
            {
                throw new InvalidOperationException("Generator inputs do not match the project's original context.");
            }

            if (mode == "throw")
            {
                throw new InvalidOperationException("The fixture generator failed.");
            }

            production.AddSource("Contract.g.cs", "namespace Generated { public class Contract { } }");
            if (mode is "error" or "warning")
            {
                var descriptor = new DiagnosticDescriptor("FIXTURE001", "Fixture generation problem",
                    "The fixture generator reported a problem.", "Fixture",
                    mode == "error" ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, isEnabledByDefault: true);
                production.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
            }
        });
    }
}
