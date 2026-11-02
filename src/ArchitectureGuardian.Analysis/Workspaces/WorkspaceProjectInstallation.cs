namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Retains evaluated installation facts from the loader's existing inner-build validation.</summary>
internal sealed record WorkspaceProjectInstallation(string AssetsPath, string BuildPropsPath, string PackagesPropsPath,
    bool CentralPackages, int AnalyzerReferences, string AnalyzerVersion, string PrivateAssets,
    int CentralAnalyzerVersions, string RunAnalyzers, string RunAnalyzersDuringBuild);
