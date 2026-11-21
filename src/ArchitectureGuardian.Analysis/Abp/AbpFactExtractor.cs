using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Frameworks.Abp;
using ArchitectureGuardian.Core.Repositories;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SourceLocation = ArchitectureGuardian.Core.Diagnostics.SourceLocation;

namespace ArchitectureGuardian.Analysis.Abp;

/// <summary>Converts the statically visible ABP Framework conventions of a solution into neutral facts.</summary>
/// <remarks>
/// Matching uses bound symbols and metadata names, so this assembly needs no ABP reference. ABP identities must come from
/// referenced assemblies: a source type that copies an ABP name is not treated as the framework. Projects without ABP
/// references contribute no facts. Module loading, conventional controllers and permissions are runtime and never inferred.
/// </remarks>
public static class AbpFactExtractor
{
    private const string ServicesNamespace = "Volo.Abp.Application.Services";
    private const string MultiTenancyNamespace = "Volo.Abp.MultiTenancy";
    private const int MaximumDtoDepth = 4;
    private static readonly SymbolDisplayFormat Display = SymbolDisplayFormat.CSharpErrorMessageFormat;

    /// <summary>Permission-check methods of ABP and ASP.NET Core authorization that authorize the calling method.</summary>
    private static readonly string[] PermissionChecks =
        ["AuthorizeAsync", "CheckAsync", "CheckPolicyAsync", "IsGrantedAnyAsync", "IsGrantedAsync"];

    /// <summary>CRUD base-class methods and the policy-name property that authorizes each of them.</summary>
    private static readonly Dictionary<string, string> CrudPolicies = new(StringComparer.Ordinal)
    {
        ["CreateAsync"] = "CreatePolicyName",
        ["DeleteAsync"] = "DeletePolicyName",
        ["GetAsync"] = "GetPolicyName",
        ["GetListAsync"] = "GetListPolicyName",
        ["UpdateAsync"] = "UpdatePolicyName",
    };

