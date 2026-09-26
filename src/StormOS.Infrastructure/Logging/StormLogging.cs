using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Settings.Configuration;

namespace StormOS.Infrastructure.Logging;

/// <summary>Creates the structured Serilog pipeline used by every STORM OS process.</summary>
public static class StormLogging
{
    /// <summary>Default number of daily log files kept.</summary>
    public const int RetainedFiles = 14;

    /// <summary>Maximum size of a single log file.</summary>
    public const long FileSizeLimitBytes = 20L * 1024 * 1024;

    /// <summary>Creates a logger writing compact JSON files: one main log plus one file per dedicated category.</summary>
    /// <param name="configuration">Configuration (reads the optional "Serilog" section for overrides).</param>
    /// <param name="logDirectory">Target directory.</param>
    /// <param name="mainCategory">Main category, <see cref="LogCategories.Application"/> or <see cref="LogCategories.Service"/>.</param>
    /// <param name="developerLogging">Whether verbose developer logging is enabled.</param>
    /// <returns>The logger.</returns>
    public static Serilog.Core.Logger Create(IConfiguration configuration, string logDirectory, string mainCategory, bool developerLogging)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(logDirectory);
        var fileStem = mainCategory.ToLowerInvariant();

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(developerLogging ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.With(new CategoryEnricher(mainCategory))
            .Enrich.With<RedactionEnricher>()
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            // Explicit sink assemblies: app, service and CLI are installed into one folder, and assembly discovery could
            // otherwise try to load Serilog packages that only one of the other executables references.
            .ReadFrom.Configuration(configuration, new ConfigurationReaderOptions(typeof(FileLoggerConfigurationExtensions).Assembly, typeof(CompactJsonFormatter).Assembly))
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(logDirectory, fileStem + "-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFiles,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                shared: true);

        foreach (var category in LogCategories.Dedicated)
        {
            var captured = category;
            config.WriteTo.Logger(sub => sub
                .Filter.ByIncludingOnly(e => e.Properties.TryGetValue("LogCategory", out var value) && value is Serilog.Events.ScalarValue { Value: string c } && c == captured)
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    Path.Combine(logDirectory, $"{fileStem}-{captured.ToLowerInvariant()}-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: RetainedFiles,
                    fileSizeLimitBytes: FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    shared: true));
        }

        return config.CreateLogger();
    }

    /// <summary>Reads the last lines of the newest main log file.</summary>
    /// <param name="logDirectory">Log directory.</param>
    /// <param name="mainCategory">Main category.</param>
    /// <param name="lines">Number of lines.</param>
    /// <returns>The file name and lines.</returns>
    public static (string File, IReadOnlyList<string> Lines) Tail(string logDirectory, string mainCategory, int lines)
    {
        lines = Math.Clamp(lines, 1, 1000);
        if (!Directory.Exists(logDirectory))
        {
            return (string.Empty, []);
        }

        var stem = mainCategory.ToLowerInvariant() + "-";
        var newest = new DirectoryInfo(logDirectory)
            .EnumerateFiles(stem + "*.log")
            .Where(f => !LogCategories.Dedicated.Any(c => f.Name.StartsWith(stem + c.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        if (newest is null)
        {
            return (string.Empty, []);
        }

        using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var queue = new Queue<string>(lines);
        while (reader.ReadLine() is { } line)
        {
            if (queue.Count == lines)
            {
                queue.Dequeue();
            }

            queue.Enqueue(line);
        }

        return (newest.Name, queue.ToArray());
    }
}
