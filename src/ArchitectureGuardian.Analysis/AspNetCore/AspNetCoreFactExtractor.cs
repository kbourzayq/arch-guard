using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Frameworks.AspNetCore;
using ArchitectureGuardian.Core.Repositories;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SourceLocation = ArchitectureGuardian.Core.Diagnostics.SourceLocation;

namespace ArchitectureGuardian.Analysis.AspNetCore;

/// <summary>Converts the statically visible ASP.NET Core structure of a solution into neutral facts.</summary>
/// <remarks>
/// Matching uses bound symbols and metadata names, so the solution needs no ASP.NET Core package reference
/// in this assembly. Projects without ASP.NET Core references contribute no endpoints. Runtime composition,
/// reflection and assembly scanning are never inferred.
/// </remarks>
public static class AspNetCoreFactExtractor
{
    private static readonly SymbolDisplayFormat Display = SymbolDisplayFormat.CSharpErrorMessageFormat;
    private static readonly string[] MinimalMethods = ["Map", "MapDelete", "MapFallback", "MapGet", "MapMethods", "MapPatch", "MapPost", "MapPut"];
    private static readonly string[] RegistrationMethods =
    [
        "AddDbContext", "AddDbContextPool", "AddScoped", "AddSingleton", "AddTransient", "TryAddScoped", "TryAddSingleton", "TryAddTransient",
    ];

