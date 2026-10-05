namespace RemoteClipboard.Core.Protocol;

/// <summary>The peer sent data that violates the wire protocol. The connection must be closed.</summary>
public sealed class ProtocolException : Exception
{
    public ProtocolException()
    {
    }

    public ProtocolException(string message)
        : base(message)
    {
    }

    public ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
