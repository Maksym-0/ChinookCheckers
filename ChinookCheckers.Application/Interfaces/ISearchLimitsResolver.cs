using ChinookCheckers.Application.DTOs;
using ChinookCheckers.Core.Models;

namespace ChinookCheckers.Application.Interfaces;

public interface ISearchLimitsResolver
{
    /// <summary>Effective hard deadline for a request (client value clamped by configuration).</summary>
    TimeSpan ResolveHardTimeout(SearchLimitsDto? limits);

    EngineSearchLimits Resolve(
        StrengthLevel? level,
        SearchLimitsDto? limits,
        Position position,
        int tablebaseMaxPieces);
}