    /// <summary>Extracts endpoints and service registrations from every available project.</summary>
    /// <param name="solution">The loaded solution; it must remain undisposed until extraction completes.</param>
    /// <param name="projects">Normalized projects that supply policy-relative identities.</param>
    /// <param name="root">The directory defining portable source and project identities.</param>
    /// <param name="cancellationToken">Cooperative cancellation while compiling and walking syntax.</param>
    /// <returns>The facts plus issues for projects whose semantic context was unavailable.</returns>
    /// <exception cref="OperationCanceledException">Cancellation interrupts extraction.</exception>
    public static async Task<(AspNetCoreFacts Facts, IReadOnlyList<AnalysisIssue> Issues)> ExtractAsync(Solution solution,
        IReadOnlyList<RepositoryProject> projects, string root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        var issues = new List<AnalysisIssue>();
        var contexts = new List<Context>();
        var owners = new Dictionary<AssemblyIdentity, string>();
        foreach (var project in solution.Projects.OrderBy(item => item.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = projects.SingleOrDefault(item => project.FilePath is not null
                && item.Id == RepositoryModelBuilder.RelativePath(fullRoot, project.FilePath));
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (model is null || compilation is null)
            {
                issues.Add(new("aspnetcore-project-unavailable", $"Project '{project.Name}' has no semantic context for ASP.NET Core analysis."));
                continue;
            }

            var generated = await RepositoryModelBuilder.GeneratedFilesAsync(project, cancellationToken).ConfigureAwait(false);
            contexts.Add(new(model.Id, compilation, generated.Keys.ToHashSet(), fullRoot));
            owners.TryAdd(compilation.Assembly.Identity, model.Id);
        }

        var endpoints = new List<AspNetCoreEndpoint>();
        var registrations = new List<AspNetCoreServiceRegistration>();
        foreach (var context in contexts.OrderBy(item => item.Project, StringComparer.Ordinal))
        {
            var scan = new Scan(context, owners, cancellationToken);
            foreach (var tree in context.Compilation.SyntaxTrees.OrderBy(item => item.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Generated source is not owned by the repository's authors and has no stable source identity.
                if (context.Generated.Contains(tree))
                {
                    continue;
                }

                var syntax = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                scan.Visit(context.Compilation.GetSemanticModel(tree), syntax, endpoints, registrations);
            }
        }

        return (new AspNetCoreFacts(endpoints, registrations), issues);
    }

    private static bool IsOrDerives(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.ContainingNamespace?.ToDisplayString() + "." + current.Name == metadataName && IsReferenced(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether a symbol comes from a referenced assembly rather than the analyzed source.</summary>
    /// <remarks>Framework identities are matched by name, so a source type in a framework namespace must not count as the framework.</remarks>
    private static bool IsReferenced(ISymbol symbol) => symbol.Locations.Length > 0 && symbol.Locations.All(item => item.IsInMetadata);

    private sealed record Context(string Project, Compilation Compilation, HashSet<SyntaxTree> Generated, string Root);

    private sealed class Scan(Context context, IReadOnlyDictionary<AssemblyIdentity, string> owners, CancellationToken cancellationToken)
    {
        internal CancellationToken Token => cancellationToken;

        private readonly INamedTypeSymbol? _controllerBase = context.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ControllerBase")
            is { } controllerBase && IsReferenced(controllerBase) ? controllerBase : null;

        internal void Visit(SemanticModel model, SyntaxNode root, List<AspNetCoreEndpoint> endpoints, List<AspNetCoreServiceRegistration> registrations)
        {
            foreach (var node in root.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (node)
                {
                    case ClassDeclarationSyntax declaration when _controllerBase is not null:
                        VisitController(model, declaration, endpoints);
                        break;
                    case InvocationExpressionSyntax invocation:
                        VisitInvocation(model, invocation, endpoints, registrations);
                        break;
                }
            }
        }

        private void VisitController(SemanticModel model, ClassDeclarationSyntax declaration, List<AspNetCoreEndpoint> endpoints)
        {
            if (model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol type || type.IsAbstract
                || !DerivesFrom(type, _controllerBase!) || HasAttribute(type, "Microsoft.AspNetCore.Mvc.NonControllerAttribute"))
            {
                return;
            }

            // A partial controller is one controller: its first declaration reports every part, and later parts are skipped.
            var parts = type.DeclaringSyntaxReferences.Select(item => item.GetSyntax(cancellationToken)).OfType<ClassDeclarationSyntax>().ToArray();
            if (!ReferenceEquals(parts.FirstOrDefault(), declaration))
            {
                return;
            }

            var shared = new Dependencies(this, model);
            foreach (var part in parts)
            {
                var partModel = SemanticModelFor(model, part);
                foreach (var parameter in ConstructorParameters(part))
                {
                    if (partModel.GetDeclaredSymbol(parameter, cancellationToken) is IParameterSymbol symbol)
                    {
                        shared.Add(symbol.Type, parameter.GetLocation(), injected: true);
                    }
                }

                var members = new Dependencies(this, partModel);
                foreach (var member in part.Members.Where(item => item is FieldDeclarationSyntax or PropertyDeclarationSyntax))
                {
                    members.Collect(member, injected: false);
                }

                shared.Merge(members);
            }

            endpoints.Add(new(context.Project, AspNetCoreEndpointKind.Controller, type.ToDisplayString(Display), DeclaredAuthorization.Undeclared,
                shared.ToList(), Location(declaration.Identifier.GetLocation())));
            var covered = shared.Symbols;
            // Signatures already supplied by a more-derived type; an override or a hiding method replaces the base action.
            var supplied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                var partModel = SemanticModelFor(model, part);
                foreach (var method in part.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (partModel.GetDeclaredSymbol(method, cancellationToken) is IMethodSymbol action)
                    {
                        supplied.Add(Signature(action));
                        AddAction(partModel, method, action, type, covered, endpoints);
                    }
                }
            }

            // Inherited actions belong to the concrete controller's route and authorization.
            for (var baseType = type.BaseType; baseType is not null && !SymbolEqualityComparer.Default.Equals(baseType, _controllerBase); baseType = baseType.BaseType)
            {
                var level = new List<string>();
                foreach (var inherited in baseType.GetMembers().OfType<IMethodSymbol>().OrderBy(item => item.ToDisplayString(Display), StringComparer.Ordinal))
                {
                    var signature = Signature(inherited);
                    level.Add(signature);
                    if (supplied.Contains(signature))
                    {
                        continue;
                    }

                    foreach (var reference in inherited.DeclaringSyntaxReferences)
                    {
                        if (reference.GetSyntax(cancellationToken) is MethodDeclarationSyntax syntax)
                        {
                            AddAction(context.Compilation.GetSemanticModel(syntax.SyntaxTree), syntax, inherited, type, covered, endpoints);
                        }
                    }
                }

                supplied.UnionWith(level);
            }
        }

        private SemanticModel SemanticModelFor(SemanticModel model, SyntaxNode node)
            => ReferenceEquals(model.SyntaxTree, node.SyntaxTree) ? model : context.Compilation.GetSemanticModel(node.SyntaxTree);

        /// <summary>Identifies a method by name, arity and parameter types, which is what overriding and hiding replace.</summary>
        private static string Signature(IMethodSymbol method)
            => method.Name + "`" + method.Arity.ToString(System.Globalization.CultureInfo.InvariantCulture) + "("
                + string.Join(",", method.Parameters.Select(item => item.RefKind + " " + item.Type.ToDisplayString(Display))) + ")";

        private void AddAction(SemanticModel model, MethodDeclarationSyntax method, IMethodSymbol action, INamedTypeSymbol controller,
            HashSet<ITypeSymbol> covered, List<AspNetCoreEndpoint> endpoints)
        {
            if (action.MethodKind != MethodKind.Ordinary || action.DeclaredAccessibility != Accessibility.Public || action.IsStatic
                || action.IsAbstract || action.IsGenericMethod || IsObjectOverride(action)
                || HasAttribute(action, "Microsoft.AspNetCore.Mvc.NonActionAttribute"))
            {
                return;
            }

            var dependencies = new Dependencies(this, model);
            foreach (var parameter in method.ParameterList.Parameters)
            {
                if (model.GetDeclaredSymbol(parameter, cancellationToken) is IParameterSymbol symbol)
                {
                    dependencies.Add(symbol.Type, parameter.GetLocation(), HasAttribute(symbol, "Microsoft.AspNetCore.Mvc.FromServicesAttribute"));
                }
            }

            dependencies.AddReturn(action.ReturnType, method.ReturnType.GetLocation());
            dependencies.Collect(method.Body ?? (SyntaxNode?)method.ExpressionBody, injected: false);
            endpoints.Add(new(context.Project, AspNetCoreEndpointKind.ControllerAction,
                // The concrete controller qualifies inherited actions so each derived route has its own identity.
                SymbolEqualityComparer.Default.Equals(action.ContainingType, controller) ? action.ToDisplayString(Display)
                    : controller.ToDisplayString(Display) + "." + action.ToDisplayString(Display)[(action.ContainingType.ToDisplayString(Display).Length + 1)..],
                ControllerAuthorization(action, controller), dependencies.ToList(covered), Location(method.Identifier.GetLocation())));
        }

        private static bool IsObjectOverride(IMethodSymbol method)
        {
            for (var current = method; current is not null; current = current.OverriddenMethod)
            {
                if (current.ContainingType.SpecialType == SpecialType.System_Object)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<ParameterSyntax> ConstructorParameters(ClassDeclarationSyntax declaration)
            => (declaration.ParameterList?.Parameters ?? [])
                .Concat(declaration.Members.OfType<ConstructorDeclarationSyntax>()
                    .Where(item => item.Modifiers.Any(SyntaxKind.PublicKeyword) && !item.Modifiers.Any(SyntaxKind.StaticKeyword))
                    .SelectMany(item => item.ParameterList.Parameters));

        private static DeclaredAuthorization ControllerAuthorization(IMethodSymbol action, INamedTypeSymbol type)
        {
            var scopes = new List<ISymbol> { action };
            for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                scopes.Add(current);
            }

            // AllowAnonymous at any level bypasses authorization, so it wins over a requirement elsewhere.
            if (scopes.Any(item => HasAttribute(item, "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute")))
            {
                return DeclaredAuthorization.AllowAnonymous;
            }

            return scopes.Any(item => HasAttribute(item, "Microsoft.AspNetCore.Authorization.AuthorizeAttribute"))
                ? DeclaredAuthorization.Required : DeclaredAuthorization.Undeclared;
        }

        private void VisitInvocation(SemanticModel model, InvocationExpressionSyntax invocation, List<AspNetCoreEndpoint> endpoints,
            List<AspNetCoreServiceRegistration> registrations)
        {
            if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method || !IsReferenced(method))
            {
                return;
            }

            var ns = method.ContainingNamespace?.ToDisplayString() ?? "";
            if (MinimalMethods.Contains(method.Name, StringComparer.Ordinal) && ns == "Microsoft.AspNetCore.Builder"
                && method.ContainingType.Name == "EndpointRouteBuilderExtensions")
            {
                VisitMinimalApi(model, invocation, method, endpoints);
            }
            else if (RegistrationMethods.Contains(method.Name, StringComparer.Ordinal) && ns.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal))
            {
                VisitRegistration(model, invocation, method, registrations);
            }
        }

        private void VisitMinimalApi(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method, List<AspNetCoreEndpoint> endpoints)
        {
            // Bind arguments by parameter so named and reordered arguments still find the route and handler.
            ExpressionSyntax? patternSyntax = null, handler = null;
            if (model.GetOperation(invocation, cancellationToken) is IInvocationOperation operation)
            {
                foreach (var argument in operation.Arguments)
                {
                    if (argument.Parameter?.Name == "pattern") { patternSyntax = argument.Value.Syntax as ExpressionSyntax; }
                    else if (argument.Parameter?.Name == "handler") { handler = argument.Value.Syntax as ExpressionSyntax; }
                }
            }

            var pattern = patternSyntax is not null && model.GetConstantValue(patternSyntax, cancellationToken) is { HasValue: true, Value: string text }
                ? text : "(dynamic)";
            var verb = method.Name == "Map" ? "MAP" : method.Name == "MapMethods" ? "METHODS" : method.Name == "MapFallback" ? "FALLBACK" : method.Name[3..].ToUpperInvariant();
            var dependencies = new Dependencies(this, model);
            var flags = new AuthorizationFlags();
            switch (handler)
            {
                case AnonymousFunctionExpressionSyntax lambda:
                    foreach (var parameter in LambdaParameters(lambda))
                    {
                        if (model.GetDeclaredSymbol(parameter, cancellationToken) is IParameterSymbol symbolInfo)
                        {
                            dependencies.Add(symbolInfo.Type, parameter.GetLocation(), HasAttribute(symbolInfo, "Microsoft.AspNetCore.Mvc.FromServicesAttribute"));
                        }
                    }

                    dependencies.Collect(lambda.Body, injected: false);
                    flags.AddAttributes((lambda as LambdaExpressionSyntax)?.AttributeLists.SelectMany(list => list.Attributes)
                        .Select(attribute => model.GetSymbolInfo(attribute, cancellationToken).Symbol?.ContainingType) ?? []);
                    break;
                case { } expression when MethodGroupTarget(model, expression) is { } target:
                    VisitMethodGroup(target, dependencies, flags);
                    break;
            }

            WalkChain(model, invocation, flags);
            // MapGroup prefixes keep /a/x and /b/x distinct; an unresolved group leaves the route relative.
            var route = string.Concat(Enumerable.Reverse(flags.Prefixes)) + pattern;
            endpoints.Add(new(context.Project, AspNetCoreEndpointKind.MinimalApi, $"{verb} {route}", flags.Result(), dependencies.ToList(),
                Location(invocation.GetLocation())));
        }

        /// <summary>Resolves a method group; a Delegate-typed parameter can leave the symbol only as a candidate.</summary>
        private IMethodSymbol? MethodGroupTarget(SemanticModel model, ExpressionSyntax expression)
        {
            var info = model.GetSymbolInfo(expression, cancellationToken);
            return info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().SingleOrDefault();
        }

        private void VisitMethodGroup(IMethodSymbol target, Dependencies dependencies, AuthorizationFlags flags)
        {
            flags.AddAttributes(target.GetAttributes().Select(item => item.AttributeClass));
            foreach (var reference in target.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration)
                {
                    continue;
                }

                var handlerModel = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                var inner = new Dependencies(this, handlerModel);
                inner.AddReturn(target.ReturnType, declaration.ReturnType.GetLocation());
                foreach (var parameter in declaration.ParameterList.Parameters)
                {
                    if (handlerModel.GetDeclaredSymbol(parameter, cancellationToken) is IParameterSymbol symbol)
                    {
                        inner.Add(symbol.Type, parameter.GetLocation(), HasAttribute(symbol, "Microsoft.AspNetCore.Mvc.FromServicesAttribute"));
                    }
                }

                inner.Collect(declaration.Body ?? (SyntaxNode?)declaration.ExpressionBody, injected: false);
                dependencies.Merge(inner);
            }
        }

        private static IEnumerable<ParameterSyntax> LambdaParameters(AnonymousFunctionExpressionSyntax lambda) => lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter],
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters,
            AnonymousMethodExpressionSyntax method => method.ParameterList?.Parameters ?? [],
            _ => [],
        };

