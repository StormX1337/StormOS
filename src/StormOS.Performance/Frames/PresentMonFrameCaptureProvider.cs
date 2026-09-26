using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StormOS.Core.Frames;
using StormOS.Security.Integrity;
using StormOS.Windows.Security;

namespace StormOS.Performance.Frames;

/// <summary>
/// Frame capture through Intel's PresentMon (2.x console). PresentMon is launched with a fixed argument list built
/// only from the numeric process id; the binary must live in an administrator-protected location and be
/// Authenticode signed or match a pinned SHA-256.
/// </summary>
public sealed class PresentMonFrameCaptureProvider : IFrameCaptureProvider
{
    private readonly FrameCaptureOptions _options;
    private readonly ILogger<PresentMonFrameCaptureProvider> _logger;
    private string? _verifiedPath;

    /// <summary>Initializes a new instance of the <see cref="PresentMonFrameCaptureProvider"/> class.</summary>
    /// <param name="options">Options.</param>
    /// <param name="logger">Logger.</param>
    public PresentMonFrameCaptureProvider(IOptions<FrameCaptureOptions> options, ILogger<PresentMonFrameCaptureProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "PresentMon";

    /// <inheritdoc />
    public int Priority => 100;

    /// <summary>Returns candidate PresentMon locations in priority order.</summary>
    /// <param name="configuredPath">Explicitly configured path.</param>
    /// <returns>Candidate paths.</returns>
    public static IEnumerable<string> CandidatePaths(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        foreach (var root in new[] { Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Intel", "PresentMon", "PresentMonCli") })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "PresentMon*.exe").OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    /// <summary>Builds the PresentMon argument list for a process.</summary>
    /// <param name="processId">Target process id.</param>
    /// <returns>Arguments.</returns>
    public static IReadOnlyList<string> BuildArguments(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        var pid = processId.ToString(CultureInfo.InvariantCulture);
        return ["--process_id", pid, "--output_stdout", "--no_console_stats", "--stop_existing_session", "--session_name", "StormOS_" + pid, "--terminate_on_proc_exit"];
    }

    /// <inheritdoc />
    public FrameCaptureAvailability CheckAvailability()
    {
        var path = Locate();
        return path is null
            ? new FrameCaptureAvailability(false, "PresentMon is not installed. Install it from the STORM OS installer or from Intel's PresentMon releases into Program Files.")
            : new FrameCaptureAvailability(true, path);
    }

    /// <inheritdoc />
    public Task<IFrameCaptureSession> StartAsync(int processId, CancellationToken cancellationToken = default)
    {
        var path = Locate() ?? throw new InvalidOperationException("PresentMon is not available.");
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path)!,
        };
        foreach (var argument in BuildArguments(processId))
        {
            info.ArgumentList.Add(argument);
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException("PresentMon could not be started.");
        _logger.LogInformation("PresentMon started for process {Pid}", processId);
        return Task.FromResult<IFrameCaptureSession>(new Session(process, processId, _logger));
    }

    private string? Locate()
    {
        if (_verifiedPath is not null && File.Exists(_verifiedPath))
        {
            return _verifiedPath;
        }

        foreach (var candidate in CandidatePaths(_options.PresentMonPath))
        {
            if (!File.Exists(candidate) || !IsProtectedLocation(candidate))
            {
                continue;
            }

            if (AuthenticodeVerifier.IsTrusted(candidate)
                || (_options.PresentMonSha256 is { Length: 64 } pin && FileIntegrity.VerifySha256Async(candidate, pin).GetAwaiter().GetResult()))
            {
                _verifiedPath = candidate;
                return candidate;
            }

            _logger.LogWarning("Ignoring PresentMon at {Path}: not signed and no matching SHA-256 pin", candidate);
        }

        return null;
    }

    private static bool IsProtectedLocation(string path)
    {
        var full = Path.GetFullPath(path);
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            AppContext.BaseDirectory,
        ];
        return roots.Where(r => !string.IsNullOrEmpty(r)).Any(r => full.StartsWith(Path.TrimEndingDirectorySeparator(r) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Session : IFrameCaptureSession
    {
        private readonly Process _process;
        private readonly Channel<FrameSample> _channel = Channel.CreateBounded<FrameSample>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true });
        private readonly Task<string?> _pump;
        private readonly ILogger _logger;

        public Session(Process process, int processId, ILogger logger)
        {
            _process = process;
            _logger = logger;
            ProcessId = processId;
            _pump = Task.Run(PumpAsync);
        }

        public int ProcessId { get; }

        public string Source => "PresentMon";

        public ChannelReader<FrameSample> Frames => _channel.Reader;

        public Task<string?> Completion => _pump;

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            await _pump.ConfigureAwait(false);
            _process.Dispose();
        }

        private async Task<string?> PumpAsync()
        {
            var parser = new PresentMonCsvParser();
            try
            {
                while (await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (!parser.HasHeader)
                    {
                        parser.TryParseHeader(line);
                        continue;
                    }

                    if (parser.TryParseRow(line, out var sample, out var pid) && pid == ProcessId)
                    {
                        _channel.Writer.TryWrite(sample);
                    }
                }

                await _process.WaitForExitAsync().ConfigureAwait(false);
                var error = (await _process.StandardError.ReadToEndAsync().ConfigureAwait(false)).Trim();
                return _process.ExitCode == 0 || !parser.HasHeader && error.Length == 0 ? null : $"PresentMon stopped (exit code {_process.ExitCode}). {error}".Trim();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "PresentMon output ended");
                return null;
            }
            finally
            {
                _channel.Writer.TryComplete();
            }
        }
    }
}