    /// <summary>Extracts module declarations, application service signatures, repository uses and tenant bypasses.</summary>
    /// <param name="solution">The loaded solution; it must remain undisposed until extraction completes.</param>
    /// <param name="projects">Normalized projects that supply policy-relative identities.</param>
    /// <param name="root">The directory defining portable source and project identities.</param>
    /// <param name="cancellationToken">Cooperative cancellation while compiling and walking syntax.</param>
    /// <returns>The facts plus issues for projects whose semantic context was unavailable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solution"/> or <paramref name="projects"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="root"/> is blank.</exception>
    /// <exception cref="OperationCanceledException">Cancellation interrupts extraction.</exception>
    public static async Task<(AbpFacts Facts, IReadOnlyList<AnalysisIssue> Issues)> ExtractAsync(Solution solution,
        IReadOnlyList<RepositoryProject> projects, string root, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var fullRoot = Path.GetFullPath(root);
        var issues = new List<AnalysisIssue>();
        var identities = new Dictionary<ProjectId, string>();
        foreach (var project in solution.Projects)
        {
            if (project.FilePath is not null && projects.SingleOrDefault(item => item.Id == RepositoryModelBuilder.RelativePath(fullRoot, project.FilePath)) is { } model)
            {
                identities[project.Id] = model.Id;
            }
        }

        var facts = new Collected();
        foreach (var project in solution.Projects.OrderBy(item => item.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (!identities.TryGetValue(project.Id, out var id) || compilation is null)
            {
                issues.Add(new("abp-project-unavailable", $"Project '{project.Name}' has no semantic context for ABP Framework analysis."));
                continue;
            }

            foreach (var reference in project.ProjectReferences)
            {
                if (identities.TryGetValue(reference.ProjectId, out var referenced))
                {
                    facts.References.Add(new(id, referenced));
                }
            }

            var generated = (await RepositoryModelBuilder.GeneratedFilesAsync(project, cancellationToken).ConfigureAwait(false)).Keys.ToHashSet();
            await new Scan(id, fullRoot, solution, compilation, generated, facts, cancellationToken).RunAsync().ConfigureAwait(false);
        }

        return (new AbpFacts(facts.Modules, facts.References, facts.Exposures, facts.Repositories, facts.Methods, facts.Bypasses), issues);
    }

    /// <summary>Determines whether a symbol comes from a referenced assembly rather than the analyzed source.</summary>
    private static bool IsReferenced(ISymbol symbol) => symbol.Locations.Length > 0 && symbol.Locations.All(item => item.IsInMetadata);

    /// <summary>Determines whether a type is the named ABP or ASP.NET Core framework type from a framework assembly.</summary>
    /// <remarks>The declaring assembly is checked too, so a referenced shim that copies an ABP name is not the framework.</remarks>
    private static bool Is(ITypeSymbol? type, string ns, string metadataName) => type?.OriginalDefinition is INamedTypeSymbol named
        && named.MetadataName == metadataName && IsReferenced(named) && named.ContainingNamespace?.ToDisplayString() == ns
        && named.ContainingAssembly?.Name is { } assembly
        && assembly.StartsWith(ns.StartsWith("Volo.Abp", StringComparison.Ordinal) ? "Volo.Abp." : "Microsoft.AspNetCore.", StringComparison.Ordinal);

    private static bool Implements(ITypeSymbol type, string ns, string metadataName)
        => Is(type, ns, metadataName) || type.AllInterfaces.Any(item => Is(item, ns, metadataName));

    private static bool DerivesFrom(ITypeSymbol? type, string ns, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (Is(current, ns, metadataName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsModule(INamedTypeSymbol type) => DerivesFrom(type.BaseType, "Volo.Abp.Modularity", "AbpModule");

    private static bool IsApplicationService(INamedTypeSymbol type) => !Is(type, ServicesNamespace, "IApplicationService")
        && Implements(type, ServicesNamespace, "IApplicationService");

    private static bool IsEntity(ITypeSymbol type) => Implements(type, "Volo.Abp.Domain.Entities", "IEntity");

    private static bool IsRepository(ITypeSymbol type) => Implements(type, "Volo.Abp.Domain.Repositories", "IRepository");

    /// <summary>Collects facts from every project before snapshotting them in deterministic order.</summary>
    private sealed class Collected
    {
        internal List<AbpModuleClass> Modules { get; } = [];
        internal List<AbpProjectReference> References { get; } = [];
        internal List<AbpEntityExposure> Exposures { get; } = [];
        internal List<AbpRepositoryUse> Repositories { get; } = [];
        internal List<AbpServiceMethod> Methods { get; } = [];
        internal List<AbpTenantBypass> Bypasses { get; } = [];
    }

    /// <summary>Walks one project's source, keeping per-type classifications cached for the project.</summary>
    private sealed class Scan(string project, string root, Solution solution, Compilation compilation, HashSet<SyntaxTree> generated,
        Collected facts, CancellationToken cancellationToken)
    {
        private readonly Dictionary<ITypeSymbol, bool> _repositories = new(SymbolEqualityComparer.Default);

        internal async Task RunAsync()
        {
            var types = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var declared = new List<INamedTypeSymbol>();
            foreach (var tree in compilation.SyntaxTrees.OrderBy(item => item.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Generated source is not owned by the repository's authors and has no stable source identity.
                if (generated.Contains(tree))
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(tree);
                var syntax = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                foreach (var declaration in syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    // Partial types are visited once, in the order of their first non-generated declaration.
                    if (model.GetDeclaredSymbol(declaration, cancellationToken) is INamedTypeSymbol type && types.Add(type))
                    {
                        declared.Add(type);
                    }
                }

                VisitNames(model, syntax);
            }

            foreach (var type in declared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (type.TypeKind == TypeKind.Class && !type.IsAbstract && IsModule(type))
                {
                    facts.Modules.Add(new(project, type.ToDisplayString(Display), DependsOnClosure(type), Location(type)));
                }

                if (type.TypeKind is TypeKind.Class or TypeKind.Interface && IsApplicationService(type))
                {
                    CollectExposures(type);
                    if (type.TypeKind == TypeKind.Class && !type.IsAbstract && IsPublishable(type) && IsRemote(type))
                    {
                        await CollectMethodsAsync(type).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>Collects every module ABP loads through [DependsOn], following inherited and transitive declarations.</summary>
        private static IEnumerable<string> DependsOnClosure(INamedTypeSymbol module)
        {
            var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { module };
            var pending = new Queue<INamedTypeSymbol>([module]);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                // ABP reads [DependsOn] with inheritance, so a base module's declarations also apply.
                for (var type = current; type is not null; type = type.BaseType)
                {
                    foreach (var attribute in type.GetAttributes().Where(item => DerivesFrom(item.AttributeClass, "Volo.Abp.Modularity", "DependsOnAttribute")))
                    {
                        foreach (var argument in attribute.ConstructorArguments)
                        {
                            var values = argument.Kind == TypedConstantKind.Array ? argument.Values : [argument];
                            foreach (var value in values)
                            {
                                if (value.Value is INamedTypeSymbol dependency && visited.Add(dependency))
                                {
                                    pending.Enqueue(dependency);
                                }
                            }
                        }
                    }
                }
            }

            visited.Remove(module);
            return visited.Select(item => item.ToDisplayString(Display));
        }

        /// <summary>Mirrors ABP's conventional-controller selection, which publishes only public, non-generic service types.</summary>
        private static bool IsPublishable(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility != Accessibility.Public || current.IsGenericType)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Determines whether ABP can expose a service remotely; RemoteService(false) and integration services are not public endpoints.</summary>
        private static bool IsRemote(INamedTypeSymbol type)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (current.GetAttributes().FirstOrDefault(item => DerivesFrom(item.AttributeClass, "Volo.Abp", "RemoteServiceAttribute")) is { } remote)
                {
                    return IsEnabled(remote);
                }
            }

            // ABP checks [IntegrationService] on the type, its base types and its interfaces.
            return !BaseTypes(type).Prepend(type).Concat(type.AllInterfaces).SelectMany(item => item.GetAttributes())
                .Any(item => DerivesFrom(item.AttributeClass, "Volo.Abp", "IntegrationServiceAttribute"));
        }

        private static bool IsEnabled(AttributeData remote)
        {
            var named = remote.NamedArguments.Where(item => item.Key == "IsEnabled").Select(item => item.Value.Value).OfType<bool>().ToArray();
            if (named.Length > 0)
            {
                return named[0];
            }

            return remote.ConstructorArguments.Select(item => item.Value).OfType<bool>().FirstOrDefault(defaultValue: true);
        }

        /// <summary>Records entities exposed by public members declared on an application service, directly or through DTOs.</summary>
        private void CollectExposures(INamedTypeSymbol service)
        {
            var name = service.ToDisplayString(Display);
            foreach (var method in service.GetMembers().OfType<IMethodSymbol>()
                         .Where(item => item.MethodKind == MethodKind.Ordinary && item.DeclaredAccessibility == Accessibility.Public && !item.IsStatic && IsAuthored(item)))
            {
                var location = Location(method);
                foreach (var type in method.Parameters.Select(item => item.Type).Prepend(method.ReturnType))
                {
                    Expose(type, method.Name, location, depth: 0, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
                }
            }

            void Expose(ITypeSymbol type, string path, SourceLocation? location, int depth, HashSet<ITypeSymbol> visited)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (type is IArrayTypeSymbol array)
                {
                    type = array.ElementType;
                }

                // Visited types are tracked per path, so a DTO first met at the depth limit is still inspected on a shorter path.
                if (type is not INamedTypeSymbol named || named.SpecialType != SpecialType.None || !visited.Add(named))
                {
                    return;
                }

                try
                {
                    ExposeNamed(named, path, location, depth, visited);
                }
                finally
                {
                    visited.Remove(named);
                }
            }

            void ExposeNamed(INamedTypeSymbol named, string path, SourceLocation? location, int depth, HashSet<ITypeSymbol> visited)
            {
                if (IsEntity(named))
                {
                    facts.Exposures.Add(new(project, name, path, named.OriginalDefinition.ToDisplayString(Display), location));
                    return;
                }

                // Wrappers such as Task<T>, List<T> and PagedResultDto<T> expose their type arguments.
                foreach (var argument in named.TypeArguments)
                {
                    Expose(argument, path, location, depth, visited);
                }

                // A source DTO is part of the contract: its public properties expose whatever they hold.
                if (depth >= MaximumDtoDepth || named.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface) || IsReferenced(named))
                {
                    return;
                }

                for (var current = named; current is not null && !IsReferenced(current); current = current.BaseType)
                {
                    foreach (var property in current.GetMembers().OfType<IPropertySymbol>()
                                 .Where(item => item.DeclaredAccessibility == Accessibility.Public && !item.IsStatic && !item.IsIndexer))
                    {
                        Expose(property.Type, $"{path} → {named.Name}.{property.Name}", Location(property) ?? location, depth + 1, visited);
                    }
                }
            }
        }

        /// <summary>Records the public instance methods ABP can expose for a concrete service, with their declared authorization.</summary>
        private async Task CollectMethodsAsync(INamedTypeSymbol service)
        {
            var name = service.ToDisplayString(Display);
            var classAuthorization = Attributes(service.GetAttributes().Concat(BaseTypes(service).SelectMany(item => item.GetAttributes())));
            var policyNames = await AssignedPolicyNamesAsync(service).ConfigureAwait(false);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in BaseTypes(service).Prepend(service))
            {
                // ApplicationService and its framework bases publish no endpoints; ABP CRUD bases publish their public methods.
                if (IsReferenced(type) && (type.ContainingNamespace?.ToDisplayString() != ServicesNamespace || type.Name == "ApplicationService"))
                {
                    break;
                }

                foreach (var method in type.GetMembers().OfType<IMethodSymbol>()
                             .Where(item => item.MethodKind == MethodKind.Ordinary && item.DeclaredAccessibility == Accessibility.Public && !item.IsStatic))
                {
                    // The most derived declaration wins; overrides and hiding methods replace inherited ones. The signature keeps
                    // overloads apart, so an accepted finding for one overload never covers another.
                    var signature = method.Name + "(" + string.Join(", ", method.Parameters.Select(item => item.Type.ToDisplayString(Display))) + ")";
                    if (!seen.Add(signature)
                        || method.GetAttributes().FirstOrDefault(item => DerivesFrom(item.AttributeClass, "Volo.Abp", "RemoteServiceAttribute")) is { } remote && !IsEnabled(remote))
                    {
                        continue;
                    }

                    var inherited = IsReferenced(method);
                    // Authorization attributes are inherited, so an override keeps the attributes of the methods it overrides.
                    var authorization = Attributes(Overridden(method).SelectMany(item => item.GetAttributes())) switch
                    {
                        AbpAuthorizationDeclaration.None => classAuthorization,
                        var declared => classAuthorization == AbpAuthorizationDeclaration.AllowAnonymous ? classAuthorization : declared,
                    };
                    if (authorization == AbpAuthorizationDeclaration.None)
                    {
                        authorization = inherited
                            ? CrudPolicies.TryGetValue(method.Name, out var policy) && policyNames.Contains(policy) ? AbpAuthorizationDeclaration.PolicyName : authorization
                            : await ChecksPermissionAsync(method).ConfigureAwait(false) ? AbpAuthorizationDeclaration.PermissionCheck : authorization;
                    }

                    facts.Methods.Add(new(project, name, signature, authorization, inherited, inherited ? Location(service) : Location(method)));
                }
            }
        }

        private static IEnumerable<IMethodSymbol> Overridden(IMethodSymbol method)
        {
            for (var current = method; current is not null; current = current.OverriddenMethod)
            {
                yield return current;
            }
        }

        /// <summary>Gets a semantic model for a declaration, which can live in another project, such as a source base class.</summary>
        private async Task<SemanticModel?> ModelAsync(SyntaxTree tree)
        {
            if (compilation.ContainsSyntaxTree(tree))
            {
                return compilation.GetSemanticModel(tree);
            }

            // A tree binds only in the compilation that owns it; using another compilation's model would throw.
            return solution.GetDocument(tree) is { } document ? await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) : null;
        }

        private static bool IsNullOrEmpty(Optional<object?> value) => value.HasValue && value.Value is null or "";

        private static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                yield return current;
            }
        }

        /// <summary>Classifies authorization attributes; ABP and ASP.NET Core let an explicit AllowAnonymous win.</summary>
        private static AbpAuthorizationDeclaration Attributes(IEnumerable<AttributeData> attributes)
        {
            var result = AbpAuthorizationDeclaration.None;
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeClass is not { } type)
                {
                    continue;
                }

                if (Implements(type, "Microsoft.AspNetCore.Authorization", "IAllowAnonymous"))
                {
                    return AbpAuthorizationDeclaration.AllowAnonymous;
                }

                if (Implements(type, "Microsoft.AspNetCore.Authorization", "IAuthorizeData"))
                {
                    result = AbpAuthorizationDeclaration.Authorize;
                }
            }

            return result;
        }

