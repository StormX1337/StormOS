using StormOS.Core.Optimization;
using StormOS.Optimization.Rules.Network;
using StormOS.Optimization.Rules.Services;
using StormOS.Optimization.Rules.WindowsSettings;
using StormOS.Windows.RegistryAccess;

namespace StormOS.Optimization.Tests;

public sealed class RuleTests
{
    private static OptimizationContext Context(int build = 26100, Dictionary<string, string>? parameters = null) => new(parameters, "t", build, isElevated: true);

    [Fact]
    public void DirectXSettings_PreservesOtherKeys()
    {
        var updated = DirectXGlobalSettings.Set("VRROptimizeEnable=0;AutoHDREnable=1;", "SwapEffectUpgradeEnable", "1");

        Assert.Equal("VRROptimizeEnable=0;AutoHDREnable=1;SwapEffectUpgradeEnable=1;", updated);
        Assert.Equal("1", DirectXGlobalSettings.Get(updated, "swapeffectupgradeenable"));
        Assert.Equal("SwapEffectUpgradeEnable=0;", DirectXGlobalSettings.Set("SwapEffectUpgradeEnable=1;", "SwapEffectUpgradeEnable", "0"));
        Assert.Null(DirectXGlobalSettings.Get(null, "x"));
    }

    [Fact]
    public async Task WindowedOptimizations_DefaultDependsOnBuild_AndRestoresExactString()
    {
        var registry = new InMemoryRegistry();
        var rule = new WindowedOptimizationsRule(registry);

        Assert.Equal(DetectionState.AlreadyApplied, (await rule.DetectAsync(Context(26100), TestContext.Current.CancellationToken)).State);
        Assert.Equal(DetectionState.Applicable, (await rule.DetectAsync(Context(22631), TestContext.Current.CancellationToken)).State);

        registry.SetValue(RegistryHive.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", new RegistryValue(RegistryKind.Sz, "VRROptimizeEnable=0;"));
        var snapshot = await rule.CaptureAsync(Context(22631), TestContext.Current.CancellationToken);
        await rule.ApplyAsync(Context(22631), TestContext.Current.CancellationToken);
        Assert.True((await rule.VerifyAsync(Context(22631), TestContext.Current.CancellationToken)).Verified);

        var restored = await rule.RollbackAsync(Context(22631), snapshot, TestContext.Current.CancellationToken);
        Assert.True(restored.Verified);
        Assert.Equal("VRROptimizeEnable=0;", registry.GetValue(RegistryHive.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings")!.Value);
    }

    [Fact]
    public void RegistryValue_SerializationRoundTrips()
    {
        RegistryValue[] values =
        [
            new(RegistryKind.DWord, 42),
            new(RegistryKind.QWord, 1L << 40),
            new(RegistryKind.Sz, "text"),
            new(RegistryKind.Binary, new byte[] { 2, 0, 0, 0, 9 }),
            new(RegistryKind.MultiSz, new[] { "a", "b" }),
        ];

        foreach (var value in values)
        {
            Assert.Equal(value, RegistryValue.Deserialize(value.Kind, value.Serialize()));
        }
    }

    [Fact]
    public void ServiceKnowledgeBase_NeverAllowsSecurityServices()
    {
        var kb = ServiceKnowledgeBase.Default;

        foreach (var name in new[] { "WinDefend", "mpssvc", "BFE", "wuauserv", "XblAuthManager", "GamingServices" })
        {
            Assert.Empty(kb.Find(name)!.AllowedStartTypes);
        }

        Assert.Contains("manual", kb.Find("DiagTrack")!.AllowedStartTypes);
        Assert.Null(kb.Find("SomethingUnknown"));
    }

    [Fact]
    public async Task ServiceRule_RefusesServicesOutsideKnowledgeBase()
    {
        var rule = new ServiceStartTypeRule(new ThrowingServices(), ServiceKnowledgeBase.Default);

        var detection = await rule.DetectAsync(Context(parameters: new() { ["service"] = "WinDefend", ["startType"] = "disabled" }), TestContext.Current.CancellationToken);
        var unknown = await rule.DetectAsync(Context(parameters: new() { ["service"] = "LanmanServer", ["startType"] = "disabled" }), TestContext.Current.CancellationToken);

        Assert.Equal(DetectionState.NotApplicable, detection.State);
        Assert.Equal(DetectionState.NotApplicable, unknown.State);
    }

    [Theory]
    [InlineData("1.1.1.1", true)]
    [InlineData("1.1.1.1, 1.0.0.1", true)]
    [InlineData("1.1.1.1,1.0.0.1,8.8.8.8", false)]
    [InlineData("2606:4700:4700::1111", false)]
    [InlineData("dns.google", false)]
    [InlineData("", false)]
    public void DnsRule_ServerValidation(string value, bool valid)
    {
        if (valid)
        {
            Assert.NotEmpty(DnsServersRule.ParseServers(value));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => DnsServersRule.ParseServers(value));
        }
    }

    [Fact]
    public async Task DnsRule_RevertsToDhcp()
    {
        var dns = new FakeDns();
        var rule = new DnsServersRule(dns);
        var context = Context(parameters: new() { ["interfaceId"] = "{8A1C0000-0000-0000-0000-000000000001}", ["servers"] = "1.1.1.1,1.0.0.1" });

        var snapshot = await rule.CaptureAsync(context, TestContext.Current.CancellationToken);
        await rule.ApplyAsync(context, TestContext.Current.CancellationToken);
        Assert.True((await rule.VerifyAsync(context, TestContext.Current.CancellationToken)).Verified);
        var restored = await rule.RollbackAsync(context, snapshot, TestContext.Current.CancellationToken);

        Assert.True(restored.Verified);
        Assert.Empty(dns.Servers);
    }

    [Theory]
    [InlineData("8a1c0000-0000-0000-0000-000000000001", "{8A1C0000-0000-0000-0000-000000000001}")]
    [InlineData("{8a1c0000-0000-0000-0000-000000000001}", "{8A1C0000-0000-0000-0000-000000000001}")]
    [InlineData("'; DROP", null)]
    public void DnsConfigurator_NormalizesInterfaceIds(string input, string? expected) => Assert.Equal(expected, WindowsDnsConfigurator.Normalize(input));

    [Fact]
    public void NameServer_Parsing() => Assert.Equal(["1.1.1.1", "8.8.8.8"], WindowsDnsConfigurator.ParseNameServer("1.1.1.1,8.8.8.8"));

    private sealed class FakeDns : IDnsConfigurator
    {
        public IReadOnlyList<string> Servers { get; private set; } = [];

        public DnsConfiguration? Read(string interfaceId) => new(Servers, Servers.Count > 0);

        public void Write(string interfaceId, IReadOnlyList<string> servers) => Servers = servers.ToList();
    }

    private sealed class ThrowingServices : StormOS.Windows.Services.IServiceConfigurator
    {
        public StormOS.Windows.Services.ServiceStartKind? GetStartKind(string serviceName) => throw new InvalidOperationException("must not be called");

        public void SetStartKind(string serviceName, StormOS.Windows.Services.ServiceStartKind kind) => throw new InvalidOperationException("must not be called");

        public IReadOnlyList<StormOS.Core.Ipc.ServiceEntry> List() => [];
    }
}