        /// <summary>Applies conventions chained after the Map call and those inherited from its route group.</summary>
        private void WalkChain(SemanticModel model, InvocationExpressionSyntax mapCall, AuthorizationFlags flags)
        {
            ExpressionSyntax current = mapCall;
            while (current.Parent is MemberAccessExpressionSyntax access && access.Expression == current
                && access.Parent is InvocationExpressionSyntax outer && outer.Expression == access)
            {
                // A project-defined lookalike must not count as the framework convention it imitates.
                if (IsFramework(model.GetSymbolInfo(outer, cancellationToken).Symbol))
                {
                    flags.AddConvention(access.Name.Identifier.Text);
                }

                current = outer;
            }

            if (mapCall.Expression is MemberAccessExpressionSyntax receiver)
            {
                ResolveReceiver(model, receiver.Expression, flags, 0);
            }
            else
            {
                // A Map call outside extension syntax, such as a static call, has no receiver chain to follow.
                flags.Indeterminate = true;
            }
        }

        private static bool IsFramework(ISymbol? symbol)
            => symbol is not null && IsReferenced(symbol)
                && (symbol.ContainingNamespace?.ToDisplayString() ?? "").StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);

        private void ResolveReceiver(SemanticModel model, ExpressionSyntax receiver, AuthorizationFlags flags, int depth)
        {
            if (depth > 8)
            {
                flags.Indeterminate = true;
                return;
            }

            if (model.GetTypeInfo(receiver, cancellationToken).Type is not { Name: "RouteGroupBuilder" })
            {
                // A route builder that is not a group, such as the application, contributes no conventions.
                return;
            }

            switch (receiver)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    ResolveReceiver(model, parenthesized.Expression, flags, depth + 1);
                    break;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call
                    when model.GetSymbolInfo(call, cancellationToken).Symbol is IMethodSymbol method && IsFramework(method):
                    // Group extensions such as MapGroup, RequireAuthorization and WithTags return the same builder.
                    flags.AddConvention(access.Name.Identifier.Text);
                    if (method.Name == "MapGroup" && call.ArgumentList.Arguments.Count > 0
                        && model.GetConstantValue(call.ArgumentList.Arguments[0].Expression, cancellationToken) is { HasValue: true, Value: string prefix })
                    {
                        flags.Prefixes.Add(prefix);
                    }

                    ResolveReceiver(model, access.Expression, flags, depth + 1);
                    break;
                case IdentifierNameSyntax name when model.GetSymbolInfo(name, cancellationToken).Symbol is ILocalSymbol local:
                    var declarator = local.DeclaringSyntaxReferences.Select(item => item.GetSyntax(cancellationToken))
                        .OfType<VariableDeclaratorSyntax>().FirstOrDefault(item => item.Initializer is not null);
                    if (declarator is not null && ReferenceEquals(declarator.SyntaxTree, model.SyntaxTree))
                    {
                        // Conventions applied in separate statements, such as group.RequireAuthorization(), also configure the group.
                        foreach (var statement in (declarator.FirstAncestorOrSelf<BlockSyntax>() ?? (SyntaxNode)declarator.SyntaxTree.GetRoot(cancellationToken))
                            .DescendantNodes().OfType<InvocationExpressionSyntax>())
                        {
                            if (statement.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax target } member
                                && target.Identifier.Text == local.Name
                                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(target, cancellationToken).Symbol, local)
                                && IsFramework(model.GetSymbolInfo(statement, cancellationToken).Symbol))
                            {
                                flags.AddConvention(member.Name.Identifier.Text);
                            }
                        }
                    }

                    if (declarator?.Initializer is { } initializer && ReferenceEquals(declarator.SyntaxTree, model.SyntaxTree))
                    {
                        ResolveReceiver(model, initializer.Value, flags, depth + 1);
                    }
                    else
                    {
                        flags.Indeterminate = true;
                    }

                    break;
                default:
                    // Parameters, fields, properties and project-defined helpers hide where the group was configured.
                    flags.Indeterminate = true;
                    break;
            }
        }

        private void VisitRegistration(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol method,
            List<AspNetCoreServiceRegistration> registrations)
        {
            var lifetime = method.Name.Contains("Singleton", StringComparison.Ordinal) ? AspNetCoreServiceLifetime.Singleton
                : method.Name.Contains("Transient", StringComparison.Ordinal) ? AspNetCoreServiceLifetime.Transient
                : AspNetCoreServiceLifetime.Scoped;
            var isDbContext = method.Name.StartsWith("AddDbContext", StringComparison.Ordinal);
            if (isDbContext)
            {
                // Only contextLifetime sets the context's lifetime; optionsLifetime configures its options and is ignored.
                if (model.GetOperation(invocation, cancellationToken) is IInvocationOperation operation)
                {
                    foreach (var argument in operation.Arguments.Where(item => item.Parameter?.Name == "contextLifetime"))
                    {
                        if (argument.Value.ConstantValue is { HasValue: true, Value: int value } && value is >= 0 and <= 2)
                        {
                            lifetime = (AspNetCoreServiceLifetime)value;
                        }
                    }
                }
            }

            var definition = method.ReducedFrom ?? method;
            if (!isDbContext && definition.Parameters.Any(item => item.Name == "implementationInstance"))
            {
                // A supplied instance already exists, so the container cannot capture anything on its behalf.
                return;
            }

            ITypeSymbol? service = null, implementation = null;
            if (method.TypeArguments.Length >= 1)
            {
                service = method.TypeArguments[0];
                implementation = method.TypeArguments.Length >= 2 ? method.TypeArguments[1] : service;
            }
            else
            {
                var types = invocation.ArgumentList.Arguments.Select(item => item.Expression).OfType<TypeOfExpressionSyntax>()
                    .Select(item => model.GetTypeInfo(item.Type, cancellationToken).Type).ToArray();
                if (types.Length >= 1)
                {
                    service = types[0];
                    implementation = types.Length >= 2 ? types[1] : service;
                }
            }

            if (service is null or ITypeParameterSymbol)
            {
                return;
            }

            var factory = !isDbContext && definition.Parameters.Any(item => item.Name == "implementationFactory");
            var implementationDisplay = factory || implementation is null or ITypeParameterSymbol ? null : implementation.ToDisplayString(Display);
            var (constructorServices, ambiguous) = implementationDisplay is null ? ([], false) : ConstructorDependencies(implementation!);
            registrations.Add(new(context.Project, lifetime, service.ToDisplayString(Display), implementationDisplay,
                constructorServices, Location(invocation.GetLocation()), ambiguous));
        }

        /// <summary>Lists constructor parameter services; more than one public constructor makes the choice ambiguous.</summary>
        private static (IReadOnlyList<string> Dependencies, bool Ambiguous) ConstructorDependencies(ITypeSymbol implementation)
        {
            if (implementation is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } type)
            {
                return ([], false);
            }

            // The container selects the constructor with the most resolvable parameters; the longest public one approximates it.
            var constructors = type.InstanceConstructors.Where(item => item.DeclaredAccessibility == Accessibility.Public).ToArray();
            var constructor = constructors.OrderByDescending(item => item.Parameters.Length).FirstOrDefault();
            if (constructor is null)
            {
                return ([], false);
            }

            var services = constructor.Parameters.Select(item => item.Type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } collection
                    && collection.Name is "IEnumerable" or "IReadOnlyCollection" or "IReadOnlyList" or "ICollection" or "IList"
                    && collection.ContainingNamespace?.ToDisplayString().StartsWith("System.Collections", StringComparison.Ordinal) == true
                        // Collection injection resolves every registration of the element service.
                        ? collection.TypeArguments[0] : item.Type)
                .Where(item => item.SpecialType == SpecialType.None && item is not ITypeParameterSymbol)
                .Select(item => item.ToDisplayString(Display)).Distinct(StringComparer.Ordinal).ToArray();
            return (services, constructors.Length > 1);
        }

        private SourceLocation Location(Microsoft.CodeAnalysis.Location location)
        {
            var span = location.GetLineSpan();
            return new(Path.GetRelativePath(context.Root, span.Path).Replace('\\', '/'), span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1, span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
        }

        private AspNetCoreDependency Dependency(ITypeSymbol type, Microsoft.CodeAnalysis.Location location, bool injected)
        {
            var origins = new List<AspNetCoreTypeOrigin> { Origin(type) };
            for (var current = type.BaseType; current is not null && current.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType); current = current.BaseType)
            {
                origins.Add(Origin(current));
            }

            origins.AddRange(type.AllInterfaces.Select(Origin));
            string? project = null;
            if (type.ContainingAssembly?.Identity is { } identity)
            {
                owners.TryGetValue(identity, out project);
            }

            return new(type.ToDisplayString(Display), origins.Distinct().ToArray(), injected,
                type.TypeKind == TypeKind.Interface || type.IsAbstract, project, Location(location));
        }

        private static AspNetCoreTypeOrigin Origin(ITypeSymbol type) => new(type.ContainingAssembly?.Name ?? "",
            type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "");

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAttribute(ISymbol symbol, string metadataName)
            => symbol.GetAttributes().Any(item => AspNetCoreFactExtractor.IsOrDerives(item.AttributeClass, metadataName));

        /// <summary>Collects semantic type uses while keeping one dependency per bound type.</summary>
        private sealed class Dependencies(Scan scan, SemanticModel model)
        {
            private readonly Dictionary<ITypeSymbol, AspNetCoreDependency> _items = new(SymbolEqualityComparer.Default);

            internal HashSet<ITypeSymbol> Symbols => [.. _items.Keys];

            internal void Add(ITypeSymbol? type, Microsoft.CodeAnalysis.Location location, bool injected)
            {
                while (type is IArrayTypeSymbol array)
                {
                    type = array.ElementType;
                }

                if (type is not INamedTypeSymbol named || named.SpecialType != SpecialType.None || named.IsAnonymousType
                    || named.TypeKind is TypeKind.Delegate or TypeKind.Enum || named.IsValueType && named.ContainingNamespace?.ToDisplayString() == "System")
                {
                    return;
                }

                var definition = named.OriginalDefinition;
                if (_items.TryGetValue(definition, out var existing))
                {
                    if (injected && !existing.IsInjected)
                    {
                        _items[definition] = new AspNetCoreDependency(existing.Symbol, existing.Lineage, true, existing.IsAbstraction,
                            existing.DeclaringProject, existing.Location);
                    }

                    return;
                }

                _items.Add(definition, scan.Dependency(definition, location, injected));
            }

            internal void Collect(SyntaxNode? node, bool injected)
            {
                if (node is null)
                {
                    return;
                }

                foreach (var name in node.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
                {
                    scan.Token.ThrowIfCancellationRequested();
                    switch (model.GetSymbolInfo(name, scan.Token).Symbol)
                    {
                        case INamedTypeSymbol type:
                            Add(type, name.GetLocation(), injected);
                            break;
                        case IMethodSymbol method:
                            Add(method.ContainingType, name.GetLocation(), injected);
                            break;
                        case IPropertySymbol property:
                            Add(property.ContainingType, name.GetLocation(), injected);
                            Add(property.Type, name.GetLocation(), injected);
                            break;
                        case IFieldSymbol field:
                            Add(field.ContainingType, name.GetLocation(), injected);
                            Add(field.Type, name.GetLocation(), injected);
                            break;
                        case ILocalSymbol local:
                            Add(local.Type, name.GetLocation(), injected);
                            break;
                        case IParameterSymbol parameter:
                            Add(parameter.Type, name.GetLocation(), injected);
                            break;
                    }
                }
            }

            /// <summary>Adds a return type and its type arguments, so Task&lt;T&gt; and ActionResult&lt;T&gt; expose T.</summary>
            internal void AddReturn(ITypeSymbol? type, Microsoft.CodeAnalysis.Location location)
            {
                Add(type, location, injected: false);
                if (type is INamedTypeSymbol { IsGenericType: true } generic)
                {
                    foreach (var argument in generic.TypeArguments)
                    {
                        AddReturn(argument, location);
                    }
                }
            }

            internal void Merge(Dependencies other)
            {
                foreach (var (key, value) in other._items)
                {
                    _items.TryAdd(key, value);
                }
            }

            /// <summary>Lists dependencies, optionally omitting types already attributed to the owning controller.</summary>
            internal IReadOnlyList<AspNetCoreDependency> ToList(HashSet<ITypeSymbol>? covered = null)
                => [.. _items.Where(item => covered is null || item.Value.IsInjected || !covered.Contains(item.Key)).Select(item => item.Value)];
        }
    }

    /// <summary>Accumulates authorization declarations from attributes, endpoint conventions and route groups.</summary>
    private sealed class AuthorizationFlags
    {
        internal bool Required { get; private set; }
        internal bool Anonymous { get; private set; }
        internal bool Indeterminate { get; set; }
        /// <summary>Gets MapGroup prefixes from the nearest group outward.</summary>
        internal List<string> Prefixes { get; } = [];

        internal void AddConvention(string name)
        {
            Required |= name == "RequireAuthorization";
            Anonymous |= name == "AllowAnonymous";
        }

        internal void AddAttributes(IEnumerable<INamedTypeSymbol?> attributes)
        {
            foreach (var attribute in attributes)
            {
                Anonymous |= IsOrDerives(attribute, "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute");
                Required |= IsOrDerives(attribute, "Microsoft.AspNetCore.Authorization.AuthorizeAttribute");
            }
        }

        // AllowAnonymous wins over a requirement; an unresolved group only matters when nothing definite was found.
        internal DeclaredAuthorization Result() => Anonymous ? DeclaredAuthorization.AllowAnonymous
            : Required ? DeclaredAuthorization.Required
            : Indeterminate ? DeclaredAuthorization.Indeterminate : DeclaredAuthorization.Undeclared;
    }
}
