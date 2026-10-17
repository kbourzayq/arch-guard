using ArchitectureGuardian.Core.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Collects source-generator errors and failures that compilation diagnostics do not include.</summary>
internal static class WorkspaceGeneratorValidator
{
    /// <summary>Validates generators against original project inputs using Roslyn's public driver API.</summary>
    internal static async Task ValidateAsync(Project project, Compilation compilation, List<AnalysisIssue> issues,
        CancellationToken cancellationToken)
    {
        var generators = project.AnalyzerReferences.SelectMany(reference => reference.GetGenerators(project.Language)).ToArray();
        if (generators.Length == 0)
        {
            return;
        }

        // Roslyn 5.0 exposes workspace generator diagnostics only internally. Run a separate diagnostic pass,
        // removing prior generated trees so generators see their original compilation rather than their own output.
        var generatedTrees = new List<SyntaxTree>();
        foreach (var document in await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false))
        {
            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            if (tree is not null)
            {
                generatedTrees.Add(tree);
            }
        }

        var input = compilation.RemoveSyntaxTrees(generatedTrees);
        var driver = CSharpGeneratorDriver.Create(generators,
            additionalTexts: project.AnalyzerOptions.AdditionalFiles,
            parseOptions: (CSharpParseOptions?)project.ParseOptions,
            optionsProvider: project.AnalyzerOptions.AnalyzerConfigOptionsProvider);
        var result = driver.RunGenerators(input, cancellationToken).GetRunResult();
        foreach (var diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            issues.Add(new("source-generator-error", $"{project.FilePath}: {diagnostic}"));
        }

        foreach (var generator in result.Results.Where(generator => generator.Exception is not null))
        {
            // A crashed generator can be reported as a compiler warning while leaving required source unavailable.
            issues.Add(new("source-generator-failed", $"{project.FilePath}: {generator.Generator.GetType().FullName}: {generator.Exception!.GetType().Name}: {generator.Exception.Message}"));
        }
    }
}
