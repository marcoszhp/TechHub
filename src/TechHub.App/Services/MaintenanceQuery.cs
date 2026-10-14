namespace TechHub.App.Services;

internal sealed class MaintenanceQuery
{
    private readonly object _sync = new();
    private readonly List<Task> _pending = [];
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    internal static TimeSpan ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        return timeout;
    }

    // Only read-only queries use this boundary. An uncooperative backend may finish
    // later, but its result cannot reach the caller and its fault is still observed.
    internal async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> query,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        CancellationTokenSource backendCancellation;
        Task<T> pending;
        lock (_sync)
        {
            _pending.RemoveAll(task => task.IsCompleted);
            if (_pending.Count >= 2)
                throw new InvalidOperationException("Consultas anteriores ainda estão encerrando; aguarde antes de tentar novamente.");
            cancellationToken.ThrowIfCancellationRequested();
            backendCancellation = new CancellationTokenSource();
            // The backend can block even before returning its Task (PowerShell/WMI).
            pending = Task.Run(() => query(backendCancellation.Token), backendCancellation.Token);
            _pending.Add(pending);
        }
        Task cancellation = Task.CompletedTask;
        try
        {
            // Wait independently of backend callbacks: shell.Stop() may block.
            var result = await pending.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (TimeoutException)
        {
            cancellation = backendCancellation.CancelAsync();
            throw new TimeoutException("A consulta excedeu o tempo limite.");
        }
        catch (OperationCanceledException)
        {
            cancellation = backendCancellation.CancelAsync();
            throw;
        }
        finally
        {
            // Permit one immediate retry, but bound abandoned backends plus their
            // cancellation callbacks to two operations per VM, including Stop().
            lock (_sync)
            {
                _pending.Remove(pending);
                _pending.Add(ObserveAndDisposeAsync(pending, cancellation, backendCancellation));
            }
        }
    }

    internal static void RequestCancellation(CancellationTokenSource? source)
    {
        if (source is not null) _ = ObserveCancellationAsync(source.CancelAsync());
    }

    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (Exception exception)
        {
            Serilog.Log.Warning("Falha em callback de cancelamento de consulta: {ExceptionType}", exception.GetType().Name);
        }
    }

    private static async Task ObserveAndDisposeAsync(Task pending, Task cancellation, CancellationTokenSource source)
    {
        try
        {
            await Task.WhenAll(ObserveBackendAsync(pending), ObserveCancellationAsync(cancellation)).ConfigureAwait(false);
        }
        finally { source.Dispose(); }
    }

    private static async Task ObserveBackendAsync(Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            // Do not log backend messages, command text or inventory data.
            Serilog.Log.Warning("Falha em consulta de manutenção: {ExceptionType}", exception.GetType().Name);
        }
    }
}
