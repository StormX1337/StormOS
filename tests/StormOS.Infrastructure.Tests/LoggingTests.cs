using Microsoft.Extensions.Configuration;
using StormOS.Infrastructure.Logging;

namespace StormOS.Infrastructure.Tests;

public sealed class LoggingTests
{
    [Theory]
    [InlineData("StormOS.Optimization.OptimizationEngine", LogCategories.Optimization)]
    [InlineData("StormOS.Hardware.Gpu.D3dkmtCollector", LogCategories.Hardware)]
    [InlineData("StormOS.Performance.TelemetryHub", LogCategories.Hardware)]
    [InlineData("StormOS.Network.NetworkDiagnostics", LogCategories.Network)]
    [InlineData("StormOS.Security.IpcAudit", LogCategories.Security)]
    [InlineData("StormOS.App.Something", LogCategories.Application)]
    [InlineData(null, LogCategories.Application)]
    public void Categories_Resolve(string? source, string expected) =>
        Assert.Equal(expected, LogCategories.Resolve(source, LogCategories.Application));

    [Fact]
    public void Logger_WritesStructuredJson_WithoutSecrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "storm-logs-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var logger = StormLogging.Create(new ConfigurationBuilder().Build(), directory, LogCategories.Application, developerLogging: false))
            {
                logger.ForContext("SourceContext", "StormOS.Optimization.Engine").Information("Applied {Rule} with {Password} and {Detail}", "windows.game-mode", "hunter2", "token=abc123");
            }

            var (file, lines) = StormLogging.Tail(directory, LogCategories.Application, 10);
            Assert.StartsWith("application-", file, StringComparison.Ordinal);
            var line = Assert.Single(lines);
            Assert.Contains("windows.game-mode", line, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", line, StringComparison.Ordinal);
            Assert.DoesNotContain("abc123", line, StringComparison.Ordinal);
            Assert.Contains("\"LogCategory\":\"Optimization\"", line, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory, "application-optimization-*.log"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
