using ChinookCheckers.Core.Models;

namespace ChinookCheckers.Core.Rules;

/// <summary>
/// Square geometry for standard numbering: row 0 holds squares 1-4 (Black's back rank), row 7 holds 29-32.
/// </summary>
public static class BoardGeometry
{
    public const int SquareCount = 32;
    public const int BoardSize = 8;

    private static readonly int[,] Neighbors = BuildNeighbors();

    public static int Row(int square)
    {
        EnsureValidSquare(square);
        return (square - 1) / 4;
    }

    public static int Column(int square)
    {
        var row = Row(square);
        return 2 * ((square - 1) % 4) + (row % 2 == 0 ? 1 : 0);
    }

    /// <returns>Square number, or 0 when the coordinates are off-board or on a light square.</returns>
    public static int ToSquare(int row, int column)
    {
        if (row is < 0 or >= BoardSize || column is < 0 or >= BoardSize || (row + column) % 2 == 0)
        {
            return 0;
        }

        return row * 4 + column / 2 + 1;
    }

    /// <returns>Adjacent square in the given direction, or 0 when it is off-board.</returns>
    public static int Neighbor(int square, Direction direction)
    {
        EnsureValidSquare(square);
        return Neighbors[square - 1, (int)direction];
    }

    public static bool IsPromotionSquare(int square, PieceColor color) =>
        Row(square) == (color == PieceColor.Black ? BoardSize - 1 : 0);

    public static bool IsValidSquare(int square) => square is >= 1 and <= SquareCount;

    public static void EnsureValidSquare(int square)
    {
        if (!IsValidSquare(square))
        {
            throw new ArgumentOutOfRangeException(nameof(square), square, "Square must be in range 1..32.");
        }
    }

    private static int[,] BuildNeighbors()
    {
        var neighbors = new int[SquareCount, 4];
        for (var square = 1; square <= SquareCount; square++)
        {
            var row = (square - 1) / 4;
            var column = 2 * ((square - 1) % 4) + (row % 2 == 0 ? 1 : 0);
            neighbors[square - 1, (int)Direction.UpLeft] = ToSquare(row - 1, column - 1);
            neighbors[square - 1, (int)Direction.UpRight] = ToSquare(row - 1, column + 1);
            neighbors[square - 1, (int)Direction.DownLeft] = ToSquare(row + 1, column - 1);
            neighbors[square - 1, (int)Direction.DownRight] = ToSquare(row + 1, column + 1);
        }

        return neighbors;
    }
}

/// <summary>"Up" points towards square 1 (White's direction of travel), "Down" towards square 32 (Black's).</summary>
public enum Direction
{
    UpLeft = 0,
    UpRight = 1,
    DownLeft = 2,
    DownRight = 3
}
