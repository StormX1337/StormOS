using System.Buffers.Binary;
using System.Text;
using StormOS.Core.Hardware;
using StormOS.Hardware.Cpu;
using StormOS.Hardware.Gpu;
using StormOS.Hardware.Inventory;
using StormOS.Windows.RegistryAccess;
using StormOS.Windows.Startup;

namespace StormOS.Hardware.Tests;

public class GpuEngineTests
{
    private const string Engine3D = "pid_1234_luid_0x00000000_0x0000D1A5_phys_0_eng_0_engtype_3D";

    [Fact]
    public void ParsesEngineInstanceNames()
    {
        Assert.True(GpuEngineInstance.TryParse(Engine3D, out var engine));
        Assert.Equal(1234, engine!.ProcessId);
        Assert.Equal(0xD1A5, engine.AdapterLuid);
        Assert.Equal("3D", engine.EngineType);

        Assert.False(GpuEngineInstance.TryParse("_Total", out _));
        Assert.True(GpuEngineInstance.TryParseAdapter("luid_0x00000001_0x00000002_phys_0", out var luid));
        Assert.Equal((1L << 32) | 2, luid);
    }

    [Fact]
    public void AggregatesPerEngineSumsAndTakesBusiestEngine()
    {
        var result = GpuEngineAggregator.Compute(
        [
            new(Engine3D, 40),
            new("pid_5678_luid_0x00000000_0x0000D1A5_phys_0_eng_0_engtype_3D", 30),
            new("pid_5678_luid_0x00000000_0x0000D1A5_phys_0_eng_3_engtype_VideoDecode", 90),
            new("pid_9_luid_0x00000000_0x0000D1A5_phys_0_eng_1_engtype_Copy", 0),
            new("garbage", 50),
        ]);

        Assert.Equal(90, result.ByAdapter[0xD1A5]);
        Assert.Equal(70, result.ByAdapter3D[0xD1A5]);
        Assert.Equal(40, result.ByProcess[1234]);
        Assert.Equal(90, result.ByProcess[5678]);
        Assert.False(result.ByProcess.ContainsKey(9));
    }

    [Fact]
    public void CapsEngineUtilizationAt100()
    {
        var result = GpuEngineAggregator.Compute([new(Engine3D, 80), new("pid_2_luid_0x00000000_0x0000D1A5_phys_0_eng_0_engtype_3D", 60)]);

        Assert.Equal(100, result.ByAdapter3D[0xD1A5]);
    }

    [Fact]
    public void D3dkmtRejectsUnsupportedValues()
    {
        var perf = new D3dkmt.AdapterPerfData { Temperature = 0, FanRpm = 0, MemoryFrequency = 0, Power = 0 };
        var readings = D3dkmtAdapterTelemetry.Interpret(perf, new D3dkmt.AdapterPerfDataCaps(), null);

        Assert.False(readings.TemperatureCelsius.IsAvailable);
        Assert.False(readings.FanRpm.IsAvailable);
        Assert.False(readings.MemoryClockMhz.IsAvailable);
        Assert.False(readings.CoreClockMhz.IsAvailable);
        Assert.False(readings.PowerPercent.IsAvailable);
        Assert.NotNull(readings.TemperatureCelsius.UnavailableReason);
    }

    [Fact]
    public void D3dkmtConvertsReportedValues()
    {
        var perf = new D3dkmt.AdapterPerfData { Temperature = 655, FanRpm = 1500, MemoryFrequency = 10_501_000_000, Power = 875 };
        var caps = new D3dkmt.AdapterPerfDataCaps { MaxFanRpm = 3000, TemperatureMax = 1000 };
        var node = new D3dkmt.NodePerfData { Frequency = 2_520_000_000 };

        var readings = D3dkmtAdapterTelemetry.Interpret(perf, caps, node);

        Assert.Equal(65.5, readings.TemperatureCelsius.Value);
        Assert.Equal(1500, readings.FanRpm.Value);
        Assert.Equal(10501, readings.MemoryClockMhz.Value);
        Assert.Equal(2520, readings.CoreClockMhz.Value);
        Assert.Equal(87.5, readings.PowerPercent.Value);
    }
}

