namespace ChinookCheckers.Application.Exceptions;

/// <summary>The engine failed or produced an unusable result. Maps to HTTP 500.</summary>
public class EngineException : Exception
{
    public EngineException(string message)
        : base(message)
    {
    }

    public EngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The engine pool is not initialized or is shutting down. Maps to HTTP 503.</summary>
public sealed class EngineUnavailableException : EngineException
{
    public EngineUnavailableException(string message)
        : base(message)
    {
    }

    public EngineUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
