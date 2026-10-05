namespace RemoteClipboard.Core.Networking;

/// <summary>What this device announces to peers (public information only).</summary>
public sealed record LocalDeviceInfo(string DisplayName, string OsDescription);
