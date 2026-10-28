using ArchitectureGuardian.Analysis.Repositories;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SourceLocation = ArchitectureGuardian.Core.Diagnostics.SourceLocation;

namespace ArchitectureGuardian.Analysis.Dependencies;

/// <summary>Extracts evaluated references and semantic dependencies into a host-independent evidence graph.</summary>
public sealed class DependencyGraphBuilder
{
    /// <summary>Normalizes a loaded workspace, extracts its available dependencies and aggregates module/layer edges.</summary>
    /// <param name="workspace">The loaded workspace, which must remain undisposed until extraction completes.</param>
    /// <param name="policyDirectory">The directory defining project and evidence path identities.</param>
    /// <param name="policy">Validated ownership and dependency permissions.</param>
    /// <param name="cancellationToken">Cooperative cancellation throughout normalization and extraction.</param>
    /// <returns>A neutral snapshot with combined coverage; unresolved context never becomes a completed clean graph.</returns>
    /// <remarks>Runtime dispatch and reflection are not inferred. Unbound dynamic operations explicitly reduce coverage.</remarks>
    public async Task<DependencyBuildResult> BuildAsync(WorkspaceLoadResult workspace, string policyDirectory,
        ArchitecturePolicy policy, CancellationToken cancellationToken = default)
    {
        var normalized = await new RepositoryModelBuilder()
            .BuildAsync(workspace, policyDirectory, policy, cancellationToken)
            .ConfigureAwait(false);
        if (normalized.Model is not { } repository || workspace.Solution is not { } solution)
        {
            return new(normalized.Model, null, normalized.Status, normalized.Issues);
        }

        var extraction = new Extraction(solution, repository, Path.GetFullPath(policyDirectory), normalized.Issues,
            cancellationToken);
        try
        {
            await extraction.RunAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(repository, extraction.Graph(), normalized.Status == AnalysisStatus.Failed
                ? AnalysisStatus.Failed
                : extraction.Issues.Count == 0
                    ? AnalysisStatus.Completed
                    : AnalysisStatus.Incomplete, extraction.Issues);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(repository, extraction.Graph(), AnalysisStatus.Cancelled, extraction.Issues);
        }
        catch (Exception exception)
        {
            extraction.Issues.Add(new("dependency-extraction-failed",
                $"{exception.GetType().Name}: {exception.Message}"));
            return new(repository, extraction.Graph(), AnalysisStatus.Failed, extraction.Issues);
        }
    }

    /// <summary>Holds compiler objects only for the lifetime of one extraction.</summary>
    private sealed class Extraction
    {
        private readonly Solution _solution;
        private readonly RepositoryModel _repository;
        private readonly string _root;
        private readonly CancellationToken _cancellationToken;
        private readonly List<DependencyNode> _nodes = [];
        private readonly List<DependencyEdge> _edges = [];
        private readonly Dictionary<ProjectId, Context> _contexts = [];

        internal Extraction(Solution solution, RepositoryModel repository, string root,
            IEnumerable<AnalysisIssue> issues,
            CancellationToken cancellationToken)
        {
            _solution = solution;
            _repository = repository;
            _root = root;
            _cancellationToken = cancellationToken;
            Issues = issues.ToHashSet();
            foreach (var project in repository.Projects)
            {
                _nodes.Add(ProjectNode(project));
                _nodes.Add(AssemblyNode(project));
                foreach (var ns in project.Assembly.Namespaces)
                {
                    _nodes.Add(NamespaceNode(ns));
                    _nodes.AddRange(ns.Types.Select(TypeNode));
                }
            }
        }

        internal HashSet<AnalysisIssue> Issues { get; }

        internal DependencyGraph Graph() => new DependencyGraph(_nodes, _edges).Aggregate(_repository);

