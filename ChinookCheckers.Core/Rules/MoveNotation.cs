using System.Text.RegularExpressions;
using ChinookCheckers.Core.Models;

namespace ChinookCheckers.Core.Rules;

/// <summary>
/// Resolves textual moves ("22-18", "18x11", full "9x18x27" or short "9x27" multi-jumps) against legal moves.
/// </summary>
public static partial class MoveNotation
{
    public static Move? FindLegalMove(IReadOnlyList<Move> legalMoves, string? notation)
    {
        ArgumentNullException.ThrowIfNull(legalMoves);

        if (!TryParseSquares(notation, out var squares))
        {
            return null;
        }

        var exact = legalMoves.FirstOrDefault(move => move.Path.SequenceEqual(squares));
        if (exact is not null || squares.Length != 2)
        {
            return exact;
        }

        return legalMoves.FirstOrDefault(move => move.From == squares[0] && move.To == squares[1]);
    }

    public static bool TryParseSquares(string? notation, out int[] squares)
    {
        squares = [];
        if (string.IsNullOrWhiteSpace(notation) || !MovePattern().IsMatch(notation))
        {
            return false;
        }

        squares = SquarePattern().Matches(notation).Select(match => int.Parse(match.Value)).ToArray();
        return squares.All(BoardGeometry.IsValidSquare);
    }

    [GeneratedRegex(@"^\s*\d{1,2}(\s*[-x:]\s*\d{1,2})+\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MovePattern();

    [GeneratedRegex(@"\d{1,2}", RegexOptions.CultureInvariant)]
    private static partial Regex SquarePattern();
}
