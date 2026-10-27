using System.ComponentModel.DataAnnotations;

namespace ChinookCheckers.Core.Options;

/// <summary>Binds the "Cache" section of appsettings.json.</summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Maximum number of cached positions; least recently used entries are evicted first.</summary>
    [Range(1, 10_000_000)]
    public int Capacity { get; init; } = 20_000;

    [Range(1, 24 * 60)]
    public int TtlMinutes { get; init; } = 15;
}
