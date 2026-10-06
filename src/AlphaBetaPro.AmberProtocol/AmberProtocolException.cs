namespace AlphaBetaPro.AmberProtocol;

public sealed class AmberProtocolException : Exception
{
    public AmberProtocolException(string message) : base(message)
    {
    }
}

public sealed class AmberConnectionException : Exception
{
    public AmberConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
