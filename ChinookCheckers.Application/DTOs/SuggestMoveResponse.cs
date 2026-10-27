using System.Text.Json.Serialization;

namespace ChinookCheckers.Application.DTOs;

public sealed record SuggestMoveResponse(
    [property: JsonPropertyName("engine")] string Engine,
    [property: JsonPropertyName("bestMove")] string BestMove,
    [property: JsonPropertyName("pv")] IReadOnlyList<string> Pv,
    [property: JsonPropertyName("scoreOrWDL")] int ScoreOrWdl,
    [property: JsonPropertyName("depth")] int Depth,
    [property: JsonPropertyName("nodes")] long Nodes,
    [property: JsonPropertyName("positionKey")] string PositionKey,
    [property: JsonPropertyName("info")] SearchInfoDto Info);

public sealed record SearchInfoDto(
    [property: JsonPropertyName("tablebaseHit")] bool TablebaseHit,
    [property: JsonPropertyName("timeMs")] long TimeMs);
