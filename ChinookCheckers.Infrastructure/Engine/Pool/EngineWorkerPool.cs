using System.Threading.Channels;
using ChinookCheckers.Application.Exceptions;
using ChinookCheckers.Application.Interfaces;
using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChinookCheckers.Infrastructure.Engine.Pool;

/// <summary>
/// Fixed-size pool of long-lived engine workers. Free workers sit in a bounded channel: renting a worker
/// means reading it from the channel, so a worker is owned by at most one request at any moment, and
/// waiting requests are served in FIFO order by whichever worker frees up first.
/// </summary>
internal sealed class EngineWorkerPool(
    IEngineAdapterFactory adapterFactory,
    IOptions<EngineOptions> options,
    ILogger<EngineWorkerPool> logger) : ICheckersEnginePool, IDisposable
{
    private readonly int _size = options.Value.Workers;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly Channel<EngineWorker> _idleWorkers = Channel.CreateBounded<EngineWorker>(
        new BoundedChannelOptions(options.Value.Workers)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });

    private EngineWorker[] _workers = [];
    private volatile bool _isReady;
    private volatile int _tablebaseMaxPieces;
    private int _disposed;

    public bool IsReady => _isReady;

    public int WorkerCount => _isReady ? _workers.Length : 0;

    public int TablebaseMaxPieces => _tablebaseMaxPieces;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        await _initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isReady)
            {
                return;
            }

            var workers = Enumerable.Range(0, _size)
                .Select(id => new EngineWorker(adapterFactory.Create(id), logger))
                .ToArray();

            try
            {
                await Task.WhenAll(workers.Select(worker => worker.InitializeAsync()))
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                foreach (var worker in workers)
                {
                    worker.Dispose();
                }

                throw;
            }

            foreach (var worker in workers)
            {
                _idleWorkers.Writer.TryWrite(worker);
            }

            _workers = workers;
            _tablebaseMaxPieces = workers.Min(worker => worker.TablebaseMaxPieces);
            _isReady = true;
            logger.LogInformation(
                "Engine pool ready with {WorkerCount} workers, tablebase up to {TablebaseMaxPieces} pieces",
                workers.Length,
                _tablebaseMaxPieces);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<EngineSearchResult> SearchAsync(
        Position position,
        EngineSearchLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(limits);

        if (!_isReady)
        {
            throw new EngineUnavailableException("Engine pool is not initialized yet.");
        }

        EngineWorker worker;
        try
        {
            worker = await _idleWorkers.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex)
        {
            throw new EngineUnavailableException("Engine pool is shutting down.", ex);
        }

        Task<EngineSearchResult> search;
        try
        {
            search = worker.SearchAsync(position, limits, cancellationToken);
        }
        catch
        {
            Release(worker);
            throw;
        }

        // The worker goes back to the pool only when the native call has really returned, even if the
        // caller already gave up on the result; otherwise two searches could hit one engine at once.
        _ = search.ContinueWith(
            static (task, state) =>
            {
                var (pool, rented) = ((EngineWorkerPool, EngineWorker))state!;
                if (task.IsFaulted)
                {
                    pool.LogWorkerFault(rented, task.Exception!);
                }

                pool.Release(rented);
            },
            (this, worker),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return await search.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _isReady = false;
        _idleWorkers.Writer.TryComplete();
        foreach (var worker in _workers)
        {
            worker.Dispose();
        }

        _initializationLock.Dispose();
    }

    private void Release(EngineWorker worker)
    {
        if (!_idleWorkers.Writer.TryWrite(worker))
        {
            logger.LogDebug("Engine worker {WorkerId} not returned to the pool: pool is shut down", worker.Id);
        }
    }

    private void LogWorkerFault(EngineWorker worker, AggregateException exception) =>
        logger.LogError(exception.GetBaseException(), "Engine worker {WorkerId} search failed", worker.Id);
}
