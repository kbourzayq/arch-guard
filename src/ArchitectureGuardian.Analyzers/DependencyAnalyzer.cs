using System.Collections.Immutable;
using System.Xml;
using ArchitectureGuardian.Analyzers.Policies;
using ArchitectureGuardian.Core.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ArchitectureGuardian.Analyzers;

/// <summary>Checks semantic AG001/AG002 dependencies using only the host compilation and an explicit ownership snapshot.</summary>
/// <remarks>Generated code is included. Each compilation owns its immutable policy; callbacks support cancellation and concurrency.</remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DependencyAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Configuration = new("AG000", "Compiler policy configuration",
        "Compiler architecture analysis is unavailable: {0}", "Architecture", DiagnosticSeverity.Error, true,
        helpLinkUri: DependencySemantics.DocumentationRoot + "compiler-scope", customTags: WellKnownDiagnosticTags.CompilationEnd);
    private static readonly DiagnosticDescriptor Modules = new(DependencySemantics.ModuleId, DependencySemantics.ModuleTitle,
        "Module '{0}' must not depend on module '{1}': assembly '{2}' uses '{3}' from '{4}'", "Architecture", DiagnosticSeverity.Warning, true,
        helpLinkUri: DependencySemantics.DocumentationRoot + DependencySemantics.ModuleAnchor);
    private static readonly DiagnosticDescriptor Layers = new(DependencySemantics.LayerId, DependencySemantics.LayerTitle,
        "Layer '{0}' must not depend on layer '{1}': assembly '{2}' uses '{3}' from '{4}'", "Architecture", DiagnosticSeverity.Warning, true,
        helpLinkUri: DependencySemantics.DocumentationRoot + DependencySemantics.LayerAnchor);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Configuration, Modules, Layers);

    /// <summary>Registers compilation-local policy loading and concurrent semantic callbacks without opening a workspace.</summary>
    /// <param name="context">The compiler's analyzer registration context.</param>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(start =>
        {
            start.CancellationToken.ThrowIfCancellationRequested();
            CompilerPolicy policy;
            var source = start.Compilation.AssemblyName ?? "";
            try
            {
                var files = start.Options.AdditionalFiles.Where(file =>
                    Path.GetFileName(file.Path) == CompilerPolicy.FileName).ToArray();
                if (files.Length != 1)
                {
                    throw new ArgumentException("Supply exactly one " + CompilerPolicy.FileName + " as an AdditionalFile.");
                }

                policy = CompilerPolicy.Load(files[0], start.CancellationToken);
                if (!policy.Contains(source))
                {
                    throw new ArgumentException("Current assembly '" + source + "' has no ownership mapping.");
                }
            }
            catch (Exception exception) when (exception is ArgumentException or XmlException or System.IO.IOException or UnauthorizedAccessException)
            {
                var message = exception.Message;
                start.RegisterCompilationEndAction(end => end.ReportDiagnostic(Diagnostic.Create(Configuration, Location.None, message)));
                return;
            }

            start.RegisterSyntaxNodeAction(node => Analyze(node, policy, source), SyntaxKind.IdentifierName, SyntaxKind.GenericName);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, CompilerPolicy policy, string source)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        // Imports alone are not semantic uses. Alias/global/static imports are resolved at each use site.
        if (context.Node.Ancestors().Any(ancestor => ancestor is UsingDirectiveSyntax))
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }

        var assembly = symbol?.ContainingAssembly;
        if (symbol is INamespaceSymbol || assembly is null || SymbolEqualityComparer.Default.Equals(assembly, context.Compilation.Assembly))
        {
            return;
        }

        foreach (var violation in policy.Violations(source, assembly.Name))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var descriptor = violation.RuleId == DependencySemantics.ModuleId ? Modules : Layers;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, context.Node.GetLocation(), violation.Source, violation.Target,
                source, symbol!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), assembly.Name));
        }
    }
}
