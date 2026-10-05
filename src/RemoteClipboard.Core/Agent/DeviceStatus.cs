using RemoteClipboard.Core.Devices;

namespace RemoteClipboard.Core.Agent;

/// <summary>A paired device and its live state, for the UI.</summary>
public sealed record DeviceStatus(PairedDevice Device, bool IsConnected, string? Address);
