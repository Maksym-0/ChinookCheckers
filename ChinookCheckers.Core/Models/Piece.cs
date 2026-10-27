namespace ChinookCheckers.Core.Models;

public enum Piece : byte
{
    Empty = 0,
    BlackMan,
    BlackKing,
    WhiteMan,
    WhiteKing
}

public static class PieceExtensions
{
    public static bool IsKing(this Piece piece) => piece is Piece.BlackKing or Piece.WhiteKing;

    public static bool BelongsTo(this Piece piece, PieceColor color) => color == PieceColor.Black
        ? piece is Piece.BlackMan or Piece.BlackKing
        : piece is Piece.WhiteMan or Piece.WhiteKing;

    public static Piece Crowned(this Piece piece) => piece switch
    {
        Piece.BlackMan => Piece.BlackKing,
        Piece.WhiteMan => Piece.WhiteKing,
        _ => piece
    };
}
