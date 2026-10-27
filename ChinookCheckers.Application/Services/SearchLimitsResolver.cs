using ChinookCheckers.Application.DTOs;
using ChinookCheckers.Application.Interfaces;
using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Options;
using Microsoft.Extensions.Options;

namespace ChinookCheckers.Application.Services;

public sealed class SearchLimitsResolver(IOptions<LimitsOptions> limitsOptions) : ISearchLimitsResolver
{
    private const int MinSoftTimeMs = 10;

    private readonly LimitsOptions _limits = limitsOptions.Value;

    public TimeSpan ResolveHardTimeout(SearchLimitsDto? limits)
    {
        var hardTimeMs = Math.Clamp(limits?.HardTimeMs ?? _limits.DefaultHardTimeMs, 1, _limits.MaxHardTimeMs);
        return TimeSpan.FromMilliseconds(hardTimeMs);
    }

    public EngineSearchLimits Resolve(
        StrengthLevel? level,
        SearchLimitsDto? limits,
        Position position,
        int tablebaseMaxPieces)
    {
        ArgumentNullException.ThrowIfNull(position);

        var probeTablebase = position.PieceCount <= tablebaseMaxPieces;
        var levelLimits = level switch
        {
            StrengthLevel.Weak => _limits.Weak,
            StrengthLevel.Medium => _limits.Medium,
            StrengthLevel.Strong => _limits.Strong,
            _ => null
        };

        var softTimeMs = probeTablebase
            ? _limits.TablebaseTimeMs
            : limits?.SoftTimeMs ?? levelLimits?.TimeMs ?? _limits.DefaultSoftTimeMs;

        var hardTimeMs = (int)ResolveHardTimeout(limits).TotalMilliseconds;
        var maxSoftTimeMs = Math.Max(MinSoftTimeMs, hardTimeMs - _limits.HardTimeSafetyMarginMs);
        softTimeMs = Math.Clamp(softTimeMs, MinSoftTimeMs, maxSoftTimeMs);

        return new EngineSearchLimits(softTimeMs, limits?.MaxDepth ?? levelLimits?.Depth, probeTablebase);
    }
}