        internal async Task RunAsync()
        {
            foreach (var project in _solution.Projects.OrderBy(project => project.FilePath, StringComparer.Ordinal))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var model = _repository.Projects.SingleOrDefault(item => project.FilePath is not null
                                                                         && item.Id ==
                                                                         RepositoryModelBuilder.RelativePath(_root,
                                                                             project.FilePath));
                var compilation = await project.GetCompilationAsync(_cancellationToken).ConfigureAwait(false);
                if (model is null || compilation is null)
                {
                    Issues.Add(new("dependency-project-unavailable",
                        $"Project '{project.Name}' has no normalized semantic context."));
                    continue;
                }

                _contexts.Add(project.Id, new(project, model, compilation,
                    await RepositoryModelBuilder.GeneratedFilesAsync(project, _cancellationToken)
                        .ConfigureAwait(false)));
            }

            foreach (var context in _contexts.Values.OrderBy(context => context.Model.Id, StringComparer.Ordinal))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                AddReferences(context);
                foreach (var diagnostic in context.Compilation.GetDiagnostics(_cancellationToken)
                             .Where(diagnostic =>
                                 diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
                {
                    Issues.Add(new("dependency-semantic-error",
                        $"{context.Model.Id}: {diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}",
                        location: diagnostic.Location.IsInSource ? Location(context, diagnostic.Location) : null));
                }

                foreach (var tree in context.Compilation.SyntaxTrees.OrderBy(tree => tree.FilePath,
                             StringComparer.Ordinal))
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    var root = await tree.GetRootAsync(_cancellationToken).ConfigureAwait(false);
                    Scan(context, context.Compilation.GetSemanticModel(tree), root);
                }
            }
        }

        private void AddReferences(Context context)
        {
            foreach (var reference in context.Project.ProjectReferences)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!_contexts.TryGetValue(reference.ProjectId, out var target))
                {
                    Issues.Add(new("dependency-project-reference-unresolved",
                        $"Project '{context.Model.Id}' has an unavailable project reference."));
                    continue;
                }

