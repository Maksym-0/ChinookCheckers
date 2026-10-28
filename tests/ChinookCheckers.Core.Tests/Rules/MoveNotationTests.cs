using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Notation;
using ChinookCheckers.Core.Rules;

namespace ChinookCheckers.Core.Tests.Rules;

public class MoveNotationTests
{
    private static readonly IReadOnlyList<Move> OpeningMoves =
        MoveGenerator.GenerateLegalMoves(Position.Initial);

    // Black man on 9 must capture twice: 9x18x27.
    private static readonly IReadOnlyList<Move> MultiJumpMoves =
        MoveGenerator.GenerateLegalMoves(PdnParser.Parse("B:W14,23,32:B9"));

    [Theory]
    [InlineData("9-13")]
    [InlineData("9x13")]
    [InlineData("9:13")]
    [InlineData(" 9 - 13 ")]
    public void FindLegalMove_AcceptsSeparatorAndSpacingVariants(string notation)
    {
        var move = MoveNotation.FindLegalMove(OpeningMoves, notation);

        Assert.NotNull(move);
        Assert.Equal([9, 13], move.Path);
    }

    [Theory]
    [InlineData("9x18x27")]
    [InlineData("9X18X27")]
    [InlineData("9x27")]   // short form: origin and destination only
    public void FindLegalMove_ResolvesMultiJumpInFullAndShortForm(string notation)
    {
        var move = MoveNotation.FindLegalMove(MultiJumpMoves, notation);

        Assert.NotNull(move);
        Assert.Equal("9x18x27", move.Notation);
    }

    [Theory]
    [InlineData("9-14")]    // legal in general, but not here: a capture is mandatory
    [InlineData("9x18")]    // unfinished multi-jump
    [InlineData("14-18")]   // not a move of the side to move
    public void FindLegalMove_IllegalMove_ReturnsNull(string notation)
    {
        Assert.Null(MoveNotation.FindLegalMove(MultiJumpMoves, notation));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("9")]
    [InlineData("9-")]
    [InlineData("-13")]
    [InlineData("9--13")]
    [InlineData("9-33")]
    [InlineData("0-5")]
    [InlineData("123-5")]
    public void FindLegalMove_MalformedNotation_ReturnsNull(string? notation)
    {
        Assert.Null(MoveNotation.FindLegalMove(OpeningMoves, notation));
    }

    [Fact]
    public void FindLegalMove_NullMoveList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MoveNotation.FindLegalMove(null!, "9-13"));
    }

    [Fact]
    public void TryParseSquares_ValidNotation_ReturnsSquaresInOrder()
    {
        Assert.True(MoveNotation.TryParseSquares("9x18x27", out var squares));
        Assert.Equal([9, 18, 27], squares);
    }

    [Theory]
    [InlineData("9-33")]
    [InlineData("nonsense")]
    public void TryParseSquares_InvalidNotation_ReturnsFalse(string notation)
    {
        Assert.False(MoveNotation.TryParseSquares(notation, out _));
    }
}
