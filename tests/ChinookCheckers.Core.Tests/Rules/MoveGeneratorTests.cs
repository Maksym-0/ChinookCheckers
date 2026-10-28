using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Notation;
using ChinookCheckers.Core.Rules;

namespace ChinookCheckers.Core.Tests.Rules;

public class MoveGeneratorTests
{
    // Expected moves below were derived by hand from the standard 1..32 board numbering
    // (rows of four dark squares; Black moves "down" towards 32, White "up" towards 1).

    [Theory]
    [InlineData("B:W21-32:B1-12", "9-13 9-14 10-14 10-15 11-15 11-16 12-16")]
    [InlineData("W:W21-32:B1-12", "21-17 22-17 22-18 23-18 23-19 24-19 24-20")]
    public void Generate_InitialPosition_HasSevenOpeningMoves(string pdn, string expected) =>
        AssertMoves(pdn, expected);

    [Theory]
    [InlineData("B:W32:B14", "14-17 14-18")]                       // a man steps forward on both diagonals
    [InlineData("B:W32:B12", "12-16")]                             // right edge: one diagonal only
    [InlineData("B:W32:B13", "13-17")]                             // left edge: one diagonal only
    [InlineData("B:W32:B14,17", "14-18 17-21 17-22")]              // own piece blocks a square
    [InlineData("W:W18:B1", "18-14 18-15")]                        // white men move up the board
    [InlineData("B:W32:BK18", "18-14 18-15 18-22 18-23")]          // kings move in all four directions
    [InlineData("W:WK18:B1", "18-14 18-15 18-22 18-23")]
    [InlineData("B:W32:BK13", "13-17 13-9")]                       // king on the edge
    public void Generate_SimpleMoves(string pdn, string expected) => AssertMoves(pdn, expected);

    [Theory]
    [InlineData("B:W16,19:B12")]   // the only man is blocked and cannot jump (landing square is occupied)
    [InlineData("B:W20:B")]        // the side to move has no pieces at all
    public void Generate_NoLegalMoves_ReturnsEmptyList(string pdn) => AssertMoves(pdn, "");

    [Theory]
    [InlineData("B:W18,32:B14,9", "14x23")]          // capture is mandatory: simple moves of 9 are not allowed
    [InlineData("B:W17,18,32:B14", "14x21 14x23")]   // several captures are offered, the player chooses
    [InlineData("B:W14,32:B18", "18-22 18-23")]      // men never capture backwards
    [InlineData("W:W18:B14", "18x9")]                // white captures upwards
    [InlineData("B:W14,32:BK18", "18x9")]            // kings capture backwards
    public void Generate_Captures(string pdn, string expected) => AssertMoves(pdn, expected);

    [Fact]
    public void Generate_MultiJump_MustBeCompleted()
    {
        var moves = Generate("B:W14,23,32:B9");

        var move = Assert.Single(moves);
        Assert.Equal("9x18x27", move.Notation);
        Assert.Equal([9, 18, 27], move.Path);
        Assert.Equal([14, 23], move.Captured);
    }

    [Fact]
    public void Generate_ManReachingKingRow_EndsItsCapture()
    {
        // From 31 the man would continue over 27 if it were already a king.
        AssertMoves("B:W26,27:B22", "22x31");
    }

    [Fact]
    public void Generate_KingReachingKingRow_ContinuesCapturing()
    {
        AssertMoves("B:W26,27:BK22", "22x31x24");
    }

    [Fact]
    public void Generate_KingCircuit_NeverJumpsTheSamePieceTwice_AndMayLandOnItsOrigin()
    {
        var moves = Generate("B:W15,16,23,24:BK18");

        Assert.Equal(
            ["18x11x20x27x18", "18x27x20x11x18"],
            moves.Select(move => move.Notation).Order(StringComparer.Ordinal));
        Assert.All(moves, move => Assert.Equal([15, 16, 23, 24], move.Captured.Order()));
    }

    [Fact]
    public void Generate_NullPosition_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MoveGenerator.GenerateLegalMoves(null!));
    }

    [Fact]
    public void IsLegal_MatchesMovesByPath()
    {
        var position = PdnParser.Parse("B:W14,23,32:B9");

        Assert.True(MoveGenerator.IsLegal(position, new Move([9, 18, 27], [14, 23])));
        Assert.False(MoveGenerator.IsLegal(position, new Move([9, 18], [14])));   // unfinished multi-jump
        Assert.False(MoveGenerator.IsLegal(position, new Move([9, 13], [])));     // capture is mandatory
    }

    /// <summary>
    /// Perft counts every legal move sequence of a given length from the initial position. The reference values
    /// are the well-known ones for English checkers, so they validate the whole rule set at once.
    /// </summary>
    [Theory]
    [InlineData(1, 7)]
    [InlineData(2, 49)]
    [InlineData(3, 302)]
    [InlineData(4, 1469)]
    [InlineData(5, 7361)]
    [InlineData(6, 36768)]
    public void Perft_FromInitialPosition_MatchesReferenceValues(int depth, long expectedNodes)
    {
        Assert.Equal(expectedNodes, Perft(Position.Initial, depth));
    }

    private static long Perft(Position position, int depth)
    {
        var moves = MoveGenerator.GenerateLegalMoves(position);
        return depth == 1
            ? moves.Count
            : moves.Sum(move => Perft(position.Apply(move), depth - 1));
    }

    private static IReadOnlyList<Move> Generate(string pdn) =>
        MoveGenerator.GenerateLegalMoves(PdnParser.Parse(pdn));

    private static void AssertMoves(string pdn, string expected)
    {
        var expectedMoves = expected.Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal);
        var actualMoves = Generate(pdn).Select(move => move.Notation).Order(StringComparer.Ordinal);

        Assert.Equal(expectedMoves, actualMoves);
    }
}