                var evidence = new DependencyEvidence(context.Model.Id, target.Model.Id,
                    new($"Project '{context.Model.Id}' references '{target.Model.Id}'."),
                    projectReference: target.Model.Id);
                Add(ProjectNode(context.Model), ProjectNode(target.Model), evidence);
                Add(AssemblyNode(context.Model), AssemblyNode(target.Model), evidence);
            }

            foreach (var reference in context.Compilation.References)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var symbol = context.Compilation.GetAssemblyOrModuleSymbol(reference);
                var assembly = symbol as IAssemblySymbol ?? (symbol as IModuleSymbol)?.ContainingAssembly;
                if (assembly is null)
                {
                    Issues.Add(new("dependency-assembly-unresolved",
                        $"Project '{context.Model.Id}' has an unresolved assembly reference."));
                    continue;
                }

                var target = AssemblyOwner(context, assembly);
                Add(AssemblyNode(context.Model),
                    target is null ? ExternalAssemblyNode(assembly) : AssemblyNode(target.Model),
                    new(context.Model.Id, target?.Model.Id,
                        new($"Assembly '{context.Model.Assembly.Name}' references '{assembly.Identity}'.")));
            }
        }

        private void Scan(Context context, SemanticModel semantic, SyntaxNode root)
        {
            var operations = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);
            foreach (var node in root.DescendantNodesAndSelf())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (node.AncestorsAndSelf().Any(ancestor => ancestor is UsingDirectiveSyntax))
                {
                    continue;
                }

                // Empty array dimensions and intrinsic constraints have error-type sentinels in TypeInfo,
                // but do not reference unresolved types. Bound element types and escaped names still run.
                if (node is OmittedArraySizeExpressionSyntax
                    || node is IdentifierNameSyntax { Parent: TypeConstraintSyntax constraint } constraintName
                    && constraint.Parent is TypeParameterConstraintClauseSyntax clause
                    && semantic.GetSymbolInfo(clause.Name, _cancellationToken).Symbol is ITypeParameterSymbol parameter
                    && (constraintName.Identifier.Text == "notnull" && parameter.HasNotNullConstraint
                        || constraintName.Identifier.Text == "unmanaged" && parameter.HasUnmanagedTypeConstraint))
                {
                    continue;
                }

                // nameof is represented as invocation syntax, but its intrinsic callee has an error-type
                // sentinel rather than a referenced type. Keep scanning the bound argument normally.
                if (node is IdentifierNameSyntax
                    {
                        Identifier.ValueText: "nameof", Parent: InvocationExpressionSyntax nameOf
                    }
                    && nameOf.Expression == node &&
                    semantic.GetOperation(nameOf, _cancellationToken) is INameOfOperation)
                {
                    continue;
                }

                if (node is ExpressionSyntax or AttributeSyntax or ConstructorInitializerSyntax)
                {
                    var symbol = semantic.GetSymbolInfo(node, _cancellationToken).Symbol;
                    var origin = Origin(semantic, node);
                    if (symbol is not null)
                    {
                        AddSymbol(context, origin, symbol, node.GetLocation());
                    }

                    if (node is ExpressionSyntax expression)
                    {
                        var types = semantic.GetTypeInfo(expression, _cancellationToken);
                        AddType(context, origin, types.Type, node.GetLocation());
                        AddType(context, origin, types.ConvertedType, node.GetLocation());
                        AddSymbol(context, origin, semantic.GetConversion(expression, _cancellationToken).MethodSymbol,
                            node.GetLocation());
                    }
                }

                if (node is AssignmentExpressionSyntax assignment)
                {
                    AddDeconstruction(context, Origin(semantic, node), semantic.GetDeconstructionInfo(assignment),
                        node.GetLocation());
                }

                if (node is CommonForEachStatementSyntax loop)
                {
                    var info = semantic.GetForEachStatementInfo(loop);
                    var origin = Origin(semantic, node);
                    foreach (var symbol in new ISymbol?[]
                             {
                                 info.GetEnumeratorMethod, info.MoveNextMethod, info.CurrentProperty, info.DisposeMethod
                             })
                    {
                        AddSymbol(context, origin, symbol, node.GetLocation());
                    }

                    AddType(context, origin, info.ElementType, node.GetLocation());
                    if (loop is ForEachVariableStatementSyntax variableLoop)
                    {
                        AddDeconstruction(context, origin, semantic.GetDeconstructionInfo(variableLoop),
                            node.GetLocation());
                    }
                }

                if (node is AwaitExpressionSyntax awaitExpression)
                {
                    var info = semantic.GetAwaitExpressionInfo(awaitExpression);
                    var origin = Origin(semantic, node);
                    foreach (var symbol in new ISymbol?[]
                                 { info.GetAwaiterMethod, info.IsCompletedProperty, info.GetResultMethod })
                    {
                        AddSymbol(context, origin, symbol, node.GetLocation());
                    }
                }

                if (node is StatementSyntax or MemberDeclarationSyntax or AttributeSyntax
                    or ConstructorInitializerSyntax
                    or EqualsValueClauseSyntax or ArrowExpressionClauseSyntax)
                {
                    if (semantic.GetOperation(node, _cancellationToken) is { Parent: null } operation)
                    {
                        ScanOperations(context, semantic, operation, operations);
                    }
                }
            }
        }

        private void AddDeconstruction(Context context, ISymbol? origin, DeconstructionInfo info, Location location)
        {
            // Deconstruction methods and element conversions are compiler-selected bindings, not invocation operations.
            var pending = new Stack<DeconstructionInfo>();
            pending.Push(info);
            while (pending.TryPop(out var current))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                AddSymbol(context, origin, current.Method, location);
                AddSymbol(context, origin, current.Conversion?.MethodSymbol, location);
                if (!current.Nested.IsDefaultOrEmpty)
                {
                    foreach (var nested in current.Nested)
                    {
                        pending.Push(nested);
                    }
                }
            }
        }

        private void ScanOperations(Context context, SemanticModel semantic, IOperation root,
            HashSet<IOperation> visited)
        {
            var pending = new Stack<IOperation>();
            pending.Push(root);
            while (pending.TryPop(out var operation))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!visited.Add(operation))
                {
                    continue;
                }

                var location = operation.Syntax.GetLocation();
                var origin = Origin(semantic, operation.Syntax);
                AddType(context, origin, operation.Type, location);
                ISymbol? symbol = operation switch
                {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IObjectCreationOperation creation => creation.Constructor,
                    IMemberReferenceOperation member => member.Member,
                    IConversionOperation conversion => conversion.OperatorMethod,
                    IBinaryOperation binary => binary.OperatorMethod,
                    IUnaryOperation unary => unary.OperatorMethod,
                    IIncrementOrDecrementOperation increment => increment.OperatorMethod,
                    ICompoundAssignmentOperation assignment => assignment.OperatorMethod,
                    IRecursivePatternOperation pattern => pattern.DeconstructSymbol,
                    _ => null,
                };
                AddSymbol(context, origin, symbol, location);
                if (HasDynamicBinding(operation))
                {
                    Issues.Add(new("dependency-dynamic-unresolved",
                        $"Project '{context.Model.Id}' contains a dynamic operation whose target cannot be established statically.",
                        location: Location(context, location)));
                }

                foreach (var child in operation.ChildOperations)
                {
                    pending.Push(child);
                }
            }
        }

        private static bool HasDynamicBinding(IOperation operation) => operation switch
        {
            IDynamicInvocationOperation or IDynamicMemberReferenceOperation or IDynamicObjectCreationOperation
                or IDynamicIndexerAccessOperation => true,
            IBinaryOperation binary => binary.LeftOperand.Type?.TypeKind == TypeKind.Dynamic
                                       || binary.RightOperand.Type?.TypeKind == TypeKind.Dynamic,
            IUnaryOperation unary => unary.Operand.Type?.TypeKind == TypeKind.Dynamic,
            IIncrementOrDecrementOperation increment => increment.Target.Type?.TypeKind == TypeKind.Dynamic,
            ICompoundAssignmentOperation assignment => assignment.Target.Type?.TypeKind == TypeKind.Dynamic
                                                       || assignment.Value.Type?.TypeKind == TypeKind.Dynamic,
            IAwaitOperation awaited => awaited.Operation.Type?.TypeKind == TypeKind.Dynamic,
            IConversionOperation conversion => conversion.Operand.Type?.TypeKind == TypeKind.Dynamic
                                               && conversion.Type?.TypeKind != TypeKind.Dynamic &&
                                               conversion.Type?.SpecialType != SpecialType.System_Object,
            _ => false,
        };

        private static ISymbol? Origin(SemanticModel semantic, SyntaxNode node)
        {
            // Attributes and base lists can precede the position at which GetEnclosingSymbol enters the type.
            var declaration = node.AncestorsAndSelf().FirstOrDefault(ancestor =>
                ancestor is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
            var enclosing = semantic.GetEnclosingSymbol(node.SpanStart);
            var declared = declaration is null ? null : semantic.GetDeclaredSymbol(declaration);
            var enclosingType = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
            return declared is not null && !SymbolEqualityComparer.Default.Equals(enclosingType, declared)
                ? declared
                : enclosing ?? declared;
        }

        private void AddSymbol(Context context, ISymbol? origin, ISymbol? symbol, Location location)
        {
            if (symbol is IAliasSymbol alias)
            {
                symbol = alias.Target;
            }

            if (symbol is ITypeSymbol type)
            {
                AddType(context, origin, type, location);
                return;
            }

            AddType(context, origin, symbol?.ContainingType, location, symbol?.ToDisplayString());
            if (symbol is IMethodSymbol method)
            {
                AddType(context, origin, method.ReducedFrom?.ContainingType, location, method.ToDisplayString());
                foreach (var argument in method.TypeArguments)
                {
                    AddType(context, origin, argument, location, method.ToDisplayString());
                }
            }
        }

        private void AddType(Context context, ISymbol? origin, ITypeSymbol? symbol, Location location,
            string? targetSymbol = null)
        {
            var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            var pending = new Stack<ITypeSymbol>();
            if (symbol is not null)
            {
                pending.Push(symbol);
            }

            while (pending.TryPop(out var type))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (!visited.Add(type))
                {
                    continue;
                }

                switch (type)
                {
                    case IErrorTypeSymbol:
                        Issues.Add(new("dependency-type-unresolved",
                            $"Project '{context.Model.Id}' cannot resolve type '{type.Name}'.",
                            location: Location(context, location)));
                        break;
                    case IArrayTypeSymbol array:
                        pending.Push(array.ElementType);
                        break;
                    case IPointerTypeSymbol pointer:
                        pending.Push(pointer.PointedAtType);
                        break;
                    case IFunctionPointerTypeSymbol pointer:
                        pending.Push(pointer.Signature.ReturnType);
                        foreach (var parameter in pointer.Signature.Parameters)
                        {
                            pending.Push(parameter.Type);
                        }

                        break;
                    case INamedTypeSymbol named:
                        foreach (var argument in named.TypeArguments)
                        {
                            pending.Push(argument);
                        }

                        if (named.ContainingType is not null)
                        {
                            pending.Push(named.ContainingType);
                        }

                        if (!named.IsAnonymousType && !named.IsImplicitlyDeclared)
                        {
                            AddNamedType(context, origin, named, location, targetSymbol ?? named.ToDisplayString());
                        }

                        break;
                }
            }
        }

        private void AddNamedType(Context context, ISymbol? origin, INamedTypeSymbol type, Location location,
            string targetSymbol)
        {
            var owner = AssemblyOwner(context, type.ContainingAssembly);
            var targetAssembly =
                owner is null ? ExternalAssemblyNode(type.ContainingAssembly) : AssemblyNode(owner.Model);
            var namespaceName = RepositoryModelBuilder.NamespaceName(type.ContainingNamespace);
            DependencyNode targetType;
            DependencyNode targetNamespace;
            if (owner is not null)
            {
                var normalized =
                    RepositoryModelBuilder.NormalizeType(type, owner.Model.Id, _root, owner.GeneratedFiles);
                if (!owner.Types.ContainsKey(normalized.Id))
                {
                    Issues.Add(new("dependency-type-unmapped",
                        $"Type '{normalized.MetadataName}' has no normalized declaration in '{owner.Model.Id}'.",
                        location: Location(context, location)));
                    return;
                }

                targetType = TypeNode(normalized);
                targetNamespace = NamespaceNode(owner.Model.Assembly.Namespaces.Single(ns => ns.Name == namespaceName));
            }
            else
            {
                var names = new Stack<string>();
                for (var current = type.OriginalDefinition; current is not null; current = current.ContainingType)
                {
                    names.Push(current.MetadataName);
                }

                var metadataName = (namespaceName.Length == 0 ? "" : namespaceName + ".") + string.Join("+", names);
                targetType = new(DependencyKind.Type, targetAssembly.Id + "/" + metadataName, metadataName);
                targetNamespace = new(DependencyKind.Namespace, targetAssembly.Id + "/" + namespaceName, namespaceName);
            }

            var sourceType = origin as INamedTypeSymbol ?? origin?.ContainingType;
            var sourceNamespace = RepositoryModelBuilder.NamespaceName(sourceType?.ContainingNamespace
                                                                       ?? origin?.ContainingNamespace ??
                                                                       context.Compilation.Assembly.GlobalNamespace);
            var evidence = new DependencyEvidence(context.Model.Id, owner?.Model.Id,
                new($"'{origin?.ToDisplayString() ?? context.Model.Assembly.Name}' uses '{targetSymbol}'.",
                    Location(context, location)),
                origin?.ToDisplayString(), targetSymbol, targetAssembly: type.ContainingAssembly.Name,
                targetNamespace: namespaceName);
            Add(AssemblyNode(context.Model), targetAssembly, evidence);
            Add(NamespaceNode(context.Model.Assembly.Namespaces.Single(ns => ns.Name == sourceNamespace)),
                targetNamespace, evidence);
            if (sourceType is { IsImplicitlyDeclared: false })
            {
                var normalized =
                    RepositoryModelBuilder.NormalizeType(sourceType, context.Model.Id, _root, context.GeneratedFiles);
                if (context.Types.ContainsKey(normalized.Id))
                {
                    Add(TypeNode(normalized), targetType, evidence);
                }
                else
                {
                    Issues.Add(new("dependency-origin-unmapped",
                        $"Source type '{normalized.MetadataName}' has no normalized declaration.",
                        location: evidence.Fact.Location));
                }
            }
        }

        private Context? AssemblyOwner(Context source, IAssemblySymbol assembly)
        {
            if (SymbolEqualityComparer.Default.Equals(source.Compilation.Assembly, assembly))
            {
                return source;
            }

            var matches = _contexts.Values
                .Where(context => context.Compilation.Assembly.Identity.Equals(assembly.Identity)).ToArray();
            if (matches.Length > 1)
            {
                Issues.Add(new("dependency-assembly-ambiguous",
                    $"Assembly '{assembly.Identity}' matches multiple source projects."));
            }

            return matches.Length == 1 ? matches[0] : null;
        }

        private SourceLocation Location(Context context, Location location)
        {
            var span = location.GetLineSpan();
            var path = location.SourceTree is { } tree && context.GeneratedFiles.TryGetValue(tree, out var generated)
                ? $"generated/{context.Model.Id}/{generated.Id}/{generated.HintName}"
                : Path.GetRelativePath(_root, span.Path).Replace('\\', '/');
            return new(path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
                span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1);
        }

        private void Add(DependencyNode source, DependencyNode target, DependencyEvidence evidence)
        {
            if (source.Id != target.Id || evidence.TargetSymbol is not null &&
                source.Kind is DependencyKind.Assembly or DependencyKind.Namespace)
            {
                _edges.Add(new(source, target, [evidence]));
            }
        }

        private static DependencyNode ProjectNode(RepositoryProject project) =>
            new(DependencyKind.Project, project.Id, project.Name, project.Id);

        private static DependencyNode AssemblyNode(RepositoryProject project) => new(DependencyKind.Assembly,
            project.Assembly.Id, project.Assembly.Name, project.Id);

        private static DependencyNode NamespaceNode(RepositoryNamespace ns) =>
            new(DependencyKind.Namespace, ns.Id, ns.Name, ns.ProjectPath);

        private static DependencyNode TypeNode(RepositoryType type) =>
            new(DependencyKind.Type, type.Id, type.MetadataName, type.ProjectPath);

        private static DependencyNode ExternalAssemblyNode(IAssemblySymbol assembly) => new(DependencyKind.Assembly,
            "external:" + assembly.Identity, assembly.Identity.ToString());

        /// <summary>Pairs one compiler snapshot with its normalized declaration identities.</summary>
        private sealed class Context(
            Project project,
            RepositoryProject model,
            Compilation compilation,
            IReadOnlyDictionary<SyntaxTree, RepositoryGeneratedFile> generatedFiles)
        {
            internal Project Project { get; } = project;
            internal RepositoryProject Model { get; } = model;
            internal Compilation Compilation { get; } = compilation;
            internal IReadOnlyDictionary<SyntaxTree, RepositoryGeneratedFile> GeneratedFiles { get; } = generatedFiles;

            internal Dictionary<string, RepositoryType> Types { get; } = model.Assembly.Namespaces
                .SelectMany(ns => ns.Types)
                .ToDictionary(type => type.Id, StringComparer.Ordinal);
        }
    }
}
