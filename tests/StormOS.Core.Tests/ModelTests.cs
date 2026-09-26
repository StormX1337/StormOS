using StormOS.Core.Common;
using StormOS.Core.Games;
using StormOS.Core.Optimization;

namespace StormOS.Core.Tests;

public sealed class ModelTests
{
    [Fact]
    public void Reading_NonFinite_IsUnavailable()
    {
        Assert.False(Reading.Of(double.NaN).IsAvailable);
        Assert.True(Reading.Of(42).IsAvailable);
        Assert.Equal("why", Reading.Unavailable("why").UnavailableReason);
    }

    [Fact]
    public void GameId_IsNormalized()
    {
        Assert.Equal("steam:730", GameInfo.CreateId(LauncherKind.Steam, " 730 "));
    }

    [Fact]
    public void OsSupport_Range()
    {
        Assert.True(OsSupport.Windows11OrLater.Supports(26100));
        Assert.False(OsSupport.Windows11OrLater.Supports(19045));
        Assert.False(new OsSupport(19041, 22000).Supports(22621));
    }

    [Fact]
    public void Result_Failure_CarriesError()
    {
        var result = Result<int>.Fail(StormErrorCodes.NotFound, "missing");

        Assert.False(result.IsSuccess);
        Assert.Equal(StormErrorCodes.NotFound, result.Error!.Code);
        Assert.Throws<InvalidOperationException>(() => result.GetValueOrThrow());
    }

    [Fact]
    public void Units_Format()
    {
        Assert.Equal("1.0 KB", Units.FormatBytes(1024));
        Assert.Equal("15.8 GB", Units.FormatBytes(15.8 * 1024 * 1024 * 1024));
        Assert.Equal("940 Mbit/s", Units.FormatBitRate(940_000_000));
        Assert.Equal("+4.8%", Units.FormatDeltaPercent(4.8));
        Assert.Equal("-2.0%", Units.FormatDeltaPercent(-2));
    }
}
