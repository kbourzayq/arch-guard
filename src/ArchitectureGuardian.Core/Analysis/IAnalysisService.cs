namespace ArchitectureGuardian.Core.Analysis;

/// <summary>Defines the analysis use case that console and protocol hosts can invoke over neutral data.</summary>
/// <typeparam name="TContext">The prepared input model shared by the service's rules.</typeparam>
public interface IAnalysisService<TContext> where TContext : notnull
{
    /// <summary>Evaluates the request and applies severity policy while preserving explicit coverage outcomes.</summary>
    /// <param name="request">The stable context and reporting policy for the run.</param>
    /// <param name="cancellationToken">A token requesting cooperative cancellation of the run.</param>
    /// <returns>A result containing coverage status, available diagnostics and operational issues.</returns>
    /// <remarks>
    /// Requested cancellation is represented by <see cref="AnalysisStatus.Cancelled"/> rather than
    /// a clean result. Callers must inspect coverage before making a quality-gate decision.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    Task<AnalysisResult> AnalyzeAsync(AnalysisRequest<TContext> request, CancellationToken cancellationToken = default);
}
