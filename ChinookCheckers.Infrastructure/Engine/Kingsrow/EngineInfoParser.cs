using System.Globalization;
using System.Text.RegularExpressions;

namespace ChinookCheckers.Infrastructure.Engine.Kingsrow;

/// <summary>
/// Parses the free-form KingsRow status line. Two shapes are observed:
/// <c>value=10,  depth 17/16.3/32,  0.2s,  6531 kN/s,  pv 22-17 8-12 17-14</c> and the
/// per-root-move score list <c>11-16(-1/32)*  11-15(1/25)*  ...</c> (best move first, "(value/depth)").
/// Every field is optional: the move itself is taken from the returned board, not from this text.
/// </summary>
internal static partial class EngineInfoParser
{
    /// <param name="elapsed">Measured search time; used to derive nodes from "kN/s" when no node count is printed.</param>
    public static EngineInfo Parse(string? text, TimeSpan elapsed)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return EngineInfo.Empty;
        }

        var pv = PvPattern().Match(text) is { Success: true } pvMatch
            ? MoveTokenPattern().Matches(pvMatch.Groups["moves"].Value).Select(match => match.Value).ToArray()
            : [];
        var scoredMove = ScoredMovePattern().Match(text);

        return new EngineInfo(
            BestMove: BestPattern().Match(text) is { Success: true } best
                ? best.Groups["move"].Value
                : scoredMove.Success ? scoredMove.Groups["move"].Value : pv.FirstOrDefault(),
            Score: ParseInt(ValuePattern().Match(text), "value") ?? ParseInt(scoredMove, "value") ?? 0,
            Depth: ParseInt(DepthPattern().Match(text), "depth") ?? ParseInt(scoredMove, "depth") ?? 0,
            Nodes: ParseNodes(text, elapsed),
            PrincipalVariation: pv,
            IsDrawMoveList: DrawMoveListPattern().IsMatch(text));
    }

    private static long ParseNodes(string text, TimeSpan elapsed)
    {
        if (NodesPattern().Match(text) is { Success: true } nodes
            && long.TryParse(nodes.Groups["nodes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            return count;
        }

        return SpeedPattern().Match(text) is { Success: true } speed
            && double.TryParse(speed.Groups["speed"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kiloNodesPerSecond)
                ? (long)(kiloNodesPerSecond * 1000 * elapsed.TotalSeconds)
                : 0;
    }

    private static int? ParseInt(Match match, string group) =>
        match.Success && int.TryParse(match.Groups[group].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// <c>depth 6; 27-23* (0.180), 19-16* (0.180), 27-24 (0.225), ...</c>: the list of equally drawing moves.
    /// Observed only with the WLD database enabled (39 of 99 searches vs. 0 of 41 without it), always together
    /// with return code UNKNOWN. This is how KingsRow reports a position that the database proves drawn.
    /// </summary>
    [GeneratedRegex(@"^\s*depth\s+\d+\s*;", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DrawMoveListPattern();

    [GeneratedRegex(@"\bbest\s+(?<move>\d+(?:[-x]\d+)+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BestPattern();

    [GeneratedRegex(@"^\s*(?<move>\d+(?:[-x]\d+)+)\((?<value>[-+]?\d+)/(?<depth>\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ScoredMovePattern();

    [GeneratedRegex(@"\bvalue\s*[=:]?\s*(?<value>[-+]?\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ValuePattern();

    [GeneratedRegex(@"\bdepth\s+(?<depth>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DepthPattern();

    [GeneratedRegex(@"\bnodes\s+(?<nodes>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NodesPattern();

    [GeneratedRegex(@"(?<speed>\d+(?:\.\d+)?)\s*kN/s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpeedPattern();

    [GeneratedRegex(@"\bpv\s+(?<moves>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PvPattern();

    [GeneratedRegex(@"\d+(?:[-x]\d+)+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MoveTokenPattern();
}

internal sealed record EngineInfo(
    string? BestMove,
    int Score,
    int Depth,
    long Nodes,
    IReadOnlyList<string> PrincipalVariation,
    bool IsDrawMoveList = false)
{
    public static EngineInfo Empty { get; } = new(null, 0, 0, 0, []);
}
