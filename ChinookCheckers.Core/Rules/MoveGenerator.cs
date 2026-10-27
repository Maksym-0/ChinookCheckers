using ChinookCheckers.Core.Models;

namespace ChinookCheckers.Core.Rules;

/// <summary>
/// English checkers (8x8) move generator: captures are mandatory, multi-jumps must be completed,
/// a man reaching the king row ends its move.
/// </summary>
public static class MoveGenerator
{
    private static readonly Direction[] BlackManDirections = [Direction.DownLeft, Direction.DownRight];
    private static readonly Direction[] WhiteManDirections = [Direction.UpLeft, Direction.UpRight];
    private static readonly Direction[] KingDirections =
        [Direction.UpLeft, Direction.UpRight, Direction.DownLeft, Direction.DownRight];

    public static IReadOnlyList<Move> GenerateLegalMoves(Position position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var side = position.SideToMove;
        var board = position.ToArray();
        var captures = new List<Move>();

        for (var square = 1; square <= BoardGeometry.SquareCount; square++)
        {
            var piece = board[square - 1];
            if (!piece.BelongsTo(side))
            {
                continue;
            }

            // The jumping piece leaves its origin, so a king may pass through or land on it again.
            board[square - 1] = Piece.Empty;
            CollectCaptures(board, piece, side, square, [square], [], captures);
            board[square - 1] = piece;
        }

        return captures.Count > 0 ? captures : GenerateSimpleMoves(board, side);
    }

    public static bool IsLegal(Position position, Move move) =>
        GenerateLegalMoves(position).Any(candidate => candidate.Path.SequenceEqual(move.Path));

    private static List<Move> GenerateSimpleMoves(Piece[] board, PieceColor side)
    {
        var moves = new List<Move>();
        for (var square = 1; square <= BoardGeometry.SquareCount; square++)
        {
            var piece = board[square - 1];
            if (!piece.BelongsTo(side))
            {
                continue;
            }

            foreach (var direction in DirectionsFor(piece))
            {
                var target = BoardGeometry.Neighbor(square, direction);
                if (target != 0 && board[target - 1] == Piece.Empty)
                {
                    moves.Add(new Move([square, target], []));
                }
            }
        }

        return moves;
    }

    private static void CollectCaptures(
        Piece[] board,
        Piece piece,
        PieceColor side,
        int current,
        List<int> path,
        List<int> captured,
        List<Move> result)
    {
        var opponent = side.Opponent();
        var extended = false;

        foreach (var direction in DirectionsFor(piece))
        {
            var over = BoardGeometry.Neighbor(current, direction);
            if (over == 0 || !board[over - 1].BelongsTo(opponent) || captured.Contains(over))
            {
                continue;
            }

            var landing = BoardGeometry.Neighbor(over, direction);
            if (landing == 0 || board[landing - 1] != Piece.Empty)
            {
                continue;
            }

            extended = true;
            path.Add(landing);
            captured.Add(over);

            if (!piece.IsKing() && BoardGeometry.IsPromotionSquare(landing, side))
            {
                result.Add(new Move(path.ToArray(), captured.ToArray()));
            }
            else
            {
                CollectCaptures(board, piece, side, landing, path, captured, result);
            }

            path.RemoveAt(path.Count - 1);
            captured.RemoveAt(captured.Count - 1);
        }

        if (!extended && captured.Count > 0)
        {
            result.Add(new Move(path.ToArray(), captured.ToArray()));
        }
    }

    private static Direction[] DirectionsFor(Piece piece) => piece switch
    {
        Piece.BlackMan => BlackManDirections,
        Piece.WhiteMan => WhiteManDirections,
        _ => KingDirections
    };
}
