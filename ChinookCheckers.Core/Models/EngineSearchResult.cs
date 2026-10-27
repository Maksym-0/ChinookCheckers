namespace ChinookCheckers.Core.Models;

/// <summary>
/// Raw engine output. The move is not trusted yet: it is verified against the rules by the application layer.
/// </summary>
/// <param name="ResultingPosition">Board returned by the engine after it played its move (null if unavailable).</param>
/// <param name="BestMoveNotation">Best move as reported in the engine info string.</param>
/// <param name="PrincipalVariation">PV as reported by the engine, unverified.</param>
/// <param name="Score">Search evaluation from the side to move perspective.</param>
/// <param name="Wdl">1 = win, 0 = draw, -1 = loss when the result is proven (tablebase); otherwise null.</param>
public sealed record EngineSearchResult(
    Position? ResultingPosition,
    string? BestMoveNotation,
    IReadOnlyList<string> PrincipalVariation,
    int Score,
    int? Wdl,
    int Depth,
    long Nodes,
    bool TablebaseHit,
    TimeSpan Elapsed);
