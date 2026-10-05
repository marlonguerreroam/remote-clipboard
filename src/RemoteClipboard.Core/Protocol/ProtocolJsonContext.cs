using System.Text.Json.Serialization;

namespace RemoteClipboard.Core.Protocol;

/// <summary>Source-generated (reflection-free) serializer for the wire protocol.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(ProtocolMessage))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
