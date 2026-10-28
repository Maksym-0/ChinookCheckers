using ChinookCheckers.Core.Exceptions;
using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Notation;

namespace ChinookCheckers.Core.Tests.Notation;

public class PdnParserTests
{
    private const string InitialKey =
        "B:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12";

    [Fact]
    public void Parse_InitialPosition_EqualsPositionInitial()
    {
        var position = PdnParser.Parse("B:W21-32:B1-12");

        Assert.True(position.HasSamePlacement(Position.Initial));
        Assert.Equal(PieceColor.Black, position.SideToMove);
        Assert.Equal(InitialKey, position.Key);
    }

    [Theory]
    [InlineData("B", PieceColor.Black)]
    [InlineData("b", PieceColor.Black)]
    [InlineData("W", PieceColor.White)]
    [InlineData("w", PieceColor.White)]
    public void Parse_SideToMove_IsCaseInsensitive(string side, PieceColor expected)
    {
        var position = PdnParser.Parse($"{side}:W18:B5");

        Assert.Equal(expected, position.SideToMove);
    }

    [Fact]
    public void Parse_MenKingsAndRanges_PlacesEveryPiece()
    {
        var position = PdnParser.Parse("B:W18-20,K25:B5,k7");

        Assert.All([18, 19, 20], square => Assert.Equal(Piece.WhiteMan, position[square]));
        Assert.Equal(Piece.WhiteKing, position[25]);
        Assert.Equal(Piece.BlackMan, position[5]);
        Assert.Equal(Piece.BlackKing, position[7]);
        Assert.Equal(Piece.Empty, position[6]);
        Assert.Equal(4, position.CountPieces(PieceColor.White));
        Assert.Equal(2, position.CountPieces(PieceColor.Black));
    }

    [Theory]
    [InlineData(" B : W18 , 19 : B5 ")]
    [InlineData("b:w18,19:b5")]
    [InlineData("B:W18,19:B5.")]
    [InlineData("\"B:W18,19:B5\"")]
    [InlineData("[FEN \"B:W18,19:B5\"]")]
    [InlineData("[fen \"B:W18,19:B5\"]")]
    public void Parse_FormattingVariants_YieldSamePosition(string pdn)
    {
        Assert.Equal("B:W18,19:B5", PdnParser.Parse(pdn).Key);
    }

    [Theory]
    [InlineData("B:W32,18:B7,5", "B:W18,32:B5,7")]
    [InlineData("W:WK30,K10:B1", "W:WK10,K30:B1")]
    [InlineData("B:W18-20:B5-7", "B:W18,19,20:B5,6,7")]
    [InlineData("B:WK5-7:B20", "B:WK5,K6,K7:B20")]
    public void Key_IsCanonical_SortedAndExpanded(string pdn, string expectedKey)
    {
        var position = PdnParser.Parse(pdn);

        Assert.Equal(expectedKey, position.Key);
        Assert.Equal(expectedKey, PdnParser.Parse(position.Key).Key);
    }

    [Theory]
    [InlineData("B:WK1:BK32")]
    [InlineData("B:W32:B1")]
    public void Parse_KingsOnKingRowAndMenOnHomeRow_AreValid(string pdn)
    {
        Assert.NotNull(PdnParser.Parse(pdn));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyInput_Throws(string? pdn)
    {
        var exception = Assert.Throws<InvalidPositionException>(() => PdnParser.Parse(pdn));

        Assert.Contains("must not be empty", exception.Message);
    }

    [Theory]
    [InlineData("hello world", "must have the form")]
    [InlineData("B:W18", "must have the form")]
    [InlineData("B:W18:B5:W7", "must have the form")]
    [InlineData("X:W18:B5", "side to move")]
    [InlineData("B:Z18:B5", "piece list")]
    [InlineData("B::B5", "must start with")]
    [InlineData("B:W18:W5", "specified more than once")]
    [InlineData("B:W33:B5", "1..32")]
    [InlineData("B:W0:B5", "1..32")]
    [InlineData("B:Wabc:B5", "1..32")]
    [InlineData("B:W5-3:B20", "1..32")]
    [InlineData("B:W18,18:B5", "occupied more than once")]
    [InlineData("B:W18:B18", "occupied more than once")]
    [InlineData("B:W:B", "no pieces")]
    [InlineData("B:WK1-13:B20", "at most 12")]
    [InlineData("B:W20:BK1-12,K13", "at most 12")]
    [InlineData("B:W1:B20", "king row")]
    [InlineData("B:W4:B20", "king row")]
    [InlineData("B:W18:B29", "king row")]
    [InlineData("B:W18:B32", "king row")]
    public void Parse_InvalidPosition_ThrowsWithReason(string pdn, string expectedMessagePart)
    {
        var exception = Assert.Throws<InvalidPositionException>(() => PdnParser.Parse(pdn));

        Assert.Contains(expectedMessagePart, exception.Message);
    }

    [Fact]
    public void TryParse_ValidPosition_ReturnsTrueAndPosition()
    {
        var parsed = PdnParser.TryParse("W:W18:B5", out var position);

        Assert.True(parsed);
        Assert.NotNull(position);
        Assert.Equal("W:W18:B5", position.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("B:W33:B5")]
    [InlineData("garbage")]
    public void TryParse_InvalidPosition_ReturnsFalseAndNull(string? pdn)
    {
        var parsed = PdnParser.TryParse(pdn, out var position);

        Assert.False(parsed);
        Assert.Null(position);
    }
}