        /// <summary>Determines whether a source method body calls an ABP or ASP.NET Core permission check.</summary>
        private async Task<bool> ChecksPermissionAsync(IMethodSymbol method)
        {
            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                var syntax = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
                if (await ModelAsync(reference.SyntaxTree).ConfigureAwait(false) is not { } model)
                {
                    continue;
                }

                foreach (var invocation in syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol called
                        && (called.ReducedFrom ?? called) is var definition && PermissionChecks.Contains(definition.Name, StringComparer.Ordinal)
                        && IsReferenced(definition) && definition.ContainingAssembly?.Name is { } assembly
                        && (assembly.StartsWith("Volo.Abp.", StringComparison.Ordinal) || assembly.StartsWith("Microsoft.AspNetCore.Authorization", StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Finds CRUD policy-name properties that a service, or a source base class, sets to a policy.</summary>
        /// <remarks>Assigning or returning a constant null or empty name supplies no policy, so it does not count.</remarks>
        private async Task<HashSet<string>> AssignedPolicyNamesAsync(INamedTypeSymbol service)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in BaseTypes(service).Prepend(service).Where(item => !IsReferenced(item)))
            {
                foreach (var reference in type.DeclaringSyntaxReferences)
                {
                    var syntax = await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false);
                    if (await ModelAsync(reference.SyntaxTree).ConfigureAwait(false) is not { } model)
                    {
                        continue;
                    }

                    foreach (var assignment in syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    {
                        if (model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is IPropertySymbol property && CrudPolicies.ContainsValue(property.Name)
                            && property.ContainingNamespace?.ToDisplayString() == ServicesNamespace
                            && !IsNullOrEmpty(model.GetConstantValue(assignment.Right, cancellationToken)))
                        {
                            names.Add(property.Name);
                        }
                    }

                    // An overriding getter supplies the policy only when it can return a name.
                    foreach (var property in syntax.DescendantNodes().OfType<PropertyDeclarationSyntax>()
                                 .Where(item => item.Modifiers.Any(modifier => modifier.ValueText == "override") && CrudPolicies.ContainsValue(item.Identifier.ValueText)))
                    {
                        var returned = (property.ExpressionBody is { } body ? [body.Expression] : Array.Empty<ExpressionSyntax>())
                            .Concat((property.AccessorList?.Accessors ?? []).Where(accessor => accessor.Keyword.ValueText == "get")
                                .SelectMany(accessor => accessor.ExpressionBody is { } arrow ? [arrow.Expression]
                                    : accessor.DescendantNodes().OfType<ReturnStatementSyntax>().Select(item => item.Expression).OfType<ExpressionSyntax>()));
                        if (returned.Any(expression => !IsNullOrEmpty(model.GetConstantValue(expression, cancellationToken))))
                        {
                            names.Add(property.Identifier.ValueText);
                        }
                    }
                }
            }

            return names;
        }

        /// <summary>Records repository uses and tenant-isolation bypasses for every bound name in a tree.</summary>
        private void VisitNames(SemanticModel model, SyntaxNode syntax)
        {
            foreach (var node in syntax.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is InvocationExpressionSyntax invocation)
                {
                    Bypass(model, invocation);
                    continue;
                }

                // Indexers bind on element access rather than a name, so store[0] must be inspected separately.
                if (node is not (SimpleNameSyntax or ElementAccessExpressionSyntax) || model.GetSymbolInfo(node, cancellationToken).Symbol is not { } symbol
                    || User(model, node) is not { } user)
                {
                    continue;
                }

                // A member access can name the same type twice, such as a property's containing and declared type.
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var type in Types(symbol).SelectMany(item => WithTypeArguments(item, depth: 0)))
                {
                    if (type is INamedTypeSymbol named && named.SpecialType == SpecialType.None && Repository(named.OriginalDefinition)
                        && named.ToDisplayString(Display) is var display && seen.Add(display))
                    {
                        facts.Repositories.Add(new(project, user.ToDisplayString(Display), display, Location(node.GetLocation())));
                    }
                }
            }
        }

