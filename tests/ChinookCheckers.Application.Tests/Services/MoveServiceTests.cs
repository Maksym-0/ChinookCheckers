using ChinookCheckers.Application.Caching;
using ChinookCheckers.Application.DTOs;
using ChinookCheckers.Application.Exceptions;
using ChinookCheckers.Application.Interfaces;
using ChinookCheckers.Application.Services;
using ChinookCheckers.Core.Exceptions;
using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Notation;
using ChinookCheckers.Core.Options;
using ChinookCheckers.Core.Rules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ChinookCheckers.Application.Tests.Services;

public class MoveServiceTests
{
    // Black to move and forced to capture: either 14x23 or 16x23. After 14x23 White must answer 27x18,
    // after which 16x23 is possible again - this is exactly the line the real engine reports.
    private const string CapturePosition = "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16";
    private const string OpeningPosition = "B:W21-32:B1-12";
    private const int TablebaseMaxPieces = 8;

    private readonly Mock<ICheckersEnginePool> _pool = new();
    private readonly Mock<ISearchLimitsResolver> _resolver = new();
    private readonly LruCache<string, SuggestMoveResponse> _cache = new(capacity: 100, ttl: TimeSpan.FromMinutes(15));
    private readonly EngineSearchLimits _limits = new(SoftTimeMs: 250, MaxDepth: 12, ProbeTablebase: false);

    public MoveServiceTests()
    {
        _pool.SetupGet(pool => pool.TablebaseMaxPieces).Returns(TablebaseMaxPieces);
        _resolver
            .Setup(resolver => resolver.Resolve(
                It.IsAny<StrengthLevel?>(), It.IsAny<SearchLimitsDto?>(), It.IsAny<Position>(), It.IsAny<int>()))
            .Returns(_limits);
    }

    // ---- SuggestMoveAsync: mapping of the engine answer ---------------------------------------------

    [Fact]
    public async Task SuggestMove_EngineAnswers_MapsResultToResponse()
    {
        SetupEngine(Result("14x23", pv: ["14x23", "27x18", "16x23"], score: 158, depth: 18, nodes: 893_685));

        var response = await CreateService().SuggestMoveAsync(Request(), CancellationToken.None);

        Assert.Equal("chinook", response.Engine);
        Assert.Equal("14x23", response.BestMove);
        Assert.Equal(["14x23", "27x18", "16x23"], response.Pv);
        Assert.Equal(158, response.ScoreOrWdl);
        Assert.Equal(18, response.Depth);
        Assert.Equal(893_685, response.Nodes);
        Assert.Equal($"pdn:{CapturePosition}", response.PositionKey);
        Assert.False(response.Info.TablebaseHit);
        Assert.True(response.Info.TimeMs >= 0);
    }

    [Theory]
    [InlineData(true, 1, 0, 1)]      // proven win from the tablebase: WDL is reported instead of the score
    [InlineData(true, -1, 7, -1)]
    [InlineData(true, null, 42, 42)] // tablebase hit without a verdict: fall back to the score
    [InlineData(false, 1, 42, 42)]   // WDL is only meaningful for tablebase hits
    public async Task SuggestMove_ScoreOrWdl_PrefersWdlOnlyForTablebaseHits(
        bool tablebaseHit, int? wdl, int score, int expected)
    {
        SetupEngine(Result("14x23", score: score, wdl: wdl, tablebaseHit: tablebaseHit));

        var response = await CreateService().SuggestMoveAsync(Request(), CancellationToken.None);

        Assert.Equal(expected, response.ScoreOrWdl);
        Assert.Equal(tablebaseHit, response.Info.TablebaseHit);
    }

    // ---- SuggestMoveAsync: the engine output is never trusted blindly ------------------------------------

    [Fact]
    public async Task SuggestMove_ResultingBoard_TakesPrecedenceOverReportedNotation()
    {
        var position = PdnParser.Parse(CapturePosition);
        var other = MoveGenerator.GenerateLegalMoves(position).Single(move => move.Notation == "16x23");
        SetupEngine(Result("14x23", resulting: position.Apply(other), pv: ["14x23"]));

        var response = await CreateService().SuggestMoveAsync(Request(), CancellationToken.None);

        Assert.Equal("16x23", response.BestMove);
        Assert.Equal(["16x23"], response.Pv);   // the engine PV starts with another move, so it is discarded
    }

