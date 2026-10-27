using Microsoft.Extensions.Hosting;

namespace ChinookCheckers.Infrastructure.Engine.Pool;

/// <summary>
/// Creates and warms up all workers before the server starts accepting requests,
/// so /healthz reports ok as soon as the application is reachable.
/// </summary>
internal sealed class EngineWorkerPoolInitializer(EngineWorkerPool pool) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => pool.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        pool.Dispose();
        return Task.CompletedTask;
    }
}