public class CpuParsingTests
{
    [Theory]
    [InlineData("0,3", true, 0, 3)]
    [InlineData("1,12", true, 1, 12)]
    [InlineData("_Total", false, 0, 0)]
    [InlineData("0,_Total", false, 0, 0)]
    public void ParsesCoreInstances(string instance, bool ok, int group, int index)
    {
        Assert.Equal(ok, CpuMetricCollector.TryParseCoreInstance(instance, out var g, out var i));
        if (ok)
        {
            Assert.Equal(group, g);
            Assert.Equal(index, i);
        }
    }

    [Fact]
    public void ConvertsThermalZoneKelvin()
    {
        Assert.Equal(46.85, CpuMetricCollector.KelvinToCelsius(320, highPrecision: false), 2);
        Assert.Equal(46.85, CpuMetricCollector.KelvinToCelsius(3200, highPrecision: true), 2);
    }
}

public class InventoryParserTests
{
    [Fact]
    public void MapsWmiCodes()
    {
        Assert.Equal(StorageMediaType.Ssd, InventoryParsers.MediaType(4));
        Assert.Equal(StorageMediaType.Unknown, InventoryParsers.MediaType(null));
        Assert.Equal("NVMe", InventoryParsers.BusType(17));
        Assert.Equal("Unknown", InventoryParsers.BusType(99));
        Assert.Equal("DDR5", InventoryParsers.MemoryType(34));
        Assert.Null(InventoryParsers.MemoryType(0));
        Assert.Equal("x64", InventoryParsers.Architecture(9));
        Assert.Null(InventoryParsers.Health(7));
    }

    [Theory]
    [InlineData("  ASUS   ROG  STRIX ", "ASUS ROG STRIX")]
    [InlineData("To Be Filled By O.E.M.", "")]
    [InlineData(null, "")]
    public void CleansNames(string? raw, string expected) => Assert.Equal(expected, InventoryParsers.CleanName(raw));
}

public class StartupParsingTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\tool.exe /background", "C:\\Tools\\tool.exe")]
    [InlineData("C:\\Program Files\\Vendor\\updater.exe -silent", "C:\\Program Files\\Vendor\\updater.exe")]
    [InlineData("", null)]
    public void ExtractsExecutables(string commandLine, string? expected) =>
        Assert.Equal(expected, CommandLineParser.ExtractExecutable(commandLine));

    [Fact]
    public void InterpretsStartupApprovedValues()
    {
        Assert.True(StartupManager.IsApproved(null));
        Assert.True(StartupManager.IsApproved(new RegistryValue(RegistryKind.Binary, new byte[] { 2, 0, 0, 0 })));
        Assert.False(StartupManager.IsApproved(new RegistryValue(RegistryKind.Binary, new byte[] { 3, 0, 0, 0 })));
        Assert.True(StartupManager.IsApproved(new RegistryValue(RegistryKind.DWord, 3)));
    }

    [Fact]
    public void ParsesShellLinkTargetAndArguments()
    {
        var link = BuildLink("C:\\Games\\launcher.exe", "--silent");

        var info = ShellLinkReader.Parse(link);

        Assert.NotNull(info);
        Assert.Equal("C:\\Games\\launcher.exe", info.TargetPath);
        Assert.Equal("--silent", info.Arguments);
    }

    [Fact]
    public void RejectsInvalidShellLinks()
    {
        Assert.Null(ShellLinkReader.Parse(new byte[10]));
        var truncated = BuildLink("C:\\a.exe", "x")[..0x50];
        Assert.Null(ShellLinkReader.Parse(truncated));
    }

    private static byte[] BuildLink(string target, string arguments)
    {
        var data = new List<byte>();
        var header = new byte[0x4C];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x4C);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0x14), 0x2 | 0x20 | 0x80);
        data.AddRange(header);

        var basePath = Encoding.Latin1.GetBytes(target + "\0");
        const int linkInfoHeader = 0x1C;
        var linkInfo = new byte[linkInfoHeader + basePath.Length + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(linkInfo, (uint)linkInfo.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(linkInfo.AsSpan(4), linkInfoHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(linkInfo.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(linkInfo.AsSpan(0x10), linkInfoHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(linkInfo.AsSpan(0x18), (uint)(linkInfoHeader + basePath.Length));
        basePath.CopyTo(linkInfo, linkInfoHeader);
        data.AddRange(linkInfo);

        var args = Encoding.Unicode.GetBytes(arguments);
        data.Add((byte)arguments.Length);
        data.Add(0);
        data.AddRange(args);
        return [.. data];
    }
}
