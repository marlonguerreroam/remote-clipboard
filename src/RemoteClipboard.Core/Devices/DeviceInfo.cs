namespace RemoteClipboard.Core.Devices;

/// <summary>Public, non-secret description of a device as shown in the UI and announced to peers.</summary>
public sealed record DeviceInfo(DeviceId Id, string DisplayName, string OsDescription);
