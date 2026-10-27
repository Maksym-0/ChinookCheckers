using System.Runtime.InteropServices;
using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Rules;

namespace ChinookCheckers.Infrastructure.Engine.Kingsrow.Native;

/// <summary>Constants and data layouts of the CheckerBoard engine API (getmove / enginecommand).</summary>
internal static class CheckerBoardInterop
{
    public const int BoardCells = BoardGeometry.BoardSize * BoardGeometry.BoardSize;
    public const int InfoBufferSize = 1024;
    public const int CommandBufferSize = 256;
    public const int ReplyBufferSize = 1024;

    public const int White = 1;
    public const int Black = 2;
    public const int Man = 4;
    public const int King = 8;

    public const int FlagResetMoves = 1;
    public const int FlagExactTime = 2;

    public const int ResultDraw = 0;
    public const int ResultWin = 1;
    public const int ResultLoss = 2;
    public const int ResultUnknown = 3;

    public static int ToColor(PieceColor color) => color == PieceColor.Black ? Black : White;

    /// <summary>
    /// Fills the engine's int[8][8] board (index x * 8 + y). CheckerBoard maps square n to
    /// n = 4 * (y + 1) - x / 2, i.e. x = 7 - column and y = row of the standard diagram.
    /// </summary>
    public static void WriteBoard(Position position, Span<int> board)
    {
        board.Clear();
        for (var square = 1; square <= BoardGeometry.SquareCount; square++)
        {
            board[CellIndex(square)] = position[square] switch
            {
                Piece.BlackMan => Black | Man,
                Piece.BlackKing => Black | King,
                Piece.WhiteMan => White | Man,
                Piece.WhiteKing => White | King,
                _ => 0
            };
        }
    }

    public static Position ReadBoard(ReadOnlySpan<int> board, PieceColor sideToMove)
    {
        var squares = new Piece[BoardGeometry.SquareCount];
        for (var square = 1; square <= BoardGeometry.SquareCount; square++)
        {
            var cell = board[CellIndex(square)];
            squares[square - 1] = (cell & (Black | White), cell & (Man | King)) switch
            {
                (Black, Man) => Piece.BlackMan,
                (Black, King) => Piece.BlackKing,
                (White, Man) => Piece.WhiteMan,
                (White, King) => Piece.WhiteKing,
                _ => Piece.Empty
            };
        }

        return new Position(squares, sideToMove);
    }

    private static int CellIndex(int square)
    {
        var x = BoardGeometry.BoardSize - 1 - BoardGeometry.Column(square);
        var y = BoardGeometry.Row(square);
        return x * BoardGeometry.BoardSize + y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct CbCoordinate
{
    public int X;
    public int Y;
}

/// <summary>Mirror of the C <c>struct CBmove</c> (268 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct CbMove
{
    public int Jumps;
    public int NewPiece;
    public int OldPiece;
    public CbCoordinate From;
    public CbCoordinate To;
    public fixed int Path[12 * 2];
    public fixed int Deleted[12 * 2];
    public fixed int DeletedPiece[12];
}
