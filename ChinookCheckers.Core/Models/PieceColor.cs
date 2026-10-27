namespace ChinookCheckers.Core.Models;

public enum PieceColor
{
    Black,
    White
}

public static class PieceColorExtensions
{
    public static PieceColor Opponent(this PieceColor color) =>
        color == PieceColor.Black ? PieceColor.White : PieceColor.Black;
}
