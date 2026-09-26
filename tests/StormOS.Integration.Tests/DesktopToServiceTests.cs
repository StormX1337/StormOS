using System.Collections.Concurrent;
using System.IO.Pipes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StormOS.Core.Common;
using StormOS.Core.Optimization;
using StormOS.Core.Telemetry;
using StormOS.Infrastructure.Ipc;
using StormOS.Optimization.Engine;
using StormOS.Performance.Frames;
using StormOS.Security.Ipc;
using StormOS.Service.Handlers;
using StormOS.Services.Client;

namespace StormOS.Integration.Tests;

/// <summary>
/// End-to-end: desktop client (StormServiceClient) → named pipe IPC → service dispatcher with the real service
/// handlers and optimization engine → rollback. Only the system-facing rule and telemetry source are in-memory.
/// </summary>
public sealed class DesktopToServiceTests
{
    [Fact]
    public async Task TrustedClient_AppliesVerifiesAndRestoresThroughTheService()
    {
        var rule = new InMemoryAdminRule();
        await using var harness = await Harness.StartAsync(ClientTrustLevel.TrustedClient, rule);
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await harness.Client.ConnectAsync(ct)).IsSuccess);
        Assert.Equal(ServiceConnectionState.Connected, harness.Client.State);
        Assert.Equal("TrustedClient", harness.Client.Hello!.ClientTrust);

        var rules = await harness.Client.GetRulesAsync(ct);
        Assert.True(rules.IsSuccess, rules.Error?.Message);
        Assert.Contains(rules.Value!, r => r.Id == rule.Id && r.RequiresAdmin);

        var detection = await harness.Client.DetectAsync(rule.Id, null, ct);
        Assert.Equal(DetectionState.Applicable, detection.Value!.State);

        var applied = await harness.Client.ApplyAsync(rule.Id, null, ct);
        Assert.True(applied.IsSuccess, applied.Error?.Message);
        Assert.Equal(OptimizationOutcome.Applied, applied.Value!.Outcome);
        Assert.Equal(RollbackStatus.Available, applied.Value.Rollback);
        Assert.True(rule.Enabled);

        var history = await harness.Client.GetOptimizationHistoryAsync(ct);
        Assert.Contains(history.Value!, r => r.Id == applied.Value.Id);

        var restored = await harness.Client.RestoreAsync(applied.Value.Id, ct);
        Assert.Equal(RollbackStatus.RolledBack, restored.Value!.Rollback);
        Assert.False(rule.Enabled);
    }

    [Fact]
    public async Task LocalUser_CanReadButCannotChangeTheSystem()
    {
        var rule = new InMemoryAdminRule();
        await using var harness = await Harness.StartAsync(ClientTrustLevel.LocalUser, rule);
        var ct = TestContext.Current.CancellationToken;
        await harness.Client.ConnectAsync(ct);

        Assert.True((await harness.Client.GetRulesAsync(ct)).IsSuccess);
        var snapshot = await harness.Client.GetSnapshotAsync(ct);
        Assert.True(snapshot.IsSuccess, snapshot.Error?.Message);
        Assert.Equal(42, snapshot.Value!.Cpu.Usage.Value);

        var apply = await harness.Client.ApplyAsync(rule.Id, null, ct);
        Assert.False(apply.IsSuccess);
        Assert.Equal(StormErrorCodes.Unauthorized, apply.Error.Code);
        Assert.False(rule.Enabled);

        var restoreAll = await harness.Client.RestoreAllAsync(ct);
        Assert.Equal(StormErrorCodes.Unauthorized, restoreAll.Error!.Code);
    }

    [Fact]
    public async Task InvalidRequestsAreRejectedWithoutSideEffects()
    {
        var rule = new InMemoryAdminRule();
        await using var harness = await Harness.StartAsync(ClientTrustLevel.TrustedClient, rule);
        var ct = TestContext.Current.CancellationToken;
        await harness.Client.ConnectAsync(ct);

        var unknown = await harness.Client.ApplyAsync("does.not-exist", null, ct);
        Assert.False(unknown.IsSuccess);
        Assert.Equal(StormErrorCodes.NotFound, unknown.Error.Code);

        var injected = await harness.Client.ApplyAsync(rule.Id, new Dictionary<string, string> { ["value"] = "x & del C:\\Windows" }, ct);
        Assert.False(injected.IsSuccess);
        Assert.Equal(StormErrorCodes.ValidationFailed, injected.Error.Code);

        Assert.False(rule.Enabled);
        Assert.DoesNotContain((await harness.Client.GetOptimizationHistoryAsync(ct)).Value!, r => r.Outcome == OptimizationOutcome.Applied);
    }

    [Fact]
    public async Task ClientReportsServiceUnavailableFriendly()
    {
        var ipc = new IpcClient(new IpcClientOptions { PipeName = "storm-absent-" + Guid.NewGuid().ToString("N")[..8], ConnectTimeout = TimeSpan.FromMilliseconds(200) }, null, NullLogger<IpcClient>.Instance);
        await using var client = new StormServiceClient(ipc, NullLogger<StormServiceClient>.Instance, "integration-tests");

        var result = await client.GetRulesAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(StormErrorCodes.ServiceUnavailable, result.Error.Code);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly IpcServer _server;
        private readonly OptimizationEngine _engine;

        private Harness(IpcServer server, OptimizationEngine engine, StormServiceClient client)
        {
            _server = server;
            _engine = engine;
            Client = client;
        }

        public StormServiceClient Client { get; }

        public static Task<Harness> StartAsync(ClientTrustLevel trust, IOptimizationRule rule)
        {
            var pipeName = "storm-it-" + Guid.NewGuid().ToString("N")[..12];
            var journal = new InMemoryJournal();
            var engine = new OptimizationEngine([rule], journal, new OptimizationExecutor(OptimizationExecutor.Service, IsElevated: true, WindowsBuild: 26100), NullLogger<OptimizationEngine>.Instance);
            var frames = new FrameCaptureCoordinator([], Options.Create(new FrameCaptureOptions()), NullLogger<FrameCaptureCoordinator>.Instance);
            IIpcOperationHandler[] handlers =
            [
                new HelloHandler(new ServiceRuntime(), frames),
                new TelemetrySnapshotHandler(new FixedTelemetry()),
                new OptimizationRulesHandler(engine),
                new OptimizationDetectHandler(engine),
                new OptimizationApplyHandler(engine),
                new OptimizationHistoryHandler(journal),
                new OptimizationRestoreHandler(engine),
                new OptimizationRestoreAllHandler(engine),
            ];
            var dispatcher = new IpcDispatcher(handlers, NullLoggerFactory.Instance);
            var server = new IpcServer(new DefaultPipeServerFactory(), new FixedIdentity(trust), dispatcher, new IpcServerOptions { PipeName = pipeName }, NullLogger<IpcServer>.Instance);
            server.Start();

            var ipc = new IpcClient(new IpcClientOptions { PipeName = pipeName }, null, NullLogger<IpcClient>.Instance);
            var client = new StormServiceClient(ipc, NullLogger<StormServiceClient>.Instance, "integration-tests");
            return Task.FromResult(new Harness(server, engine, client));
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _server.DisposeAsync();
            _engine.Dispose();
        }
    }

    private sealed class FixedIdentity(ClientTrustLevel trust) : IClientIdentityResolver
    {
        public ClientIdentity Resolve(NamedPipeServerStream pipe) => new() { ProcessId = Environment.ProcessId, UserName = "integration", Trust = trust };
    }

    private sealed class FixedTelemetry : ITelemetryHub
    {
        public MetricsSnapshot? Latest { get; } = new() { Timestamp = DateTimeOffset.UtcNow, Cpu = new CpuMetrics { Usage = Reading.Of(42) } };

        public SamplingMode Mode => SamplingMode.Monitoring;

        public IDisposable Subscribe(Action<MetricsSnapshot> handler) => new Nothing();

        public Task<MetricsSnapshot> SampleOnceAsync(CancellationToken cancellationToken = default) => Task.FromResult(Latest!);

        public void SetMode(SamplingMode mode)
        {
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class InMemoryAdminRule : IOptimizationRule
    {
        public bool Enabled { get; private set; }

        public string Id => "test.admin-toggle";

        public string Name => "Test toggle";

        public string Description => "Integration test rule.";

        public OptimizationCategory Category => OptimizationCategory.WindowsSettings;

        public RiskLevel RiskLevel => RiskLevel.Low;

        public bool RequiresAdmin => true;

        public bool CanRollback => true;

        public bool RequiresRestart => false;

        public OsSupport SupportedOs => OsSupport.Windows10OrLater;

        public IReadOnlyList<RuleParameter> Parameters { get; } = [new("value", "Optional marker.", Required: false)];

        public Task<RuleDetection> DetectAsync(OptimizationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuleDetection(Enabled ? DetectionState.AlreadyApplied : DetectionState.Applicable, Enabled ? "On" : "Off", "On", Description));

        public Task<RuleSnapshot> CaptureAsync(OptimizationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuleSnapshot { RuleId = Id, CapturedAt = DateTimeOffset.UtcNow, Description = Enabled ? "On" : "Off", Values = new Dictionary<string, string?> { ["enabled"] = Enabled ? "1" : "0" } });

        public Task ApplyAsync(OptimizationContext context, CancellationToken cancellationToken = default)
        {
            Enabled = true;
            return Task.CompletedTask;
        }

        public Task<RuleVerification> VerifyAsync(OptimizationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuleVerification(Enabled, Enabled ? "On" : "Off"));

        public Task<RuleVerification> RollbackAsync(OptimizationContext context, RuleSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Enabled = snapshot.Get("enabled") == "1";
            return Task.FromResult(new RuleVerification(true, Enabled ? "On" : "Off"));
        }
    }

    private sealed class InMemoryJournal : IOptimizationJournal
    {
        private readonly ConcurrentDictionary<Guid, OptimizationRecord> _records = new();

        public Task SaveAsync(OptimizationRecord record, CancellationToken cancellationToken = default)
        {
            _records[record.Id] = record;
            return Task.CompletedTask;
        }

        public Task<OptimizationRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_records.TryGetValue(id, out var record) ? record : null);

        public Task<IReadOnlyList<OptimizationRecord>> ListAsync(int limit = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OptimizationRecord>>(_records.Values.OrderByDescending(r => r.Timestamp).Take(limit).ToList());

        public Task<IReadOnlyList<OptimizationRecord>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OptimizationRecord>>(_records.Values.Where(r => r.Rollback == RollbackStatus.Available).ToList());
    }
}
