using System.Globalization;
using System.Net;
using RemoteClipboard.Core.Protocol;

namespace RemoteClipboard.Core.Networking;

/// <summary>Parses user input such as "192.168.1.20", "192.168.1.20:47801", "[fe80::1]:47800" or "SERVIDOR".</summary>
public static class EndpointParser
{
    public static bool TryParse(string? input, out string host, out int port)
    {
        host = string.Empty;
        port = ProtocolLimits.DefaultTcpPort;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text) || text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        // IPAddress.TryParse would accept "[fe80::1]:47802" but drop the port, so try the endpoint form first.
        if (IPEndPoint.TryParse(text, out var endpoint))
        {
            host = endpoint.Address.ToString();
            port = endpoint.Port > 0 ? endpoint.Port : ProtocolLimits.DefaultTcpPort;
            return true;
        }

        // Host name, optionally with :port.
        var colon = text.LastIndexOf(':');
        if (colon > 0 && text.IndexOf(':', StringComparison.Ordinal) == colon)
        {
            if (!int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
            {
                return false;
            }

            text = text[..colon];
        }

        if (Uri.CheckHostName(text) is UriHostNameType.Dns)
        {
            host = text;
            return true;
        }

        return false;
    }
}
