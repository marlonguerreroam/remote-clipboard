// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;

namespace RemoteClipboard.Core.Discovery;

/// <summary>
/// LAN discovery datagram (UDP, plaintext). Contains only public information. It is never a source of
/// trust: a discovered address is just a hint, and every connection is still authenticated by TLS pin.
/// </summary>
internal sealed record DiscoveryDatagram(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("t")] string Type,
    [property: JsonPropertyName("id")] Guid DeviceId,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("os")] string? Os = null,
    [property: JsonPropertyName("port")] int Port = 0,
    [property: JsonPropertyName("pairing")] bool AcceptingPairing = false)
{
    public const int CurrentVersion = 1;
    public const string Announce = "announce";
    public const string Query = "query";
}

[JsonSerializable(typeof(DiscoveryDatagram))]
internal sealed partial class DiscoveryJsonContext : JsonSerializerContext;
