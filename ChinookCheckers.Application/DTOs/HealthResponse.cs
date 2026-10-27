using System.Text.Json.Serialization;

namespace ChinookCheckers.Application.DTOs;

public sealed record HealthResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("workers")] int Workers);