    [Fact]
    public async Task SuggestMove_ResultingBoardMatchesNothing_FallsBackToReportedNotation()
    {
        SetupEngine(Result("16x23", resulting: Position.Initial));

        var response = await CreateService().SuggestMoveAsync(Request(), CancellationToken.None);

        Assert.Equal("16x23", response.BestMove);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("1-5")]   // well-formed but not legal in this position
    public async Task SuggestMove_UnusableNotation_FallsBackToFirstPvMove(string? reportedMove)
    {
        SetupEngine(Result(reportedMove, pv: ["16x23"]));

        var response = await CreateService().SuggestMoveAsync(Request(), CancellationToken.None);

        Assert.Equal("16x23", response.BestMove);
    }

    [Fact]
    public async Task SuggestMove_EngineMoveIsIllegal_ThrowsEngineExceptionAndCachesNothing()
    {
        SetupEngine(Result("1-5", pv: ["2-6"]));
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<EngineException>(
            () => service.SuggestMoveAsync(Request(), CancellationToken.None));

        Assert.Contains("illegal move", exception.Message);
        Assert.Equal(0, _cache.Count);
    }

    [Theory]
    [InlineData("14x23 27x18 99-99 16x23", "14x23 27x18")]   // replay stops at the first illegal move
    [InlineData("14x23 1-2", "14x23")]
    [InlineData("14x23", "14x23")]
    [InlineData("", "14x23")]                                // no PV from the engine: only the best move
    [InlineData("16x23 27x18", "14x23")]                     // PV starts with another move: not trusted
    public async Task SuggestMove_PrincipalVariation_IsReplayedAndTrimmedToItsLegalPrefix(string enginePv, string expected)
    {
        SetupEngine(Result("14x23", pv: enginePv.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

        var response = await CreateService().SuggestMoveAsync(Request(), CancellationToken.None);

        Assert.Equal(expected.Split(' '), response.Pv);
    }

    // ---- SuggestMoveAsync: cache -----------------------------------------------------------------------------

    [Fact]
    public async Task SuggestMove_SameRequestTwice_SecondAnswerComesFromCache()
    {
        SetupEngine(Result("14x23", depth: 18));
        var service = CreateService();

        var first = await service.SuggestMoveAsync(Request(), CancellationToken.None);
        var second = await service.SuggestMoveAsync(Request(), CancellationToken.None);

        Assert.Equal(first.BestMove, second.BestMove);
        Assert.Equal(first.Depth, second.Depth);
        Assert.Equal(first.Pv, second.Pv);
        Assert.Equal(1, _cache.Count);
        VerifyEngineCalls(Times.Once());
        _resolver.Verify(
            resolver => resolver.Resolve(
                It.IsAny<StrengthLevel?>(), It.IsAny<SearchLimitsDto?>(), It.IsAny<Position>(), It.IsAny<int>()),
            Times.Once());
    }

    [Fact]
    public async Task SuggestMove_DifferentSpellingOfSamePosition_SharesCacheEntry()
    {
        SetupEngine(Result("14x23"));
        var service = CreateService();

        await service.SuggestMoveAsync(Request(CapturePosition), CancellationToken.None);
        await service.SuggestMoveAsync(Request(CapturePosition.ToLowerInvariant()), CancellationToken.None);
        await service.SuggestMoveAsync(Request($"[FEN \"{CapturePosition}\"]"), CancellationToken.None);

        VerifyEngineCalls(Times.Once());
    }

    [Fact]
    public async Task SuggestMove_CacheIsKeyedByLevel()
    {
        SetupEngine(Result("14x23"));
        var service = CreateService();

        await service.SuggestMoveAsync(Request(level: StrengthLevel.Weak), CancellationToken.None);
        await service.SuggestMoveAsync(Request(level: StrengthLevel.Strong), CancellationToken.None);
        await service.SuggestMoveAsync(Request(level: null), CancellationToken.None);
        await service.SuggestMoveAsync(Request(level: StrengthLevel.Weak), CancellationToken.None);   // cache hit

        VerifyEngineCalls(Times.Exactly(3));
    }

    [Fact]
    public async Task SuggestMove_EngineFailure_IsNotCached()
    {
        _pool
            .SetupSequence(pool => pool.SearchAsync(It.IsAny<Position>(), It.IsAny<EngineSearchLimits>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EngineUnavailableException("engine is down"))
            .ReturnsAsync(Result("14x23"));
        var service = CreateService();

        await Assert.ThrowsAsync<EngineUnavailableException>(
            () => service.SuggestMoveAsync(Request(), CancellationToken.None));
        var retry = await service.SuggestMoveAsync(Request(), CancellationToken.None);

        Assert.Equal("14x23", retry.BestMove);
        VerifyEngineCalls(Times.Exactly(2));
    }

    // ---- SuggestMoveAsync: collaboration with the pool and the limits resolver --------------------------

    [Fact]
    public async Task SuggestMove_PassesResolvedLimitsAndCancellationTokenToThePool()
    {
        SetupEngine(Result("14x23"));
        var requestedLimits = new SearchLimitsDto { SoftTimeMs = 150 };
        using var cts = new CancellationTokenSource();

        await CreateService().SuggestMoveAsync(
            Request(level: StrengthLevel.Strong, limits: requestedLimits), cts.Token);

        _resolver.Verify(
            resolver => resolver.Resolve(
                StrengthLevel.Strong, requestedLimits, It.Is<Position>(p => p.Key == CapturePosition), TablebaseMaxPieces),
            Times.Once());
        _pool.Verify(
            pool => pool.SearchAsync(It.Is<Position>(p => p.Key == CapturePosition), _limits, cts.Token),
            Times.Once());
    }

    [Fact]
    public async Task SuggestMove_CancelledSearch_PropagatesAndCachesNothing()
    {
        _pool
            .Setup(pool => pool.SearchAsync(It.IsAny<Position>(), It.IsAny<EngineSearchLimits>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().SuggestMoveAsync(Request(), CancellationToken.None));

        Assert.Equal(0, _cache.Count);
    }

    // ---- SuggestMoveAsync: request validation (the engine must not be called) -----------------------------

    [Theory]
    [InlineData("chess", "PDN", CapturePosition, "Unsupported gameId")]
    [InlineData("checkers-8x8", "FEN", CapturePosition, "Unsupported notation")]
    [InlineData("checkers-8x8", "PDN", "B:W33:B5", "1..32")]
    [InlineData("checkers-8x8", "PDN", "B:W20:B", "no legal moves")]   // game is over: nothing to suggest
    public async Task SuggestMove_InvalidRequest_ThrowsInvalidPositionAndSkipsTheEngine(
        string gameId, string notation, string position, string expectedMessagePart)
    {
        var exception = await Assert.ThrowsAsync<InvalidPositionException>(
            () => CreateService().SuggestMoveAsync(Request(position, gameId: gameId, notation: notation), CancellationToken.None));

        Assert.Contains(expectedMessagePart, exception.Message);
        VerifyEngineCalls(Times.Never());
    }

    [Fact]
    public async Task SuggestMove_GameIdAndNotationAreCaseInsensitive()
    {
        SetupEngine(Result("14x23"));

        var response = await CreateService().SuggestMoveAsync(
            Request(gameId: "CHECKERS-8X8", notation: "pdn"), CancellationToken.None);

        Assert.Equal("14x23", response.BestMove);
    }

    [Fact]
    public async Task NullRequests_Throw()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SuggestMoveAsync(null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => service.ValidateMove(null!));
        Assert.Throws<ArgumentNullException>(() => service.GetLegalMoves(null!));
    }

    // ---- ValidateMove -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(OpeningPosition, "9-13", true)]
    [InlineData(OpeningPosition, "9x13", true)]       // separator is not significant
    [InlineData(OpeningPosition, "12-16", true)]
    [InlineData(OpeningPosition, "1-5", false)]       // blocked by an own piece
    [InlineData(OpeningPosition, "9-15", false)]      // not adjacent
    [InlineData(OpeningPosition, "21-17", false)]     // piece of the other side
    [InlineData(OpeningPosition, "nonsense", false)]
    [InlineData(CapturePosition, "14x23", true)]
    [InlineData(CapturePosition, "16x23", true)]
    [InlineData(CapturePosition, "10-15", false)]     // a simple move is not allowed while a capture is available
    [InlineData("B:W14,23,32:B9", "9x18x27", true)]
    [InlineData("B:W14,23,32:B9", "9x27", true)]      // short form of a multi-jump
    [InlineData("B:W14,23,32:B9", "9x18", false)]     // unfinished multi-jump
    public void ValidateMove_ReportsWhetherTheMoveIsLegal(string position, string move, bool expected)
    {
        var response = CreateService().ValidateMove(new ValidateMoveRequest { Position = position, Move = move });

        Assert.Equal(expected, response.Legal);
    }

    [Fact]
    public void ValidateMove_InvalidPosition_Throws()
    {
        var request = new ValidateMoveRequest { Position = "B:W33:B5", Move = "9-13" };

        Assert.Throws<InvalidPositionException>(() => CreateService().ValidateMove(request));
    }

    // ---- GetLegalMoves ----------------------------------------------------------------------------------------

    [Fact]
    public void GetLegalMoves_OpeningPosition_ListsSevenMovesWithResultingPositions()
    {
        var response = CreateService().GetLegalMoves(new LegalMovesRequest { Position = OpeningPosition });

        Assert.Equal(PieceColor.Black, response.SideToMove);
        Assert.Null(response.Winner);
        Assert.Equal(
            new[] { "9-13", "9-14", "10-14", "10-15", "11-15", "11-16", "12-16" }.Order(StringComparer.Ordinal),
            response.Moves.Select(move => move.Notation).Order(StringComparer.Ordinal));

        var nineToThirteen = response.Moves.Single(move => move.Notation == "9-13");
        Assert.Equal("W:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,10,11,12,13", nineToThirteen.ResultingPosition);
    }

    [Fact]
    public void GetLegalMoves_Capture_ReportsPathAndCapturedSquares()
    {
        var response = CreateService().GetLegalMoves(new LegalMovesRequest { Position = "B:W18,32:B14" });

        var move = Assert.Single(response.Moves);
        Assert.Equal("14x23", move.Notation);
        Assert.Equal([14, 23], move.Path);
        Assert.Equal([18], move.Captured);
        Assert.Equal("W:W32:B23", move.ResultingPosition);
    }

    [Fact]
    public void GetLegalMoves_SideToMoveCannotMove_ReportsTheOpponentAsWinner()
    {
        var response = CreateService().GetLegalMoves(new LegalMovesRequest { Position = "B:W20:B" });

        Assert.Empty(response.Moves);
        Assert.Equal(PieceColor.Black, response.SideToMove);
        Assert.Equal(PieceColor.White, response.Winner);
    }

    [Fact]
    public void GetLegalMoves_InvalidPosition_Throws()
    {
        Assert.Throws<InvalidPositionException>(
            () => CreateService().GetLegalMoves(new LegalMovesRequest { Position = "B:W33:B5" }));
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------

    private MoveService CreateService() => new(
        _pool.Object,
        _resolver.Object,
        _cache,
        Options.Create(new EngineOptions { Name = "chinook" }),
        NullLogger<MoveService>.Instance);

    private void SetupEngine(EngineSearchResult result) =>
        _pool
            .Setup(pool => pool.SearchAsync(It.IsAny<Position>(), It.IsAny<EngineSearchLimits>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void VerifyEngineCalls(Times times) =>
        _pool.Verify(
            pool => pool.SearchAsync(It.IsAny<Position>(), It.IsAny<EngineSearchLimits>(), It.IsAny<CancellationToken>()),
            times);

    private static SuggestMoveRequest Request(
        string position = CapturePosition,
        StrengthLevel? level = null,
        SearchLimitsDto? limits = null,
        string gameId = "checkers-8x8",
        string notation = "PDN") =>
        new()
        {
            GameId = gameId,
            State = new GameStateDto { Notation = notation, Position = position },
            Level = level,
            Limits = limits
        };

    private static EngineSearchResult Result(
        string? bestMove,
        Position? resulting = null,
        string[]? pv = null,
        int score = 0,
        int? wdl = null,
        int depth = 10,
        long nodes = 1_000,
        bool tablebaseHit = false) =>
        new(resulting, bestMove, pv ?? [], score, wdl, depth, nodes, tablebaseHit, TimeSpan.FromMilliseconds(5));

}
