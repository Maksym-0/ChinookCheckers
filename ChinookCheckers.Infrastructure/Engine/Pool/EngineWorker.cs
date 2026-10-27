using System.Collections.Concurrent;
using ChinookCheckers.Core.Models;
using Microsoft.Extensions.Logging;

namespace ChinookCheckers.Infrastructure.Engine.Pool;

/// <summary>
/// Owns one engine adapter and a dedicated OS thread. Every native call (load, configure, search)
/// runs on that thread, so the engine always sees the same thread and thread-pool threads are never
/// blocked by a long native search.
/// </summary>
internal sealed class EngineWorker : IDisposable
{
    // Native alpha-beta recursion needs more than the 256 KB default stack of IIS worker threads.
    private const int ThreadStackSize = 16 * 1024 * 1024;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly IEngineAdapter _adapter;
    private readonly ILogger _logger;
    private readonly BlockingCollection<Action> _jobs = new(boundedCapacity: 1);
    private readonly Thread _thread;
    private int _disposed;

    public EngineWorker(IEngineAdapter adapter, ILogger logger)
    {
        _adapter = adapter;
        _logger = logger;
        _thread = new Thread(ProcessJobs, ThreadStackSize)
        {
            IsBackground = true,
            Name = $"engine-worker-{adapter.WorkerId}"
        };
        _thread.Start();
    }

    public int Id => _adapter.WorkerId;

    public int TablebaseMaxPieces => _adapter.TablebaseMaxPieces;

    public Task InitializeAsync() => InvokeAsync(adapter =>
    {
        adapter.Initialize();
        adapter.WarmUp();
        return true;
    });

    public Task<EngineSearchResult> SearchAsync(
        Position position,
        EngineSearchLimits limits,
        CancellationToken cancellationToken) => InvokeAsync(adapter =>
    {
        adapter.SetPosition(position);
        return adapter.Search(limits, cancellationToken);
    });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _adapter.RequestStop();
        _jobs.CompleteAdding();
        if (!_thread.Join(ShutdownTimeout))
        {
            _logger.LogWarning("Engine worker {WorkerId} did not stop within {Timeout}", Id, ShutdownTimeout);
        }
    }

    private Task<T> InvokeAsync<T>(Func<IEngineAdapter, T> work)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_jobs.TryAdd(() => Execute(work, completion)))
        {
            // The pool hands a worker to exactly one caller, so a busy queue means the invariant is broken.
            throw new InvalidOperationException($"Engine worker {Id} is already busy.");
        }

        return completion.Task;
    }

    private void Execute<T>(Func<IEngineAdapter, T> work, TaskCompletionSource<T> completion)
    {
        try
        {
            completion.TrySetResult(work(_adapter));
        }
        catch (OperationCanceledException ex)
        {
            completion.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private void ProcessJobs()
    {
        try
        {
            foreach (var job in _jobs.GetConsumingEnumerable())
            {
                job();
            }
        }
        finally
        {
            _adapter.Dispose();
            _jobs.Dispose();
        }
    }
}
