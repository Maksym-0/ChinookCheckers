using ChinookCheckers.Application.DTOs;
using ChinookCheckers.Application.Services;
using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Notation;
using ChinookCheckers.Core.Options;
using Microsoft.Extensions.Options;

namespace ChinookCheckers.Application.Tests.Services;

public class SearchLimitsResolverTests
{
    // Defaults: soft 300 ms, hard 1200 ms (max 10 s), safety margin 100 ms, tablebase 30 ms,
    // Weak 100 ms / depth 8, Medium 250 ms / depth 12, Strong 500 ms / depth 18.
    private static readonly Position MiddleGame = Position.Initial;                    // 24 pieces
    private static readonly Position EightPieces = PdnParser.Parse("B:W17-20:B5-8");   // 8 pieces
    private static readonly Position NinePieces = PdnParser.Parse("B:W17-20:B5-9");    // 9 pieces

    private static SearchLimitsResolver CreateResolver(LimitsOptions? options = null) =>
        new(Options.Create(options ?? new LimitsOptions()));

    // ---- level -> limits --------------------------------------------------------------------------

    [Theory]
    [InlineData(StrengthLevel.Weak, 100, 8)]
    [InlineData(StrengthLevel.Medium, 250, 12)]
    [InlineData(StrengthLevel.Strong, 500, 18)]
    public void Resolve_Level_UsesTimeAndDepthOfThatLevel(StrengthLevel level, int expectedTimeMs, int expectedDepth)
    {
        var limits = CreateResolver().Resolve(level, null, MiddleGame, tablebaseMaxPieces: 8);

        Assert.Equal(new EngineSearchLimits(expectedTimeMs, expectedDepth, ProbeTablebase: false), limits);
    }

    [Fact]
    public void Resolve_NoLevel_UsesDefaultSoftTimeAndNoDepth()
    {
        var limits = CreateResolver().Resolve(null, null, MiddleGame, tablebaseMaxPieces: 8);

        Assert.Equal(new EngineSearchLimits(300, null, ProbeTablebase: false), limits);
    }

    [Fact]
    public void Resolve_LevelValuesComeFromConfiguration()
    {
        var resolver = CreateResolver(new LimitsOptions { Weak = new LevelLimits { TimeMs = 40, Depth = 3 } });

        var limits = resolver.Resolve(StrengthLevel.Weak, null, MiddleGame, tablebaseMaxPieces: 8);

        Assert.Equal(new EngineSearchLimits(40, 3, ProbeTablebase: false), limits);
    }

    [Fact]
    public void Resolve_ExplicitLimits_OverrideTheLevel()
    {
        var requested = new SearchLimitsDto { SoftTimeMs = 150, MaxDepth = 6 };

        var limits = CreateResolver().Resolve(StrengthLevel.Strong, requested, MiddleGame, tablebaseMaxPieces: 8);

        Assert.Equal(new EngineSearchLimits(150, 6, ProbeTablebase: false), limits);
    }

    [Fact]
    public void Resolve_PartialLimits_FallBackToLevelForTheRest()
    {
        var requested = new SearchLimitsDto { MaxDepth = 6 };

        var limits = CreateResolver().Resolve(StrengthLevel.Medium, requested, MiddleGame, tablebaseMaxPieces: 8);

        Assert.Equal(new EngineSearchLimits(250, 6, ProbeTablebase: false), limits);
    }

    // ---- soft time is bounded by the hard deadline ---------------------------------------------

    [Theory]
    [InlineData(5000, 1000, 900)]   // soft is capped at hard - margin (100 ms)
    [InlineData(1100, null, 1100)]  // exactly hard(1200) - margin
    [InlineData(1101, null, 1100)]
    [InlineData(1, null, 10)]       // never below the 10 ms floor
    [InlineData(500, 50, 10)]       // hard - margin is negative: the floor wins
    public void Resolve_SoftTime_IsClampedBetweenFloorAndHardMinusMargin(int softTimeMs, int? hardTimeMs, int expected)
    {
        var requested = new SearchLimitsDto { SoftTimeMs = softTimeMs, HardTimeMs = hardTimeMs };

        var limits = CreateResolver().Resolve(null, requested, MiddleGame, tablebaseMaxPieces: 8);

        Assert.Equal(expected, limits.SoftTimeMs);
    }

    [Fact]
    public void Resolve_LevelTimeAboveDefaultHardDeadline_IsClamped()
    {
        var resolver = CreateResolver(new LimitsOptions { Strong = new LevelLimits { TimeMs = 5000, Depth = 18 } });

        var limits = resolver.Resolve(StrengthLevel.Strong, null, MiddleGame, tablebaseMaxPieces: 8);

        Assert.Equal(1100, limits.SoftTimeMs);
    }

    // ---- tablebase ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, 8, true)]    // exactly at the limit
    [InlineData(false, 8, false)]  // one piece above the limit
    [InlineData(true, 0, false)]   // no database configured
    public void Resolve_ProbeTablebase_DependsOnPieceCount(bool useEightPieces, int tablebaseMaxPieces, bool expected)
    {
        var position = useEightPieces ? EightPieces : NinePieces;

        var limits = CreateResolver().Resolve(null, null, position, tablebaseMaxPieces);

        Assert.Equal(expected, limits.ProbeTablebase);
    }

    [Fact]
    public void Resolve_TablebasePosition_UsesShortTablebaseTimeRegardlessOfLevelAndRequestedTime()
    {
        var requested = new SearchLimitsDto { SoftTimeMs = 400 };

        var limits = CreateResolver().Resolve(StrengthLevel.Strong, requested, EightPieces, tablebaseMaxPieces: 8);

        Assert.True(limits.ProbeTablebase);
        Assert.Equal(30, limits.SoftTimeMs);
    }

    [Fact]
    public void Resolve_TablebaseTime_IsStillBoundedByHardDeadline()
    {
        var requested = new SearchLimitsDto { HardTimeMs = 50 };   // 50 - margin 100 < floor -> 10 ms

        var limits = CreateResolver().Resolve(null, requested, EightPieces, tablebaseMaxPieces: 8);

        Assert.Equal(10, limits.SoftTimeMs);
    }

    // ---- hard timeout -------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, 1200)]     // default
    [InlineData(500, 500)]
    [InlineData(50_000, 10_000)] // capped at MaxHardTimeMs
    [InlineData(0, 1)]           // never below 1 ms
    [InlineData(-5, 1)]
    public void ResolveHardTimeout_ClampsRequestedValue(int? hardTimeMs, int expectedMs)
    {
        var requested = hardTimeMs is null ? null : new SearchLimitsDto { HardTimeMs = hardTimeMs };

        var timeout = CreateResolver().ResolveHardTimeout(requested);

        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), timeout);
    }

    [Fact]
    public void ResolveHardTimeout_LimitsWithoutHardTime_UseDefault()
    {
        var timeout = CreateResolver().ResolveHardTimeout(new SearchLimitsDto { SoftTimeMs = 100 });

        Assert.Equal(TimeSpan.FromMilliseconds(1200), timeout);
    }

    [Fact]
    public void Resolve_NullPosition_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CreateResolver().Resolve(null, null, null!, 8));
    }
}
