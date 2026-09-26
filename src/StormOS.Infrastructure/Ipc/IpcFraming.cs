using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;
using StormOS.Core.Common;
using StormOS.Core.Ipc;

namespace StormOS.Infrastructure.Ipc;

/// <summary>Length-prefixed (4 byte little endian) UTF-8 JSON framing.</summary>
public static class IpcFraming
{
    private const int HeaderSize = 4;

    /// <summary>Serializes and writes a message as a single frame.</summary>
    /// <param name="stream">Target stream.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <exception cref="InvalidDataException">The message exceeds <see cref="IpcProtocol.MaxMessageBytes"/>.</exception>
    public static async Task WriteAsync(Stream stream, IpcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, StormJson.Options);
        if (payload.Length > IpcProtocol.MaxMessageBytes)
        {
            throw new InvalidDataException($"IPC message of {payload.Length} bytes exceeds the {IpcProtocol.MaxMessageBytes} byte limit.");
        }

        var frame = ArrayPool<byte>.Shared.Rent(HeaderSize + payload.Length);
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
            payload.CopyTo(frame, HeaderSize);
            await stream.WriteAsync(frame.AsMemory(0, HeaderSize + payload.Length), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frame);
        }
    }

    /// <summary>Reads the next frame.</summary>
    /// <param name="stream">Source stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message, or <see langword="null"/> when the peer closed the stream cleanly.</returns>
    /// <exception cref="InvalidDataException">The frame is malformed or too large.</exception>
    public static async Task<IpcMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderSize];
        var read = await stream.ReadAtLeastAsync(header, HeaderSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        if (read == 0)
        {
            return null;
        }

        if (read < HeaderSize)
        {
            throw new InvalidDataException("The IPC stream ended inside a frame header.");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > IpcProtocol.MaxMessageBytes)
        {
            throw new InvalidDataException($"Invalid IPC frame length {length}.");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            await stream.ReadExactlyAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            try
            {
                return JsonSerializer.Deserialize<IpcMessage>(buffer.AsSpan(0, length), StormJson.Options)
                    ?? throw new InvalidDataException("The IPC frame contained a null message.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The IPC frame is not a valid message.", ex);
            }
            catch (NotSupportedException ex)
            {
                throw new InvalidDataException("The IPC frame has an unknown message kind.", ex);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>Converts between typed DTOs and JSON payload elements.</summary>
public static class IpcPayload
{
    /// <summary>Serializes a value into a JSON element.</summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The element, or <see langword="null"/> for null values.</returns>
    public static JsonElement? From<T>(T? value) =>
        value is null ? null : JsonSerializer.SerializeToElement(value, value.GetType(), StormJson.Options);

    /// <summary>Deserializes a payload.</summary>
    /// <typeparam name="T">Target type.</typeparam>
    /// <param name="payload">The payload.</param>
    /// <returns>The value, or default when absent.</returns>
    /// <exception cref="JsonException">The payload does not match the type.</exception>
    public static T? To<T>(JsonElement? payload) =>
        payload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } element
            ? element.Deserialize<T>(StormJson.Options)
            : default;
}
