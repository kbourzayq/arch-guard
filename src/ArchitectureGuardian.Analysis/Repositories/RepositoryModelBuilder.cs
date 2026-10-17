using System.Globalization;
using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Repositories;
using Microsoft.CodeAnalysis;

namespace ArchitectureGuardian.Analysis.Repositories;

/// <summary>Converts a loaded workspace into immutable Core data without retaining compiler or workspace objects.</summary>
public sealed class RepositoryModelBuilder
{
    /// <summary>Normalizes all available source projects and resolves policy ownership without improving loading coverage.</summary>
    /// <param name="workspace">The owned workspace result, which must remain undisposed until this call completes.</param>
    /// <param name="policyDirectory">The base directory for policy selectors and every project/input identity.</param>
    /// <param name="policy">The validated architecture policy.</param>
    /// <param name="cancellationToken">Cooperative cancellation during project and declaration traversal.</param>
    /// <returns>A neutral model and aggregate coverage; invalid mappings or paths produce explanatory incomplete results.</returns>
    /// <exception cref="ArgumentException">The policy directory is blank.</exception>
    /// <exception cref="ArgumentNullException">A required input is null.</exception>
    public async Task<RepositoryBuildResult> BuildAsync(WorkspaceLoadResult workspace, string policyDirectory,
        ArchitecturePolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyDirectory);
        var root = Path.GetFullPath(policyDirectory);
        var issues = workspace.Issues.ToList();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (workspace.Status == AnalysisStatus.Cancelled)
            {
                return new(null, AnalysisStatus.Cancelled, issues);
            }

            if (workspace.Solution is not { } solution)
            {
                issues.Add(new("repository-workspace-unavailable", "Repository normalization requires an available workspace."));
                return new(null, workspace.Status == AnalysisStatus.Failed ? AnalysisStatus.Failed : AnalysisStatus.Incomplete, issues);
            }

            var inputPath = RelativePath(root, workspace.InputPath);
            var projects = new List<RepositoryProject>();
            foreach (var project in solution.Projects.OrderBy(project => project.FilePath, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (project.FilePath is null || project.Language != LanguageNames.CSharp)
                {
                    issues.Add(new("repository-project-unsupported", $"Project '{project.Name}' has no supported C# project path."));
                    continue;
                }

                var projectPath = RelativePath(root, project.FilePath);
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
                if (compilation is null)
                {
                    issues.Add(new("repository-compilation-unavailable", $"Project '{projectPath}' has no compilation to normalize."));
                    continue;
                }

                var generatedFiles = await GeneratedFilesAsync(project, cancellationToken).ConfigureAwait(false);
                var namespaces = new List<RepositoryNamespace>();
                AddNamespace(compilation.Assembly.GlobalNamespace, "", projectPath, root, generatedFiles, namespaces, cancellationToken);
                projects.Add(new(projectPath, project.Name, new(projectPath, compilation.Assembly.Name, namespaces)));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var model = new RepositoryModel(inputPath, policy, projects);
            cancellationToken.ThrowIfCancellationRequested();
            var status = workspace.Status == AnalysisStatus.Failed ? AnalysisStatus.Failed
                : issues.Count == 0 ? AnalysisStatus.Completed : AnalysisStatus.Incomplete;
            return new(model, status, issues);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(null, AnalysisStatus.Cancelled, issues);
        }
        catch (ArgumentException exception)
        {
            issues.Add(new("repository-mapping-invalid", exception.Message));
            return new(null, workspace.Status == AnalysisStatus.Failed ? AnalysisStatus.Failed : AnalysisStatus.Incomplete, issues);
        }
        catch (Exception exception)
        {
            issues.Add(new("repository-normalization-failed", $"{exception.GetType().Name}: {exception.Message}"));
            return new(null, AnalysisStatus.Failed, issues);
        }
    }

    private static void AddNamespace(INamespaceSymbol symbol, string name, string projectPath, string root,
        IReadOnlyDictionary<SyntaxTree, RepositoryGeneratedFile> generatedFiles, List<RepositoryNamespace> namespaces,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var types = new List<RepositoryType>();
        foreach (var type in symbol.GetTypeMembers())
        {
            AddType(type, projectPath, name, null, null, null, true, root, generatedFiles, types, cancellationToken);
        }

        namespaces.Add(new(projectPath, name, types));
        foreach (var child in symbol.GetNamespaceMembers())
        {
            var childName = name.Length == 0 ? child.Name : name + "." + child.Name;
            AddNamespace(child, childName, projectPath, root, generatedFiles, namespaces, cancellationToken);
        }
    }

