using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;

namespace ArchitectureGuardian.Analysis.Doctor;

/// <summary>Snapshots diagnostic coverage and checks without asserting architecture compliance.</summary>
public sealed class DoctorResult
{
    internal DoctorResult(WorkspaceLoadRequest request, string policyPath, string workflow, AnalysisStatus status,
        AnalysisStatus? workspaceStatus, IEnumerable<DoctorCheck> checks, IEnumerable<WorkspaceProjectContext> contexts)
    {
        Request = request;
        PolicyPath = policyPath;
        Workflow = workflow;
        Status = status;
        WorkspaceStatus = workspaceStatus;
        Checks = Array.AsReadOnly(checks.OrderBy(check => check.Id, StringComparer.Ordinal).ThenBy(check => check.Scope, StringComparer.Ordinal)
            .ThenBy(check => check.Message, StringComparer.Ordinal).ToArray());
        Contexts = Array.AsReadOnly(contexts.OrderBy(context => context.ProjectPath, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Gets the explicit input and requested trust/configuration/framework.</summary>
    public WorkspaceLoadRequest Request { get; }
    /// <summary>Gets the explicit absolute policy path.</summary>
    public string PolicyPath { get; }
    /// <summary>Gets the selected scan, compiler, mcp or ci workflow.</summary>
    public string Workflow { get; }
    /// <summary>Gets aggregate diagnostic coverage; skipped required checks cannot establish health.</summary>
    public AnalysisStatus Status { get; }
    /// <summary>Gets original workspace coverage, or null when evaluation did not run.</summary>
    public AnalysisStatus? WorkspaceStatus { get; }
    /// <summary>Gets checks in deterministic identifier/scope/message order.</summary>
    public IReadOnlyList<DoctorCheck> Checks { get; }
    /// <summary>Gets actual evaluated contexts, including partial work.</summary>
    public IReadOnlyList<WorkspaceProjectContext> Contexts { get; }
    /// <summary>Gets zero for completed health without required blockers, otherwise two; never an architecture-gate exit code.</summary>
    public int ExitCode => Status == AnalysisStatus.Completed && !Checks.Any(check => check.Required && check.Status is "blocked" or "notRun") ? 0 : 2;
}
