namespace ChinookCheckers.Core.Exceptions;

/// <summary>The supplied position (or move) is malformed or violates the rules. Maps to HTTP 422.</summary>
public sealed class InvalidPositionException : Exception
{
    public InvalidPositionException(string message)
        : base(message)
    {
    }

    public InvalidPositionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
