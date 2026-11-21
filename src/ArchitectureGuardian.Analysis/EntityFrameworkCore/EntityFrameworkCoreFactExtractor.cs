using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Frameworks.EntityFrameworkCore;
using ArchitectureGuardian.Core.Repositories;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceLocation = ArchitectureGuardian.Core.Diagnostics.SourceLocation;

namespace ArchitectureGuardian.Analysis.EntityFrameworkCore;

/// <summary>Converts the statically visible EF Core usage of a solution into neutral facts.</summary>
/// <remarks>
/// Matching uses bound symbols and metadata names, so this assembly needs no EF Core reference. EF Core identities
/// must come from referenced assemblies: a source type in an EF Core namespace is not treated as the framework.
/// Projects without EF Core references contribute no facts. Runtime composition and reflection are never inferred.
/// </remarks>
public static class EntityFrameworkCoreFactExtractor
{
    private const string EntityFrameworkCoreNamespace = "Microsoft.EntityFrameworkCore";
    private const string DbContextName = EntityFrameworkCoreNamespace + ".DbContext";
    private static readonly SymbolDisplayFormat Display = SymbolDisplayFormat.CSharpErrorMessageFormat;

    /// <summary>Registration methods whose type and option arguments are composition rather than data access.</summary>
    private static readonly string[] RegistrationMethods = ["AddDbContext", "AddDbContextFactory", "AddDbContextPool", "AddPooledDbContextFactory"];

