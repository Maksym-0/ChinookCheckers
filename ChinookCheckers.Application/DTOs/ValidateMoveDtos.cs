using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ChinookCheckers.Application.DTOs;

public sealed record ValidateMoveRequest
{
    /// <summary>PDN FEN position, e.g. "B:W18,19,22:B1,5,6".</summary>
    [Required]
    [JsonPropertyName("position")]
    public required string Position { get; init; }

    /// <summary>Move in PDN notation, e.g. "22-18" or "18x11".</summary>
    [Required]
    [JsonPropertyName("move")]
    public required string Move { get; init; }
}

public sealed record ValidateMoveResponse([property: JsonPropertyName("legal")] bool Legal);
