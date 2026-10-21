using ModelContextProtocol;

namespace ArchitectureGuardian.Mcp.Requests;

/// <summary>Bounds application work without blocking the SDK's cancellation and lifecycle message processing.</summary>
internal sealed class RequestExecutor(TimeSpan timeout) : IDisposable
{
    private readonly SemaphoreSlim _slot = new(1, 1);

    internal async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await _slot.WaitAsync(0, cancellationToken))
        {
            throw new McpException("request_busy: one application request is already running; retry after it finishes.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            var result = await action(deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new McpException("request_timeout: the application request exceeded its deadline.");
        }
        finally
        {
            _slot.Release();
        }
    }

    public void Dispose() => _slot.Dispose();
}
