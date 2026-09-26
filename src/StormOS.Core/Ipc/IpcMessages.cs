using System.Text.Json;
using System.Text.Json.Serialization;

namespace StormOS.Core.Ipc;

/// <summary>Protocol constants shared by the desktop app, CLI and service.</summary>
public static class IpcProtocol
{
    /// <summary>The protocol version. Bumped on breaking changes.</summary>
    public const int Version = 1;

    /// <summary>The pipe name (without the \\.\pipe\ prefix).</summary>
    public const string PipeName = "StormOS.Service.v1";

    /// <summary>Maximum size of a single framed message in bytes.</summary>
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    /// <summary>Maximum tolerated clock skew for request timestamps.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);
}

/// <summary>Base type of all framed IPC messages.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(IpcRequest), "request")]
[JsonDerivedType(typeof(IpcResponse), "response")]
[JsonDerivedType(typeof(IpcEvent), "event")]
public abstract record IpcMessage
{
    /// <summary>Gets the time the message was created.</summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>A request from a client.</summary>
public sealed record IpcRequest : IpcMessage
{
    /// <summary>Gets the request id echoed in the response.</summary>
    public Guid RequestId { get; init; }

    /// <summary>Gets the operation name, see <see cref="IpcOperations"/>.</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>Gets the protocol version the client speaks.</summary>
    public int ProtocolVersion { get; init; } = IpcProtocol.Version;

    /// <summary>Gets the operation payload.</summary>
    public JsonElement? Payload { get; init; }
}

/// <summary>A response to a request.</summary>
public sealed record IpcResponse : IpcMessage
{
    /// <summary>Gets the id of the request being answered.</summary>
    public Guid RequestId { get; init; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the error when the operation failed.</summary>
    public IpcError? Error { get; init; }

    /// <summary>Gets the response payload.</summary>
    public JsonElement? Payload { get; init; }
}

/// <summary>A server push event for subscribed clients.</summary>
public sealed record IpcEvent : IpcMessage
{
    /// <summary>Gets the topic, see <see cref="IpcTopics"/>.</summary>
    public string Topic { get; init; } = string.Empty;

    /// <summary>Gets the event payload.</summary>
    public JsonElement? Payload { get; init; }
}

/// <summary>An IPC error. The message is user-safe; technical details stay in the service log.</summary>
/// <param name="Code">Error code, see <see cref="Common.StormErrorCodes"/>.</param>
/// <param name="Message">Friendly message.</param>
public sealed record IpcError(string Code, string Message);
