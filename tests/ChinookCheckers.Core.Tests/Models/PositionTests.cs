using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Notation;

namespace ChinookCheckers.Core.Tests.Models;

public class PositionTests
{
    [Fact]
    public void Initial_HasTwelvePiecesPerSideAndBlackToMove()
    {
        var position = Position.Initial;

        Assert.Equal(PieceColor.Black, position.SideToMove);
        Assert.Equal(12, position.CountPieces(PieceColor.Black));
        Assert.Equal(12, position.CountPieces(PieceColor.White));
        Assert.Equal(24, position.PieceCount);
    }

    [Fact]
    public void Apply_SimpleMove_MovesPieceAndPassesTurn()
    {
        var position = PdnParser.Parse("B:W32:B14");

        var next = position.Apply(new Move([14, 18], []));

        Assert.Equal("W:W32:B18", next.Key);
        Assert.Equal("B:W32:B14", position.Key);   // the original position is immutable
    }

    [Fact]
    public void Apply_Capture_RemovesCapturedPieces()
    {
        var position = PdnParser.Parse("B:W14,23,32:B9");

        var next = position.Apply(new Move([9, 18, 27], [14, 23]));

        Assert.Equal("W:W32:B27", next.Key);
    }

    [Theory]
    [InlineData("B:W20:B28", 28, 32, Piece.BlackKing)]
    [InlineData("W:W5:B20", 5, 1, Piece.WhiteKing)]
    public void Apply_ReachingKingRow_CrownsTheMan(string pdn, int from, int to, Piece expected)
    {
        var next = PdnParser.Parse(pdn).Apply(new Move([from, to], []));

        Assert.Equal(expected, next[to]);
    }

    [Fact]
    public void Apply_PieceOfTheOpponent_Throws()
    {
        var position = PdnParser.Parse("B:W32:B14");

        Assert.Throws<InvalidOperationException>(() => position.Apply(new Move([32, 28], [])));
    }

    [Fact]
    public void ToArray_ReturnsCopy_SoThePositionStaysImmutable()
    {
        var position = PdnParser.Parse("B:W32:B14");

        position.ToArray()[13] = Piece.Empty;

        Assert.Equal(Piece.BlackMan, position[14]);
    }

    [Fact]
    public void Indexer_SquareOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Position.Initial[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Position.Initial[33]);
    }

    [Fact]
    public void Constructor_WrongNumberOfSquares_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Position(new Piece[31], PieceColor.Black));
    }

    [Fact]
    public void HasSamePlacement_IgnoresSideToMove()
    {
        var black = PdnParser.Parse("B:W32:B14");
        var white = PdnParser.Parse("W:W32:B14");

        Assert.True(black.HasSamePlacement(white));
        Assert.False(black.HasSamePlacement(PdnParser.Parse("B:W32:B18")));
    }
}