    private static void AddType(INamedTypeSymbol symbol, string projectPath, string namespaceName, string? containingName,
        string? declaringFile, RepositoryGeneratedFile? generatedFile, bool containingPublic, string root,
        IReadOnlyDictionary<SyntaxTree, RepositoryGeneratedFile> generatedFiles, List<RepositoryType> types,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (symbol.IsImplicitlyDeclared)
        {
            return;
        }

        // Build the logical metadata name rather than persisting Roslyn's path-dependent file-local prefix.
        var name = symbol.Name + (symbol.Arity == 0 ? "" : "`" + symbol.Arity.ToString(CultureInfo.InvariantCulture));
        var qualifiedName = containingName is not null ? containingName + "+" + name
            : namespaceName.Length == 0 ? name : namespaceName + "." + name;
        if (symbol.IsFileLocal)
        {
            var tree = symbol.DeclaringSyntaxReferences.First().SyntaxTree;
            generatedFiles.TryGetValue(tree, out generatedFile);
            declaringFile = generatedFile is null ? RelativePath(root, tree.FilePath) : null;
        }

        var isPublic = containingPublic && symbol.DeclaredAccessibility == Accessibility.Public;
        var kind = symbol.TypeKind switch
        {
            TypeKind.Class => RepositoryTypeKind.Class,
            TypeKind.Struct => RepositoryTypeKind.Struct,
            TypeKind.Interface => RepositoryTypeKind.Interface,
            TypeKind.Enum => RepositoryTypeKind.Enum,
            TypeKind.Delegate => RepositoryTypeKind.Delegate,
            _ => throw new ArgumentException($"Unsupported source type '{qualifiedName}'."),
        };
        types.Add(new(projectPath, namespaceName, qualifiedName, kind, isPublic, declaringFile, generatedFile));
        foreach (var nested in symbol.GetTypeMembers())
        {
            AddType(nested, projectPath, namespaceName, qualifiedName, declaringFile, generatedFile, isPublic, root,
                generatedFiles, types, cancellationToken);
        }
    }

    private static async Task<IReadOnlyDictionary<SyntaxTree, RepositoryGeneratedFile>> GeneratedFilesAsync(Project project,
        CancellationToken cancellationToken)
    {
        var generators = project.AnalyzerReferences.SelectMany(reference => reference.GetGenerators(project.Language))
            .Select(generator => generator.GetGeneratorType())
            .Select(type => (Assembly: type.Assembly.GetName().Name
                    ?? throw new ArgumentException("A source generator must have an assembly name."),
                Type: type.FullName ?? throw new ArgumentException("A source generator must have a full type name.")))
            .Distinct().ToArray();
        var files = new Dictionary<SyntaxTree, RepositoryGeneratedFile>();
        foreach (var document in await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hint = document.HintName.Replace('\\', '/');
            var path = document.FilePath?.Replace('\\', '/');
            // Roslyn 5.0 keeps document generator identity internal. Match its logical assembly/type/hint
            // suffix to registered generators, discarding the configuration-dependent output prefix entirely.
            var matches = generators.Where(generator => path is not null &&
                (path == $"{generator.Assembly}/{generator.Type}/{hint}"
                    || path.EndsWith($"/{generator.Assembly}/{generator.Type}/{hint}", StringComparison.Ordinal))).ToArray();
            if (matches.Length != 1)
            {
                throw new ArgumentException($"Generated hint '{hint}' must resolve to exactly one registered generator.");
            }

            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new ArgumentException($"Generated hint '{hint}' has no syntax tree.");
            files.Add(tree, new(matches[0].Assembly, matches[0].Type, hint));
        }

        return files;
    }

    private static string RelativePath(string root, string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"Workspace path '{path}' must be absolute before normalization.");
        }

        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        try
        {
            return ProjectPattern.NormalizeIdentity(relative);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException($"Path '{relative}' cannot be represented relative to the policy directory. Place the policy above every input, project and authored file-local source file. {exception.Message}", exception);
        }
    }
}
