using System.IO.Pipes;
using Microsoft.Extensions.Logging.Abstractions;
using StormOS.Core.Common;
using StormOS.Core.Ipc;
using StormOS.Infrastructure.Ipc;
using StormOS.Security.Ipc;

namespace StormOS.Infrastructure.Tests;

public sealed class IpcRoundTripTests
{
    [Fact]
    public async Task Request_Response_Event_AndAuthorization()
    {
        var pipeName = "storm-test-" + Guid.NewGuid().ToString("N")[..12];
        var dispatcher = new IpcDispatcher([new HelloHandler(), new ApplyHandler()], NullLoggerFactory.Instance);
        await using var server = new IpcServer(new DefaultPipeServerFactory(), new FixedIdentity(ClientTrustLevel.LocalUser), dispatcher, new IpcServerOptions { PipeName = pipeName }, NullLogger<IpcServer>.Instance);
        server.Start();

        await using var client = new IpcClient(new IpcClientOptions { PipeName = pipeName }, null, NullLogger<IpcClient>.Instance);
        var events = new List<IpcEvent>();
        client.EventReceived += (_, e) => { lock (events) { events.Add(e); } };

        var hello = await client.RequestAsync<HelloResponse>(IpcOperations.Hello, new HelloRequest("tests", "1"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(hello.IsSuccess, hello.Error?.Message);
        Assert.Equal("LocalUser", hello.Value!.ClientTrust);

        var denied = await client.RequestAsync<object>(IpcOperations.OptimizationApply, new RuleRequest("windows.game-mode", null), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(denied.IsSuccess);
        Assert.Equal(StormErrorCodes.Unauthorized, denied.Error!.Code);

        var invalid = await client.RequestAsync<HelloResponse>(IpcOperations.Hello, null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(StormErrorCodes.ValidationFailed, invalid.Error!.Code);

        var unknown = await client.RequestAsync<object>("registry.write", null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(StormErrorCodes.UnknownOperation, unknown.Error!.Code);

        for (var i = 0; i < 50 && events.Count == 0; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        lock (events)
        {
            Assert.Contains(events, e => e.Topic == "test.greeting");
        }
    }

    [Fact]
    public async Task Client_ServiceNotRunning_ReturnsFriendlyError()
    {
        await using var client = new IpcClient(new IpcClientOptions { PipeName = "storm-missing-" + Guid.NewGuid().ToString("N")[..8], ConnectTimeout = TimeSpan.FromMilliseconds(200) }, null, NullLogger<IpcClient>.Instance);

        var result = await client.RequestAsync<HelloResponse>(IpcOperations.Hello, new HelloRequest("t", "1"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(StormErrorCodes.ServiceUnavailable, result.Error!.Code);
        Assert.DoesNotContain("Exception", result.Error.Message, StringComparison.Ordinal);
    }

    private sealed class FixedIdentity(ClientTrustLevel trust) : IClientIdentityResolver
    {
        public ClientIdentity Resolve(NamedPipeServerStream pipe) => new() { ProcessId = Environment.ProcessId, UserName = "test", Trust = trust };
    }

    private sealed class HelloHandler : IpcHandler<HelloRequest>
    {
        public override string Operation => IpcOperations.Hello;

        protected override string? Validate(HelloRequest payload) => string.IsNullOrWhiteSpace(payload.ClientName) ? "Client name is required." : null;

        protected override async Task<object?> HandleAsync(HelloRequest payload, IIpcSession session, CancellationToken cancellationToken)
        {
            await session.SendEventAsync("test.greeting", new { hello = payload.ClientName });
            return new HelloResponse { ServiceVersion = "test", ProtocolVersion = IpcProtocol.Version, ClientTrust = session.Client.Trust.ToString() };
        }
    }

    private sealed class ApplyHandler : IpcHandler<RuleRequest>
    {
        public override string Operation => IpcOperations.OptimizationApply;

        protected override Task<object?> HandleAsync(RuleRequest payload, IIpcSession session, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Must never run for untrusted clients.");
    }
}
