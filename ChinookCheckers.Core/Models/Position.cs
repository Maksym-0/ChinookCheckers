using System.Text;
using ChinookCheckers.Core.Rules;

namespace ChinookCheckers.Core.Models;

/// <summary>
/// Immutable 8x8 English checkers position in standard PDN numbering (Black starts on 1-12 and moves first).
/// </summary>
public sealed class Position
{
    private readonly Piece[] _squares;

    public Position(ReadOnlySpan<Piece> squares, PieceColor sideToMove)
    {
        if (squares.Length != BoardGeometry.SquareCount)
        {
            throw new ArgumentException($"Expected {BoardGeometry.SquareCount} squares.", nameof(squares));
        }

        _squares = squares.ToArray();
        SideToMove = sideToMove;
    }

    public static Position Initial { get; } = CreateInitial();

    public PieceColor SideToMove { get; }

    public Piece this[int square]
    {
        get
        {
            BoardGeometry.EnsureValidSquare(square);
            return _squares[square - 1];
        }
    }

    public int PieceCount => _squares.Count(piece => piece != Piece.Empty);

    public int CountPieces(PieceColor color) => _squares.Count(piece => piece.BelongsTo(color));

    /// <summary>Canonical PDN FEN: side to move, white list, black list, squares ascending, kings prefixed with K.</summary>
    public string Key => ToPdn();

    public Piece[] ToArray() => (Piece[])_squares.Clone();

    public Position Apply(Move move)
    {
        ArgumentNullException.ThrowIfNull(move);

        var squares = ToArray();
        var piece = squares[move.From - 1];
        if (!piece.BelongsTo(SideToMove))
        {
            throw new InvalidOperationException($"Square {move.From} does not hold a piece of the side to move.");
        }

        squares[move.From - 1] = Piece.Empty;
        foreach (var captured in move.Captured)
        {
            squares[captured - 1] = Piece.Empty;
        }

        squares[move.To - 1] = BoardGeometry.IsPromotionSquare(move.To, SideToMove) ? piece.Crowned() : piece;
        return new Position(squares, SideToMove.Opponent());
    }

    public bool HasSamePlacement(Position other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _squares.AsSpan().SequenceEqual(other._squares);
    }

    public string ToPdn()
    {
        var builder = new StringBuilder(96);
        builder.Append(SideToMove == PieceColor.Black ? 'B' : 'W');
        AppendSide(builder, 'W', PieceColor.White);
        AppendSide(builder, 'B', PieceColor.Black);
        return builder.ToString();
    }

    public override string ToString() => ToPdn();

    private void AppendSide(StringBuilder builder, char prefix, PieceColor color)
    {
        builder.Append(':').Append(prefix);
        var first = true;
        for (var square = 1; square <= BoardGeometry.SquareCount; square++)
        {
            var piece = _squares[square - 1];
            if (!piece.BelongsTo(color))
            {
                continue;
            }

            if (!first)
            {
                builder.Append(',');
            }

            if (piece.IsKing())
            {
                builder.Append('K');
            }

            builder.Append(square);
            first = false;
        }
    }

    private static Position CreateInitial()
    {
        var squares = new Piece[BoardGeometry.SquareCount];
        for (var square = 1; square <= 12; square++)
        {
            squares[square - 1] = Piece.BlackMan;
        }

        for (var square = 21; square <= 32; square++)
        {
            squares[square - 1] = Piece.WhiteMan;
        }

        return new Position(squares, PieceColor.Black);
    }
}
