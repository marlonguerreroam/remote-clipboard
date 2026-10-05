using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RemoteClipboard.App.Services;

internal static class NetworkInfo
{
    /// <summary>IPv4 addresses of active, non-loopback adapters (what the other device should type).</summary>
    public static IReadOnlyList<string> LocalIPv4Addresses()
    {
        try
        {
            return [.. NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Distinct()];
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }
}
