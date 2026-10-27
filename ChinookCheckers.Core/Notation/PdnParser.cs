using ChinookCheckers.Core.Exceptions;
using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Rules;

namespace ChinookCheckers.Core.Notation;

/// <summary>
/// Parses PDN FEN positions such as <c>B:W18,19,K22:B1,5-7</c> (optionally wrapped in <c>[FEN "..."]</c>).
/// </summary>
public static class PdnParser
{
    private const int MaxPiecesPerSide = 12;

    public static Position Parse(string? pdn)
    {
        if (string.IsNullOrWhiteSpace(pdn))
        {
            throw new InvalidPositionException("Position must not be empty.");
        }

        var fen = Unwrap(pdn);
        var sections = fen.Split(':', StringSplitOptions.TrimEntries);
        if (sections.Length != 3)
        {
            throw new InvalidPositionException(
                $"Position '{pdn}' must have the form '<side>:W<squares>:B<squares>'.");
        }

        var sideToMove = ParseColor(sections[0], "side to move");
        var squares = new Piece[BoardGeometry.SquareCount];
        var seenColors = new HashSet<PieceColor>();

        foreach (var section in sections.AsSpan(1))
        {
            if (section.Length == 0)
            {
                throw new InvalidPositionException("Piece list section must start with 'W' or 'B'.");
            }

            var color = ParseColor(section[..1], "piece list");
            if (!seenColors.Add(color))
            {
                throw new InvalidPositionException($"Piece list for {color} is specified more than once.");
            }

            ParsePieceList(section[1..], color, squares);
        }

        Validate(squares);
        return new Position(squares, sideToMove);
    }

    public static bool TryParse(string? pdn, out Position? position)
    {
        try
        {
            position = Parse(pdn);
            return true;
        }
        catch (InvalidPositionException)
        {
            position = null;
            return false;
        }
    }

    private static string Unwrap(string pdn)
    {
        var text = pdn.Trim();
        if (text.StartsWith("[FEN", StringComparison.OrdinalIgnoreCase) && text.EndsWith(']'))
        {
            text = text[4..^1].Trim();
        }

        return text.Trim('"').TrimEnd('.').Trim();
    }

    private static PieceColor ParseColor(string token, string context) => token.ToUpperInvariant() switch
    {
        "B" => PieceColor.Black,
        "W" => PieceColor.White,
        _ => throw new InvalidPositionException($"Invalid {context} '{token}': expected 'W' or 'B'.")
    };

    private static void ParsePieceList(string list, PieceColor color, Piece[] squares)
    {
        foreach (var rawEntry in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var isKing = rawEntry.StartsWith('K') || rawEntry.StartsWith('k');
            var entry = isKing ? rawEntry[1..] : rawEntry;
            var (first, last) = ParseRange(entry, rawEntry);

            for (var square = first; square <= last; square++)
            {
                if (squares[square - 1] != Piece.Empty)
                {
                    throw new InvalidPositionException($"Square {square} is occupied more than once.");
                }

                squares[square - 1] = (color, isKing) switch
                {
                    (PieceColor.Black, false) => Piece.BlackMan,
                    (PieceColor.Black, true) => Piece.BlackKing,
                    (PieceColor.White, false) => Piece.WhiteMan,
                    _ => Piece.WhiteKing
                };
            }
        }
    }

    private static (int First, int Last) ParseRange(string entry, string rawEntry)
    {
        var bounds = entry.Split('-', StringSplitOptions.TrimEntries);
        if (bounds.Length is < 1 or > 2
            || !int.TryParse(bounds[0], out var first)
            || !int.TryParse(bounds[^1], out var last)
            || !BoardGeometry.IsValidSquare(first)
            || !BoardGeometry.IsValidSquare(last)
            || first > last)
        {
            throw new InvalidPositionException($"Invalid square '{rawEntry}': squares must be in range 1..32.");
        }

        return (first, last);
    }

    private static void Validate(Piece[] squares)
    {
        var black = squares.Count(piece => piece.BelongsTo(PieceColor.Black));
        var white = squares.Count(piece => piece.BelongsTo(PieceColor.White));

        if (black > MaxPiecesPerSide || white > MaxPiecesPerSide)
        {
            throw new InvalidPositionException(
                $"Each side may have at most {MaxPiecesPerSide} pieces (black: {black}, white: {white}).");
        }

        if (black + white == 0)
        {
            throw new InvalidPositionException("Position contains no pieces.");
        }

        for (var square = 1; square <= BoardGeometry.SquareCount; square++)
        {
            var piece = squares[square - 1];
            if ((piece == Piece.BlackMan && BoardGeometry.IsPromotionSquare(square, PieceColor.Black))
                || (piece == Piece.WhiteMan && BoardGeometry.IsPromotionSquare(square, PieceColor.White)))
            {
                throw new InvalidPositionException($"An uncrowned man cannot stand on its king row (square {square}).");
            }
        }
    }
}