    /// <summary>Extracts EF Core type uses and method references from every available project.</summary>
    /// <param name="solution">The loaded solution; it must remain undisposed until extraction completes.</param>
    /// <param name="projects">Normalized projects that supply policy-relative identities.</param>
    /// <param name="root">The directory defining portable source and project identities.</param>
    /// <param name="cancellationToken">Cooperative cancellation while compiling and walking syntax.</param>
    /// <returns>The facts plus issues for projects whose semantic context was unavailable.</returns>
    /// <exception cref="OperationCanceledException">Cancellation interrupts extraction.</exception>
    public static async Task<(EntityFrameworkCoreFacts Facts, IReadOnlyList<AnalysisIssue> Issues)> ExtractAsync(Solution solution,
        IReadOnlyList<RepositoryProject> projects, string root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        var issues = new List<AnalysisIssue>();
        var contexts = new List<(string Project, Compilation Compilation, HashSet<SyntaxTree> Generated)>();
        var owners = new Dictionary<AssemblyIdentity, string?>();
        foreach (var project in solution.Projects.OrderBy(item => item.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = projects.SingleOrDefault(item => project.FilePath is not null
                && item.Id == RepositoryModelBuilder.RelativePath(fullRoot, project.FilePath));
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (model is null || compilation is null)
            {
                issues.Add(new("efcore-project-unavailable", $"Project '{project.Name}' has no semantic context for EF Core analysis."));
                continue;
            }

            var generated = await RepositoryModelBuilder.GeneratedFilesAsync(project, cancellationToken).ConfigureAwait(false);
            contexts.Add((model.Id, compilation, generated.Keys.ToHashSet()));
            var identity = compilation.Assembly.Identity;
            if (!owners.TryAdd(identity, model.Id) && owners[identity] != model.Id)
            {
                // EF001 reports this only for contexts it meets; EF002 and EF003 do not need an owner.
                owners[identity] = null;
            }
        }

        var uses = new List<EntityFrameworkCoreTypeUse>();
        var calls = new List<EntityFrameworkCoreApiCall>();
        foreach (var context in contexts.OrderBy(item => item.Project, StringComparer.Ordinal))
        {
            var scan = new Scan(context.Project, fullRoot, owners, cancellationToken);
            foreach (var tree in context.Compilation.SyntaxTrees.OrderBy(item => item.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Generated source is not owned by the repository's authors and has no stable source identity.
                if (context.Generated.Contains(tree))
                {
                    continue;
                }

                var syntax = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                scan.Visit(context.Compilation.GetSemanticModel(tree), syntax, uses, calls);
            }
        }

        return (new EntityFrameworkCoreFacts(uses, calls), issues);
    }

    /// <summary>Determines whether a symbol comes from a referenced assembly rather than the analyzed source.</summary>
    private static bool IsReferenced(ISymbol symbol) => symbol.Locations.Length > 0 && symbol.Locations.All(item => item.IsInMetadata);

    private static bool IsEntityFrameworkCoreSymbol(ISymbol symbol) => IsReferenced(symbol)
        && symbol.ContainingNamespace is { IsGlobalNamespace: false } ns && ns.ToDisplayString() is var name
        && (name == EntityFrameworkCoreNamespace || name.StartsWith(EntityFrameworkCoreNamespace + ".", StringComparison.Ordinal));

    /// <summary>Determines whether a method is an EF Core API, including extensions EF Core declares in other namespaces.</summary>
    /// <remarks>Registration methods such as AddDbContext live in Microsoft.Extensions.DependencyInjection but ship in EF Core assemblies.</remarks>
    private static bool IsEntityFrameworkCoreMethod(IMethodSymbol method) => IsEntityFrameworkCoreSymbol(method.ContainingType)
        || IsReferenced(method) && method.ContainingAssembly is { Name: var assembly }
        && (assembly == EntityFrameworkCoreNamespace || assembly.StartsWith(EntityFrameworkCoreNamespace + ".", StringComparison.Ordinal));

    private static bool IsFrameworkDbContext(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "DbContext", Arity: 0 } && IsReferenced(type) && type.ToDisplayString() == DbContextName;

    private static bool DerivesFromDbContext(ITypeSymbol? type)
    {
        for (var current = type?.BaseType; current is not null; current = current.BaseType)
        {
            if (IsFrameworkDbContext(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Walks one project's syntax trees, keeping per-type classification cached for the project.</summary>
    private sealed class Scan(string project, string root, IReadOnlyDictionary<AssemblyIdentity, string?> owners, CancellationToken cancellationToken)
    {
        private readonly Dictionary<ITypeSymbol, bool> _entityFrameworkCore = new(SymbolEqualityComparer.Default);

        internal void Visit(SemanticModel model, SyntaxNode syntax, List<EntityFrameworkCoreTypeUse> uses, List<EntityFrameworkCoreApiCall> calls)
        {
            var (typeArguments, arguments) = CompositionSpans(model, syntax);
            // Indexers bind on element access rather than a name, so store[0] must be inspected separately.
            foreach (var name in syntax.DescendantNodes().Where(node => node is SimpleNameSyntax or ElementAccessExpressionSyntax))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbol = model.GetSymbolInfo(name, cancellationToken).Symbol;
                if (symbol is null || User(model, name) is not { } user)
                {
                    continue;
                }

                if (symbol is IMethodSymbol method && (method.ReducedFrom ?? method).OriginalDefinition is var definition
                    && definition.MethodKind is not MethodKind.Constructor && IsEntityFrameworkCoreMethod(definition))
                {
                    calls.Add(new(project, user.ToDisplayString(Display), definition.Name,
                        definition.ContainingType.ToDisplayString(Display) + "." + definition.Name, Location(name.GetLocation())));
                }

                // Registered context types are composition. Inside registration callbacks only option configuration is
                // exempt; resolving or using a context there is still data access by the registering project.
                if (typeArguments.Any(span => span.Contains(name.Span)))
                {
                    continue;
                }

                var configuring = arguments.Any(span => span.Contains(name.Span));
                // A member access can name the same type twice, such as a property's containing and declared type.
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var type in Types(symbol).SelectMany(item => WithTypeArguments(item, depth: 0)))
                {
                    if (Use(type, user, name) is { } use && !(configuring && use.Kind == EntityFrameworkCoreTypeKind.Persistence) && seen.Add(use.Symbol))
                    {
                        uses.Add(use);
                    }
                }
            }
        }

        private static IEnumerable<ITypeSymbol?> Types(ISymbol symbol) => symbol switch
        {
            INamedTypeSymbol type => [type],
            // The bound return type proves which context a factory or accessor supplies.
            IMethodSymbol method => [Declaring(method), method.ReturnType],
            IPropertySymbol property => [Declaring(property), property.Type],
            IFieldSymbol field => [Declaring(field), field.Type],
            ILocalSymbol local => [local.Type],
            IParameterSymbol parameter => [parameter.Type],
            _ => [],
        };

        /// <summary>Gets a member's declaring type, except the DbContext base type that declares inherited members.</summary>
        /// <remarks>
        /// SaveChanges on a concrete context is declared by DbContext; the receiver's own type is captured separately,
        /// so the declaring base must not suggest that the context is decided at runtime.
        /// </remarks>
        private static ITypeSymbol? Declaring(ISymbol member) => member.ContainingType is { } type && IsFrameworkDbContext(type) ? null : member.ContainingType;

        /// <summary>Expands bound generic arguments, so aliased types such as IDbContextFactory&lt;TContext&gt; expose TContext.</summary>
        private static IEnumerable<ITypeSymbol?> WithTypeArguments(ITypeSymbol? type, int depth)
        {
            yield return type;
            while (type is IArrayTypeSymbol array)
            {
                type = array.ElementType;
            }

            if (type is INamedTypeSymbol { IsGenericType: true } generic && depth < 8)
            {
                foreach (var argument in generic.TypeArguments.SelectMany(item => WithTypeArguments(item, depth + 1)))
                {
                    yield return argument;
                }
            }
        }

        private EntityFrameworkCoreTypeUse? Use(ITypeSymbol? type, INamedTypeSymbol user, SyntaxNode name)
        {
            while (type is IArrayTypeSymbol array)
            {
                type = array.ElementType;
            }

            if (type is not INamedTypeSymbol named || named.SpecialType != SpecialType.None || !IsEntityFrameworkCore(named.OriginalDefinition))
            {
                return null;
            }

            var definition = named.OriginalDefinition;
            string? declaring = null;
            var ambiguous = false;
            if (definition.ContainingAssembly?.Identity is { } identity && owners.TryGetValue(identity, out declaring))
            {
                // A null owner marks an assembly identity that several source projects produce.
                ambiguous = declaring is null;
            }

            var kind = IsFrameworkDbContext(definition)
                // A DbContext subclass naming its own base type is declaring a context, not choosing one at runtime.
                ? DerivesFromDbContext(user) ? EntityFrameworkCoreTypeKind.Persistence : EntityFrameworkCoreTypeKind.BaseDbContext
                : DerivesFromDbContext(definition) ? EntityFrameworkCoreTypeKind.DbContext : EntityFrameworkCoreTypeKind.Persistence;
            return new(project, user.ToDisplayString(Display), definition.ToDisplayString(Display), kind, declaring, Location(name.GetLocation()),
                ambiguous && kind == EntityFrameworkCoreTypeKind.DbContext);
        }

        /// <summary>Determines whether a type, a base type or an implemented interface is an EF Core framework type.</summary>
        private bool IsEntityFrameworkCore(INamedTypeSymbol type)
        {
            if (_entityFrameworkCore.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var result = false;
            for (ITypeSymbol? current = type; current is not null && !result; current = current.BaseType)
            {
                result = IsEntityFrameworkCoreSymbol(current);
            }

            result = result || type.AllInterfaces.Any(IsEntityFrameworkCoreSymbol);
            _entityFrameworkCore.Add(type, result);
            return result;
        }

        /// <summary>Finds the innermost type whose source contains a name; top-level statements belong to their synthesized type.</summary>
        private INamedTypeSymbol? User(SemanticModel model, SyntaxNode name)
        {
            if (name.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is { } declaration)
            {
                return model.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
            }

            for (var symbol = model.GetEnclosingSymbol(name.SpanStart, cancellationToken); symbol is not null; symbol = symbol.ContainingSymbol)
            {
                if (symbol is INamedTypeSymbol type)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>Collects the type-argument and argument spans of EF Core service registrations in one tree.</summary>
        private (List<TextSpan> TypeArguments, List<TextSpan> Arguments) CompositionSpans(SemanticModel model, SyntaxNode syntax)
        {
            var typeArguments = new List<TextSpan>();
            var arguments = new List<TextSpan>();
            foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name,
                    MemberBindingExpressionSyntax binding => binding.Name,
                    SimpleNameSyntax simple => simple,
                    _ => null,
                };
                // The name check is a cheap syntactic filter; the bound method must still come from EF Core's assembly.
                if (target is null || !RegistrationMethods.Contains(target.Identifier.ValueText, StringComparer.Ordinal)
                    || model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method || !IsReferenced(method)
                    || method.ContainingAssembly?.Name.StartsWith(EntityFrameworkCoreNamespace, StringComparison.Ordinal) != true)
                {
                    continue;
                }

                arguments.Add(invocation.ArgumentList.Span);
                if (target is GenericNameSyntax generic)
                {
                    typeArguments.Add(generic.TypeArgumentList.Span);
                }
            }

            return (typeArguments, arguments);
        }

        private SourceLocation Location(Microsoft.CodeAnalysis.Location location)
        {
            var span = location.GetLineSpan();
            return new(Path.GetRelativePath(root, span.Path).Replace('\\', '/'), span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1, span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
        }
    }
}
