namespace ChinookCheckers.Core.Models;

/// <param name="SoftTimeMs">Thinking time handed to the engine; the engine stops on its own after it.</param>
/// <param name="MaxDepth">Requested nominal depth. Informational: the CheckerBoard API has no depth limit parameter.</param>
/// <param name="ProbeTablebase">True when the position is small enough to be resolved by the endgame databases.</param>
public sealed record EngineSearchLimits(int SoftTimeMs, int? MaxDepth, bool ProbeTablebase);
