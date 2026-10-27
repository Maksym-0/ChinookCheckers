using System.ComponentModel.DataAnnotations;

namespace ChinookCheckers.Core.Options;

/// <summary>Binds the "Engine" section of appsettings.json.</summary>
public sealed class EngineOptions
{
    public const string SectionName = "Engine";
    public const string KingsrowDllType = "kingsrow_dll";

    [Required]
    public string Type { get; init; } = KingsrowDllType;

    /// <summary>Engine name reported in API responses.</summary>
    [Required]
    public string Name { get; init; } = "chinook";

    /// <summary>Path to Kingsrow64.dll; relative paths are resolved against the application base directory.</summary>
    [Required]
    public string Path { get; init; } = string.Empty;

    [Range(1, 64)]
    public int Workers { get; init; } = 2;

    /// <summary>Directory with endgame (WLD) databases. Empty disables tablebase probing.</summary>
    public string? Databases { get; init; }

    [Range(2, 10)]
    public int TablebaseMaxPieces { get; init; } = 8;

    [Range(1, 4096)]
    public int HashSizeMb { get; init; } = 64;

    /// <summary>Database cache per worker. Every worker holds its own copy, so total RAM = Workers * DbCacheMb.</summary>
    [Range(0, 65536)]
    public int DbCacheMb { get; init; } = 512;

    [Range(1, 64)]
    public int SearchThreadsPerWorker { get; init; } = 1;

    [Range(1, 10_000)]
    public int WarmupTimeMs { get; init; } = 100;

    /// <summary>Asks the engine to use the full soft time instead of stopping early on "easy" moves.</summary>
    public bool UseExactTime { get; init; } = true;

    /// <summary>Extra raw CheckerBoard "enginecommand" strings sent to every worker after initialization.</summary>
    public string[] AdditionalCommands { get; init; } = [];
}
