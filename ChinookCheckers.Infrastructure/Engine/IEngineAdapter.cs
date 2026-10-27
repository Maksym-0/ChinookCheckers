using ChinookCheckers.Core.Models;

namespace ChinookCheckers.Infrastructure.Engine;

/// <summary>
/// One stateful engine instance. Not thread-safe: the pool guarantees a single caller at a time,
/// except for <see cref="RequestStop"/> which may be called from any thread.
/// </summary>
public interface IEngineAdapter : IDisposable
{
    int WorkerId { get; }

    /// <summary>Largest piece count resolved from the endgame database; 0 when probing is unavailable.</summary>
    int TablebaseMaxPieces { get; }

    void Initialize();

    void WarmUp();

    void SetPosition(Position position);

    /// <summary>Blocking search. Cancellation interrupts the native search and throws <see cref="OperationCanceledException"/>.</summary>
    EngineSearchResult Search(EngineSearchLimits limits, CancellationToken cancellationToken);

    void RequestStop();
}

public interface IEngineAdapterFactory
{
    IEngineAdapter Create(int workerId);
}
