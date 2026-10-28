using System.Collections.Immutable;
using System.Xml;
using ArchitectureGuardian.Analyzers.Policies;
using ArchitectureGuardian.Core.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using ArchitectureGuardian.Policy;

namespace ArchitectureGuardian.Analyzers;

/// <summary>Checks semantic AG001/AG002 dependencies, AG004 implementation access and AG005/AG006 reference restrictions in the host compilation.</summary>
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

    private static readonly DiagnosticDescriptor Implementations = new(DependencySemantics.ImplementationId, DependencySemantics.ImplementationTitle,
        "Module '{0}' must not access implementation of module '{1}': assembly '{2}' uses '{3}' from '{4}'", "Architecture", DiagnosticSeverity.Warning, true,
        helpLinkUri: DependencySemantics.DocumentationRoot + DependencySemantics.ImplementationAnchor);

    private static readonly DiagnosticDescriptor ForbiddenReferences = new(ForbiddenReferenceSemantics.Id, ForbiddenReferenceSemantics.Title,
        "Assembly '{0}' uses '{1}' from '{2}', violating {3}.{4} restriction '{5}'", "Architecture", DiagnosticSeverity.Warning, true,
        helpLinkUri: DependencySemantics.DocumentationRoot + ForbiddenReferenceSemantics.Anchor);

    private static readonly DiagnosticDescriptor DomainInfrastructure = new(DomainInfrastructureSemantics.Id, DomainInfrastructureSemantics.Title,
        "Assembly '{0}' uses infrastructure symbol '{1}' from '{2}', violating {3}.{4} restriction '{5}'", "Architecture", DiagnosticSeverity.Warning, true,
        helpLinkUri: DependencySemantics.DocumentationRoot + DomainInfrastructureSemantics.Anchor);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Configuration, Modules, Layers, Implementations, ForbiddenReferences, DomainInfrastructure);

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
                    Path.GetFileName(file.Path) is CompilerPolicy.FileName or CompilerPolicy.YamlFileName).ToArray();
                if (files.Length != 1)
                {
                    throw new ArgumentException("Supply exactly one architecture.guardian.yaml (or a legacy architecture.guardian.compiler.xml) as an AdditionalFile; do not combine formats.");
                }

                policy = Path.GetFileName(files[0].Path) == CompilerPolicy.YamlFileName
                    ? CompilerPolicy.LoadYaml(files[0], start.Options, start.Compilation, start.CancellationToken)
                    : CompilerPolicy.Load(files[0], start.CancellationToken);
                if (!policy.Contains(source))
                {
                    throw new ArgumentException("Current assembly '" + source + "' has no ownership mapping.");
                }
            }
            catch (PolicyReadException exception)
            {
                var file = start.Options.AdditionalFiles.Single(item => Path.GetFileName(item.Path) == CompilerPolicy.YamlFileName);
                var text = file.GetText(start.CancellationToken)!;
                var line = Math.Min(exception.Position.Line - 1, text.Lines.Count - 1);
                var column = Math.Min(exception.Position.Column - 1, text.Lines[line].Span.Length);
                var position = new LinePosition(line, column);
                var location = Location.Create(file.Path, new TextSpan(text.Lines[line].Start + column, 0), new LinePositionSpan(position, position));
                start.RegisterCompilationEndAction(end => end.ReportDiagnostic(Diagnostic.Create(Configuration, location, exception.Message)));
                return;
            }
            catch (Exception exception) when (exception is ArgumentException or XmlException or System.IO.IOException or UnauthorizedAccessException)
            {
                var message = exception.Message;
                start.RegisterCompilationEndAction(end => end.ReportDiagnostic(Diagnostic.Create(Configuration, Location.None, message)));
                return;
            }

            start.RegisterSyntaxNodeAction(node => Analyze(node, policy, source), SyntaxKind.IdentifierName, SyntaxKind.GenericName, SyntaxKind.PredefinedType);
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

        var alias = context.SemanticModel.GetAliasInfo(context.Node, context.CancellationToken);
        var symbol = context.Node is PredefinedTypeSyntax
            ? context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).Type
            : alias?.Target ?? context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        if (symbol is null)
        {
            return;
        }

        // Alias declarations are skipped as imports, so inspect their full type at each use.
        // Ordinary generic names have separate callbacks for their authored type arguments.
        var symbols = alias is null ? new[] { symbol } : AliasSymbols(alias.Target, context.CancellationToken);
        foreach (var targetSymbol in symbols)
        {
            var assembly = targetSymbol?.ContainingAssembly;
            if (targetSymbol is INamespaceSymbol || assembly is null)
            {
                continue;
            }

            if (targetSymbol is INamedTypeSymbol or IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)
            {
                var ns = targetSymbol.ContainingNamespace;
                var namespaceName = NamespaceName(ns);
                foreach (var restriction in policy.Restrictions(source, assembly.Name, namespaceName))
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    Report(context, policy, ForbiddenReferences, source,
                        targetSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), assembly.Name,
                        restriction.Scope, restriction.Kind, restriction.Name);
                }

                foreach (var restriction in policy.InfrastructureRestrictions(source, assembly.Name, namespaceName))
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    Report(context, policy, DomainInfrastructure, source,
                        targetSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), assembly.Name,
                        restriction.Scope, restriction.Kind, restriction.Name);
                }
            }

            if (SymbolEqualityComparer.Default.Equals(assembly, context.Compilation.Assembly))
            {
                continue;
            }

            foreach (var violation in policy.Violations(source, assembly.Name))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var descriptor = violation.RuleId switch
                {
                    DependencySemantics.ModuleId => Modules,
                    DependencySemantics.LayerId => Layers,
                    _ => Implementations,
                };
                Report(context, policy, descriptor, violation.Source, violation.Target,
                    source, targetSymbol!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), assembly.Name);
            }
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, CompilerPolicy policy, DiagnosticDescriptor descriptor, params object[] arguments)
    {
        if (!policy.IsSelected(descriptor.Id)) { return; }
        var severity = policy.Severity(descriptor.Id);
        var provider = context.Compilation.Options.SyntaxTreeOptionsProvider;
        if (provider is not null && (provider.TryGetDiagnosticValue(context.Node.SyntaxTree, descriptor.Id, context.CancellationToken, out var configured)
            || provider.TryGetGlobalDiagnosticValue(descriptor.Id, context.CancellationToken, out configured)))
        {
            if (configured == ReportDiagnostic.Suppress) { return; }
            severity = configured switch
            {
                ReportDiagnostic.Error => DiagnosticSeverity.Error,
                ReportDiagnostic.Warn => DiagnosticSeverity.Warning,
                ReportDiagnostic.Info => DiagnosticSeverity.Info,
                ReportDiagnostic.Hidden => DiagnosticSeverity.Hidden,
                _ => severity,
            };
        }
        context.ReportDiagnostic(Diagnostic.Create(descriptor, context.Node.GetLocation(), severity, null, null, arguments));
    }

    private static string NamespaceName(INamespaceSymbol? symbol)
    {
        var names = new Stack<string>();
        for (var current = symbol; current is not null && !current.IsGlobalNamespace; current = current.ContainingNamespace)
        {
            names.Push(current.Name);
        }

        return string.Join(".", names);
    }

    private static IEnumerable<ISymbol> AliasSymbols(ISymbol target, CancellationToken cancellationToken)
    {
        var pending = new Stack<ISymbol>();
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        pending.Push(target);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = pending.Pop();
            if (!visited.Add(symbol))
            {
                continue;
            }

            switch (symbol)
            {
                case IArrayTypeSymbol array:
                    pending.Push(array.ElementType);
                    break;
                case IPointerTypeSymbol pointer:
                    pending.Push(pointer.PointedAtType);
                    break;
                case INamedTypeSymbol named:
                    yield return named;
                    foreach (var argument in named.TypeArguments)
                    {
                        pending.Push(argument);
                    }

                    if (named.ContainingType is not null)
                    {
                        pending.Push(named.ContainingType);
                    }

                    break;
            }
        }
    }
}
