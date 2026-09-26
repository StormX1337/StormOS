using System.Buffers.Binary;
using StormOS.Core.Ipc;
using StormOS.Infrastructure.Ipc;

namespace StormOS.Infrastructure.Tests;

public sealed class IpcFramingTests
{
    [Fact]
    public async Task RoundTrip_PreservesPolymorphicMessages()
    {
        using var stream = new MemoryStream();
        var request = new IpcRequest { RequestId = Guid.NewGuid(), Operation = IpcOperations.Hello, Payload = IpcPayload.From(new HelloRequest("test", "1.0")), Timestamp = DateTimeOffset.UtcNow };
        var evt = new IpcEvent { Topic = IpcTopics.Telemetry, Timestamp = DateTimeOffset.UtcNow };

        await IpcFraming.WriteAsync(stream, request, TestContext.Current.CancellationToken);
        await IpcFraming.WriteAsync(stream, evt, TestContext.Current.CancellationToken);
        stream.Position = 0;

        var first = Assert.IsType<IpcRequest>(await IpcFraming.ReadAsync(stream, TestContext.Current.CancellationToken));
        Assert.Equal(request.RequestId, first.RequestId);
        Assert.Equal("test", IpcPayload.To<HelloRequest>(first.Payload)!.ClientName);
        Assert.IsType<IpcEvent>(await IpcFraming.ReadAsync(stream, TestContext.Current.CancellationToken));
        Assert.Null(await IpcFraming.ReadAsync(stream, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(IpcProtocol.MaxMessageBytes + 1)]
    public async Task Read_RejectsInvalidLength(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(() => IpcFraming.ReadAsync(stream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Read_RejectsGarbageAndUnknownKinds()
    {
        foreach (var body in new[] { "not json", """{"kind":"evil","timestamp":"2024-01-01T00:00:00Z"}""" })
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            var frame = new byte[4 + bytes.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, bytes.Length);
            bytes.CopyTo(frame, 4);
            using var stream = new MemoryStream(frame);
            await Assert.ThrowsAsync<InvalidDataException>(() => IpcFraming.ReadAsync(stream, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Read_TruncatedHeader_Throws()
    {
        using var stream = new MemoryStream([1, 2]);
        await Assert.ThrowsAsync<InvalidDataException>(() => IpcFraming.ReadAsync(stream, TestContext.Current.CancellationToken));
    }
}
