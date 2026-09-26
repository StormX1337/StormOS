using Microsoft.Extensions.Logging.Abstractions;
using StormOS.Core.Analysis;
using StormOS.Core.Common;
using StormOS.Core.Optimization;
using StormOS.Core.Scan;
using StormOS.Core.Telemetry;
using StormOS.Services.Analysis;
using StormOS.Services.Scan;
using StormOS.Services.Updates;

namespace StormOS.Services.Tests;

public class RuleBasedAnalysisTests
{
    private static readonly RuleBasedAnalysisEngine Engine = new();

    [Fact]
    public async Task DetectsCpuLimitWithEvidence()
    {
        var window = new AnalysisWindow { SampleCount = 60, GameName = "Test Game", AverageGpuUsage = 55, AverageMaxCoreUsage = 97 };

        var results = await Engine.AnalyzeAsync(window, TestContext.Current.CancellationToken);

        var cpu = Assert.Single(results, r => r.Id == "cpu-limited");
        Assert.Contains(cpu.Evidence, e => e.Contains("55", StringComparison.Ordinal));
        Assert.Equal(Confidence.High, cpu.Confidence);
        Assert.Null(cpu.Action);
        Assert.False(string.IsNullOrWhiteSpace(cpu.Why));
    }

    [Fact]
    public async Task ProducesNothingWithoutEnoughSamples()
    {
        var window = new AnalysisWindow { SampleCount = 2, GameName = "Test", AverageGpuUsage = 10, AverageMaxCoreUsage = 100, MaxCpuTemperature = 99 };

        Assert.Empty(await Engine.AnalyzeAsync(window, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HealthyWindowHasNoRecommendations()
    {
        var window = new AnalysisWindow { SampleCount = 120, GameName = "Test", AverageGpuUsage = 90, AverageMaxCoreUsage = 70, AverageRamUsage = 50, MaxCpuTemperature = 70, MaxGpuTemperature = 70, AverageLatencyMs = 20 };

        Assert.Empty(await Engine.AnalyzeAsync(window, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ActionsReferenceReversibleRulesOnly()
    {
        var window = new AnalysisWindow { SampleCount = 60, GameName = "Test", PowerPlan = "Power saver", AverageGpuUsage = 90, AverageMaxCoreUsage = 50 };

        var results = await Engine.AnalyzeAsync(window, TestContext.Current.CancellationToken);

        var power = Assert.Single(results, r => r.Id == "power-saver");
        Assert.Equal("power.plan", power.Action?.RuleId);
    }

    [Fact]
    public void WindowBuilderAveragesOnlyMeasuredValues()
    {
        var start = DateTimeOffset.UnixEpoch;
        var snapshots = Enumerable.Range(0, 10).Select(i => new MetricsSnapshot
        {
            Timestamp = start.AddSeconds(i),
            Cpu = new CpuMetrics { Usage = Reading.Of(i < 5 ? 20 : 40), TemperatureCelsius = Reading.Unavailable("no sensor") },
            Memory = new MemoryMetrics { TotalBytes = 100, AvailableBytes = 50 },
        }).ToList();

        var window = AnalysisWindowBuilder.Build(snapshots);

        Assert.Equal(10, window.SampleCount);
        Assert.Equal(30, window.AverageCpuUsage);
        Assert.Null(window.MaxCpuTemperature);
        Assert.Null(window.AverageGpuUsage);
        Assert.Equal(start.AddSeconds(9), window.To);
    }
}

public class SystemScanServiceTests
{
    [Fact]
    public async Task FailingAreaIsReportedAndOthersStillRun()
    {
        var service = new SystemScanService([new ThrowingCheck(), new FindingCheck()], NullLogger<SystemScanService>.Instance);

        var result = await service.RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(result.Checks, c => c.Name == "Broken" && !c.Passed);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(ScanSeverity.Warning, result.Overall);
        Assert.NotEmpty(finding.Evidence);
    }

    [Fact]
    public async Task EmptyScanIsHealthy()
    {
        var result = await new SystemScanService([], NullLogger<SystemScanService>.Instance).RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ScanSeverity.Healthy, result.Overall);
    }

    private sealed class ThrowingCheck : ISystemScanCheck
    {
        public string Area => "Broken";

        public Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("WMI unavailable");
    }

    private sealed class FindingCheck : ISystemScanCheck
    {
        public string Area => "Storage";

        public Task<(IReadOnlyList<ScanCheck> Checks, IReadOnlyList<ScanFinding> Findings)> RunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<(IReadOnlyList<ScanCheck>, IReadOnlyList<ScanFinding>)>((
                [new ScanCheck("Free space", false, "C: 6 % free")],
                [new ScanFinding { Id = "disk-space", Area = Area, Severity = ScanSeverity.Warning, Title = "Low disk space", Evidence = ["C: 12 GB free of 200 GB"], Risk = RiskLevel.None }]));
    }
}

public class UpdateVersionTests
{
    [Theory]
    [InlineData("1.2.0", "1.1.9", 1)]
    [InlineData("1.2.0", "1.2.0", 0)]
    [InlineData("1.2.0-beta.1", "1.2.0", -1)]
    [InlineData("1.2.0-beta.2", "1.2.0-beta.1", 1)]
    [InlineData("2.0", "10.0", -1)]
    [InlineData("garbage", "0.0.1", -1)]
    public void ComparesSemanticVersions(string left, string right, int expected) =>
        Assert.Equal(expected, Math.Sign(UpdateService.CompareVersions(left, right)));

    [Theory]
    [InlineData("1.2.0", "https://downloads.stormos.app/StormOS-Setup-1.2.0-x64.exe", "StormOS-Setup-1.2.0.exe")]
    [InlineData("1.2.0-beta.1", "https://downloads.stormos.app/StormOS-1.2.0-x64.MSI", "StormOS-1.2.0-beta.1.msi")]
    [InlineData("1.2.0/../../x", "https://downloads.stormos.app/setup.exe?sig=abc", "StormOS-Setup-1.2.0....x.exe")]
    public void InstallerFileNameFollowsTheDownloadType(string version, string url, string expected) =>
        Assert.Equal(expected, UpdateService.InstallerFileName(version, new Uri(url)));
}
