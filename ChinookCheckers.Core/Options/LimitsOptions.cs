using System.ComponentModel.DataAnnotations;

namespace ChinookCheckers.Core.Options;

/// <summary>Binds the "Limits" section of appsettings.json.</summary>
public sealed class LimitsOptions
{
    public const string SectionName = "Limits";

    [Range(1, 60_000)]
    public int DefaultSoftTimeMs { get; init; } = 300;

    [Range(1, 60_000)]
    public int DefaultHardTimeMs { get; init; } = 1200;

    /// <summary>Upper bound for client-supplied hardTimeMs, protects workers from being monopolised.</summary>
    [Range(1, 600_000)]
    public int MaxHardTimeMs { get; init; } = 10_000;

    /// <summary>Soft time is capped at hardTimeMs minus this margin so the engine answers before the deadline.</summary>
    [Range(0, 10_000)]
    public int HardTimeSafetyMarginMs { get; init; } = 100;

    [Range(1, 60_000)]
    public int TablebaseTimeMs { get; init; } = 30;

    public LevelLimits Weak { get; init; } = new() { TimeMs = 100, Depth = 8 };

    public LevelLimits Medium { get; init; } = new() { TimeMs = 250, Depth = 12 };

    public LevelLimits Strong { get; init; } = new() { TimeMs = 500, Depth = 18 };
}

public sealed class LevelLimits
{
    [Range(1, 60_000)]
    public int TimeMs { get; init; }

    [Range(1, 64)]
    public int Depth { get; init; }
}
