using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ChinookCheckers.Core.Models;

namespace ChinookCheckers.Application.DTOs;

public sealed record SuggestMoveRequest
{
    public const string SupportedGameId = "checkers-8x8";

    [Required]
    [JsonPropertyName("gameId")]
    public required string GameId { get; init; }

    [Required]
    [JsonPropertyName("state")]
    public required GameStateDto State { get; init; }

    /// <summary>"weak" | "medium" | "strong". When omitted the default limits from configuration apply.</summary>
    [JsonPropertyName("level")]
    public StrengthLevel? Level { get; init; }

    [JsonPropertyName("limits")]
    public SearchLimitsDto? Limits { get; init; }
}

public sealed record GameStateDto
{
    public const string PdnNotation = "PDN";

    [Required]
    [JsonPropertyName("notation")]
    public required string Notation { get; init; }

    [Required]
    [JsonPropertyName("position")]
    public required string Position { get; init; }
}

public sealed record SearchLimitsDto
{
    [Range(1, 64)]
    [JsonPropertyName("maxDepth")]
    public int? MaxDepth { get; init; }

    [Range(1, 600_000)]
    [JsonPropertyName("softTimeMs")]
    public int? SoftTimeMs { get; init; }

    [Range(1, 600_000)]
    [JsonPropertyName("hardTimeMs")]
    public int? HardTimeMs { get; init; }
}
