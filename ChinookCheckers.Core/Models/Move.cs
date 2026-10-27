namespace ChinookCheckers.Core.Models;

/// <summary>
/// A legal move in standard 1..32 square numbering. For captures <see cref="Path"/> holds every landing square.
/// </summary>
public sealed class Move
{
    public Move(IReadOnlyList<int> path, IReadOnlyList<int> captured)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(captured);
        if (path.Count < 2)
        {
            throw new ArgumentException("A move must contain at least source and destination squares.", nameof(path));
        }

        Path = path;
        Captured = captured;
    }

    public IReadOnlyList<int> Path { get; }

    public IReadOnlyList<int> Captured { get; }

    public int From => Path[0];

    public int To => Path[^1];

    public bool IsCapture => Captured.Count > 0;

    public string Notation => IsCapture ? string.Join('x', Path) : $"{From}-{To}";

    public override string ToString() => Notation;
}
