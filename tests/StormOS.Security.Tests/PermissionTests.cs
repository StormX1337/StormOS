using StormOS.Core.Common;
using StormOS.Core.Ipc;
using StormOS.Security.Ipc;

namespace StormOS.Security.Tests;

public sealed class PermissionTests
{
    private static ClientIdentity Client(ClientTrustLevel trust) => new() { ProcessId = 1234, UserName = "PC\\user", Trust = trust };

    [Fact]
    public void UnknownOperation_IsRejected()
    {
        var result = OperationPolicies.Authorize("registry.write", Client(ClientTrustLevel.TrustedClient));

        Assert.False(result.IsSuccess);
        Assert.Equal(StormErrorCodes.UnknownOperation, result.Error!.Code);
    }

    [Theory]
    [InlineData(IpcOperations.OptimizationApply)]
    [InlineData(IpcOperations.OptimizationRestore)]
    [InlineData(IpcOperations.OptimizationRestoreAll)]
    [InlineData(IpcOperations.ProcessTerminate)]
    [InlineData(IpcOperations.ProcessSetPriority)]
    [InlineData(IpcOperations.FramesStart)]
    public void MutatingOperations_RequireTrustedClient(string operation)
    {
        Assert.False(OperationPolicies.Authorize(operation, Client(ClientTrustLevel.LocalUser)).IsSuccess);
        Assert.False(OperationPolicies.Authorize(operation, Client(ClientTrustLevel.Untrusted)).IsSuccess);
        Assert.True(OperationPolicies.Authorize(operation, Client(ClientTrustLevel.TrustedClient)).IsSuccess);
    }

    [Fact]
    public void ReadOnlyOperations_AllowLocalUser()
    {
        Assert.True(OperationPolicies.Authorize(IpcOperations.TelemetrySubscribe, Client(ClientTrustLevel.LocalUser)).IsSuccess);
        Assert.False(OperationPolicies.Authorize(IpcOperations.TelemetrySubscribe, Client(ClientTrustLevel.Untrusted)).IsSuccess);
        Assert.True(OperationPolicies.Authorize(IpcOperations.Health, Client(ClientTrustLevel.Untrusted)).IsSuccess);
    }

    [Fact]
    public void EveryDeclaredOperation_HasPolicy()
    {
        var declared = typeof(IpcOperations).GetFields().Select(f => (string)f.GetValue(null)!).ToList();
        Assert.All(declared, op => Assert.True(OperationPolicies.TryGet(op, out _), op));
    }

    [Fact]
    public void TrustEvaluator_RequiresInstallDirectoryAndKnownImage()
    {
        var install = Path.Combine(Path.GetTempPath(), "StormOS-install");
        var evaluator = new ClientTrustEvaluator(install, ["StormOS.exe", "storm.exe"], null, null);

        Assert.Equal(ClientTrustLevel.TrustedClient, evaluator.Evaluate(Path.Combine(install, "StormOS.exe"), userResolved: true));
        Assert.Equal(ClientTrustLevel.LocalUser, evaluator.Evaluate(Path.Combine(install, "other.exe"), userResolved: true));
        Assert.Equal(ClientTrustLevel.LocalUser, evaluator.Evaluate(Path.Combine(Path.GetTempPath(), "StormOS.exe"), userResolved: true));
        Assert.Equal(ClientTrustLevel.Untrusted, evaluator.Evaluate(Path.Combine(install, "StormOS.exe"), userResolved: false));
        Assert.Equal(ClientTrustLevel.Untrusted, evaluator.Evaluate(null, userResolved: true));
    }

    [Fact]
    public void TrustEvaluator_SignedService_RequiresMatchingSigner()
    {
        var install = Path.Combine(Path.GetTempPath(), "StormOS-install");
        var good = Path.Combine(install, "StormOS.exe");
        var verifier = new FakeVerifier(new Dictionary<string, string?> { [Path.GetFullPath(good)] = "ABC" });

        Assert.Equal(ClientTrustLevel.TrustedClient, new ClientTrustEvaluator(install, ["StormOS.exe"], verifier, "abc").Evaluate(good, true));
        Assert.Equal(ClientTrustLevel.LocalUser, new ClientTrustEvaluator(install, ["StormOS.exe"], verifier, "DEF").Evaluate(good, true));
    }

    [Fact]
    public void TokenBucket_LimitsBurstAndRefills()
    {
        var time = new ManualTime();
        var bucket = new TokenBucket(5, 1, time);

        Assert.True(bucket.TryConsume(5));
        Assert.False(bucket.TryConsume());
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(bucket.TryConsume(2));
        Assert.False(bucket.TryConsume());
    }

    private sealed class FakeVerifier(Dictionary<string, string?> map) : IAuthenticodeVerifier
    {
        public string? GetTrustedSignerThumbprint(string filePath) => map.GetValueOrDefault(filePath);
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
