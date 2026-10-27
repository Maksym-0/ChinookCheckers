using ChinookCheckers.Core.Models;

namespace ChinookCheckers.Application.Interfaces;

/// <summary>
/// A fixed set of long-lived engine workers. Each request is routed to a free worker; callers never
/// touch an engine instance directly.
/// </summary>
public interface ICheckersEnginePool
{
    bool IsReady { get; }

    int WorkerCount { get; }

    /// <summary>Positions with at most this many pieces are resolved from the endgame database; 0 = no database.</summary>
    int TablebaseMaxPieces { get; }

    /// <summary>
    /// Waits for a free worker and runs a search on it. Cancelling <paramref name="cancellationToken"/>
    /// interrupts both the wait and a running native search.
    /// </summary>
    Task<EngineSearchResult> SearchAsync(
        Position position,
        EngineSearchLimits limits,
        CancellationToken cancellationToken);
}
