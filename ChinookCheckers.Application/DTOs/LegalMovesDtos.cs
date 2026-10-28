using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ChinookCheckers.Core.Models;

namespace ChinookCheckers.Application.DTOs;

public sealed record LegalMovesRequest
{
    /// <summary>PDN FEN position, e.g. "B:W21-32:B1-12".</summary>
    [Required]
    [JsonPropertyName("position")]
    public required string Position { get; init; }
}

/// <param name="SideToMove">Side that has to move in the requested position.</param>
/// <param name="Winner">Set only when the side to move has no legal moves (and therefore lost).</param>
/// <param name="Moves">All legal moves; captures are mandatory, so they are the only moves when available.</param>
public sealed record LegalMovesResponse(
    [property: JsonPropertyName("sideToMove")] PieceColor SideToMove,
    [property: JsonPropertyName("winner")] PieceColor? Winner,
    [property: JsonPropertyName("moves")] IReadOnlyList<LegalMoveDto> Moves);

/// <param name="Notation">Canonical notation, e.g. "9-13" or "9x18x27".</param>
/// <param name="Path">Origin square followed by every landing square.</param>
/// <param name="Captured">Squares of the captured pieces (empty for simple moves).</param>
/// <param name="ResultingPosition">Canonical PDN FEN after the move has been played.</param>
public sealed record LegalMoveDto(
    [property: JsonPropertyName("notation")] string Notation,
    [property: JsonPropertyName("path")] IReadOnlyList<int> Path,
    [property: JsonPropertyName("captured")] IReadOnlyList<int> Captured,
    [property: JsonPropertyName("resultingPosition")] string ResultingPosition);