        /// <summary>Records calls that disable the IMultiTenant filter or switch the current tenant to the host.</summary>
        private void Bypass(SemanticModel model, InvocationExpressionSyntax invocation)
        {
            if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method || User(model, invocation) is not { } user)
            {
                return;
            }

            var container = method.ContainingType;
            var multiTenant = method.Name == "Disable"
                && (Implements(container, "Volo.Abp.Data", "IDataFilter") && method.TypeArguments.Length == 1 && Is(method.TypeArguments[0], MultiTenancyNamespace, "IMultiTenant")
                    || Implements(container, "Volo.Abp.Data", "IDataFilter`1") && container.TypeArguments.Length == 1 && Is(container.TypeArguments[0], MultiTenancyNamespace, "IMultiTenant"));
            // The tenant ID is matched by its bound parameter, so named arguments in any order are classified correctly.
            var host = method.Name == "Change" && Implements(container, MultiTenancyNamespace, "ICurrentTenant")
                && model.GetOperation(invocation, cancellationToken) is IInvocationOperation operation
                && operation.Arguments.FirstOrDefault(item => item.Parameter?.Ordinal == 0) is { } id && IsNull(id.Value);
            if (multiTenant || host)
            {
                facts.Bypasses.Add(new(project, user.ToDisplayString(Display),
                    multiTenant ? AbpTenantBypassKind.DisableMultiTenantFilter : AbpTenantBypassKind.ChangeToHost,
                    multiTenant ? "IDataFilter.Disable<IMultiTenant>" : "ICurrentTenant.Change(null)", Location(invocation.GetLocation())));
            }
        }

        private static bool IsNull(IOperation value)
        {
            while (value is IConversionOperation conversion)
            {
                value = conversion.Operand;
            }

            return value is IDefaultValueOperation || value.ConstantValue is { HasValue: true, Value: null };
        }

        private static IEnumerable<ITypeSymbol?> Types(ISymbol symbol) => symbol switch
        {
            // Members contribute only their declared types: the receiver is already captured where it was declared,
            // and a member's declaring base interface, such as IReadOnlyBasicRepository<T>, is not what the code chose.
            INamedTypeSymbol type => [type],
            IMethodSymbol method => [method.ReturnType],
            IPropertySymbol property => [property.Type],
            IFieldSymbol field => [field.Type],
            ILocalSymbol local => [local.Type],
            IParameterSymbol parameter => [parameter.Type],
            _ => [],
        };

        /// <summary>Expands bound generic arguments, so a Lazy&lt;IRepository&lt;Book, Guid&gt;&gt; exposes the repository.</summary>
        private static IEnumerable<ITypeSymbol?> WithTypeArguments(ITypeSymbol? type, int depth)
        {
            while (type is IArrayTypeSymbol array)
            {
                type = array.ElementType;
            }

            yield return type;
            if (type is INamedTypeSymbol { IsGenericType: true } generic && depth < 8)
            {
                foreach (var argument in generic.TypeArguments.SelectMany(item => WithTypeArguments(item, depth + 1)))
                {
                    yield return argument;
                }
            }
        }

        private bool Repository(INamedTypeSymbol type)
        {
            if (!_repositories.TryGetValue(type, out var result))
            {
                result = IsRepository(type);
                _repositories.Add(type, result);
            }

            return result;
        }

        /// <summary>Finds the innermost type whose source contains a node; top-level statements belong to their synthesized type.</summary>
        private INamedTypeSymbol? User(SemanticModel model, SyntaxNode node)
        {
            if (node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is { } declaration)
            {
                return model.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
            }

            for (var symbol = model.GetEnclosingSymbol(node.SpanStart, cancellationToken); symbol is not null; symbol = symbol.ContainingSymbol)
            {
                if (symbol is INamedTypeSymbol type)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>Determines whether a member is declared in authored, rather than generated, source.</summary>
        private bool IsAuthored(ISymbol symbol) => symbol.DeclaringSyntaxReferences.Any(item => !generated.Contains(item.SyntaxTree));

        /// <summary>Gets the first authored declaration of a source symbol, ordered by path and position.</summary>
        private SourceLocation? Location(ISymbol symbol) => symbol.Locations
            .Where(item => item.IsInSource && item.SourceTree is { } tree && !generated.Contains(tree))
            .OrderBy(item => item.SourceTree!.FilePath, StringComparer.Ordinal).ThenBy(item => item.SourceSpan.Start)
            .Select(item => Location(item)).FirstOrDefault();

        private SourceLocation Location(Microsoft.CodeAnalysis.Location location)
        {
            var span = location.GetLineSpan();
            return new(Path.GetRelativePath(root, span.Path).Replace('\\', '/'), span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1, span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
        }
    }
}
