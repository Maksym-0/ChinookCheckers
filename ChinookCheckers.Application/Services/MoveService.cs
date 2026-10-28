using System.Diagnostics;
using ChinookCheckers.Application.Caching;
using ChinookCheckers.Application.DTOs;
using ChinookCheckers.Application.Exceptions;
using ChinookCheckers.Application.Interfaces;
using ChinookCheckers.Core.Exceptions;
using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Notation;
using ChinookCheckers.Core.Options;
using ChinookCheckers.Core.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChinookCheckers.Application.Services;

public sealed class MoveService(
    ICheckersEnginePool enginePool,
    ISearchLimitsResolver limitsResolver,
    LruCache<string, SuggestMoveResponse> cache,
    IOptions<EngineOptions> engineOptions,
    ILogger<MoveService> logger) : IMoveService
{
    private const string PositionKeyPrefix = "pdn:";

    private readonly string _engineName = engineOptions.Value.Name;

    public async Task<SuggestMoveResponse> SuggestMoveAsync(
        SuggestMoveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAt = Stopwatch.GetTimestamp();
        EnsureSupported(request);

        var position = PdnParser.Parse(request.State.Position);
        var positionKey = PositionKeyPrefix + position.Key;
        var cacheKey = $"{positionKey}|{request.Level?.ToString() ?? "default"}";

        if (cache.TryGet(cacheKey, out var cached))
        {
            var response = cached with { Info = cached.Info with { TimeMs = ElapsedMs(startedAt) } };
            LogSuggestion(response, cacheHit: true);
            return response;
        }

        var legalMoves = MoveGenerator.GenerateLegalMoves(position);
        if (legalMoves.Count == 0)
        {
            throw new InvalidPositionException("The side to move has no legal moves (the game is over).");
        }

        var limits = limitsResolver.Resolve(request.Level, request.Limits, position, enginePool.TablebaseMaxPieces);
        var result = await enginePool.SearchAsync(position, limits, cancellationToken).ConfigureAwait(false);

        var bestMove = ResolveBestMove(position, legalMoves, result);
        var suggestion = new SuggestMoveResponse(
            Engine: _engineName,
            BestMove: bestMove.Notation,
            Pv: BuildPrincipalVariation(position, legalMoves, bestMove, result.PrincipalVariation),
            ScoreOrWdl: result.TablebaseHit && result.Wdl is { } wdl ? wdl : result.Score,
            Depth: result.Depth,
            Nodes: result.Nodes,
            PositionKey: positionKey,
            Info: new SearchInfoDto(result.TablebaseHit, ElapsedMs(startedAt)));

        cache.Set(cacheKey, suggestion);

        LogSuggestion(suggestion, cacheHit: false);
        return suggestion;
    }

    public ValidateMoveResponse ValidateMove(ValidateMoveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var position = PdnParser.Parse(request.Position);
        var legalMoves = MoveGenerator.GenerateLegalMoves(position);
        return new ValidateMoveResponse(MoveNotation.FindLegalMove(legalMoves, request.Move) is not null);
    }

    public LegalMovesResponse GetLegalMoves(LegalMovesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var position = PdnParser.Parse(request.Position);
        var legalMoves = MoveGenerator.GenerateLegalMoves(position);

        var moves = legalMoves
            .Select(move => new LegalMoveDto(move.Notation, move.Path, move.Captured, position.Apply(move).ToPdn()))
            .ToArray();

        return new LegalMovesResponse(
            position.SideToMove,
            Winner: moves.Length == 0 ? position.SideToMove.Opponent() : null,
            moves);
    }

    private static void EnsureSupported(SuggestMoveRequest request)
    {
        if (!string.Equals(request.GameId, SuggestMoveRequest.SupportedGameId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidPositionException(
                $"Unsupported gameId '{request.GameId}'. Only '{SuggestMoveRequest.SupportedGameId}' is supported.");
        }

        if (!string.Equals(request.State.Notation, GameStateDto.PdnNotation, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidPositionException(
                $"Unsupported notation '{request.State.Notation}'. Only '{GameStateDto.PdnNotation}' is supported.");
        }
    }

    /// <summary>
    /// The engine output is never trusted blindly: the move is matched against our own legal move list,
    /// first by the board the engine returned, then by its textual best move, then by the first PV move.
    /// </summary>
    private Move ResolveBestMove(Position position, IReadOnlyList<Move> legalMoves, EngineSearchResult result)
    {
        if (result.ResultingPosition is { } resulting)
        {
            var byBoard = legalMoves.FirstOrDefault(move => position.Apply(move).HasSamePlacement(resulting));
            if (byBoard is not null)
            {
                return byBoard;
            }
        }

        var fallback = MoveNotation.FindLegalMove(legalMoves, result.BestMoveNotation)
            ?? MoveNotation.FindLegalMove(legalMoves, result.PrincipalVariation.FirstOrDefault());

        if (fallback is null)
        {
            throw new EngineException(
                $"Engine returned an illegal move for position '{position.Key}' " +
                $"(best: '{result.BestMoveNotation}', pv: '{string.Join(' ', result.PrincipalVariation)}').");
        }

        logger.LogWarning(
            "Engine board did not match a legal move for {PositionKey}; using reported move {BestMove}",
            position.Key,
            fallback.Notation);
        return fallback;
    }

    /// <summary>Replays the engine PV and keeps only its legal prefix, normalized to our notation.</summary>
    private static IReadOnlyList<string> BuildPrincipalVariation(
        Position position,
        IReadOnlyList<Move> legalMoves,
        Move bestMove,
        IReadOnlyList<string> enginePv)
    {
        var pv = new List<string> { bestMove.Notation };
        if (enginePv.Count == 0 || MoveNotation.FindLegalMove(legalMoves, enginePv[0]) is not { } first
            || !first.Path.SequenceEqual(bestMove.Path))
        {
            return pv;
        }

        var current = position.Apply(bestMove);
        foreach (var notation in enginePv.Skip(1))
        {
            var move = MoveNotation.FindLegalMove(MoveGenerator.GenerateLegalMoves(current), notation);
            if (move is null)
            {
                break;
            }

            pv.Add(move.Notation);
            current = current.Apply(move);
        }

        return pv;
    }

    private void LogSuggestion(SuggestMoveResponse response, bool cacheHit) =>
        logger.LogInformation(
            "Move suggested {PositionKey} {BestMove} timeMs={TimeMs} depth={Depth} nodes={Nodes} " +
            "tablebaseHit={TablebaseHit} cacheHit={CacheHit}",
            response.PositionKey,
            response.BestMove,
            response.Info.TimeMs,
            response.Depth,
            response.Nodes,
            response.Info.TablebaseHit,
            cacheHit);

    private static long ElapsedMs(long startedAt) =>
        (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
}
