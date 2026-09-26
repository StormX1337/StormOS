using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using StormOS.Core.Frames;

namespace StormOS.Performance.Frames;

/// <summary>
/// Fallback frame capture using ETW present events of the Microsoft-Windows-DXGI (Present_Start, event 42) and
/// Microsoft-Windows-D3D9 (Present_Start, event 1) providers. Measures the interval between presents of the
/// dominant swap chain (equivalent to PresentMon's MsBetweenPresents). Covers Direct3D 9–12 titles; Vulkan and
/// OpenGL titles require PresentMon. Requires administrative rights (runs inside the service).
/// </summary>
public sealed class EtwPresentFrameCaptureProvider : IFrameCaptureProvider
{
    /// <summary>Microsoft-Windows-DXGI provider id.</summary>
    public static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");

    /// <summary>Microsoft-Windows-D3D9 provider id.</summary>
    public static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");

    private readonly ILogger<EtwPresentFrameCaptureProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="EtwPresentFrameCaptureProvider"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public EtwPresentFrameCaptureProvider(ILogger<EtwPresentFrameCaptureProvider> logger) => _logger = logger;

    /// <inheritdoc />
    public string Name => "ETW (DXGI/D3D9)";

    /// <inheritdoc />
    public int Priority => 10;

    /// <inheritdoc />
    public FrameCaptureAvailability CheckAvailability() =>
        TraceEventSession.IsElevated() == true
            ? new FrameCaptureAvailability(true, "Direct3D 9–12 titles only; Vulkan and OpenGL require PresentMon.")
            : new FrameCaptureAvailability(false, "ETW frame capture requires the STORM OS service (administrator rights).");

    /// <inheritdoc />
    public Task<IFrameCaptureSession> StartAsync(int processId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IFrameCaptureSession>(new Session(processId, _logger));

    private sealed class Session : IFrameCaptureSession
    {
        private readonly TraceEventSession _session;
        private readonly Channel<FrameSample> _channel = Channel.CreateBounded<FrameSample>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true });
        private readonly SwapChainSelector _selector = new();
        private readonly Task<string?> _pump;

        public Session(int processId, ILogger logger)
        {
            ProcessId = processId;
            _session = new TraceEventSession($"StormOS-Frames-{processId}-{Environment.TickCount64}") { StopOnDispose = true };
            _session.EnableProvider(DxgiProvider, TraceEventLevel.Informational);
            _session.EnableProvider(D3D9Provider, TraceEventLevel.Informational);
            var parser = new RegisteredTraceEventParser(_session.Source);
            parser.All += OnEvent;
            _pump = Task.Run(() =>
            {
                try
                {
                    _session.Source.Process();
                    return (string?)null;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "ETW frame capture stopped");
                    return "ETW frame capture stopped unexpectedly.";
                }
                finally
                {
                    _channel.Writer.TryComplete();
                }
            });
        }

        public int ProcessId { get; }

        public string Source => "ETW (DXGI/D3D9)";

        public ChannelReader<FrameSample> Frames => _channel.Reader;

        public Task<string?> Completion => _pump;

        public async ValueTask DisposeAsync()
        {
            _session.Dispose();
            await _pump.ConfigureAwait(false);
        }

        private void OnEvent(TraceEvent data)
        {
            if (data.ProcessID != ProcessId)
            {
                return;
            }

            var isPresent = (data.ProviderGuid == DxgiProvider && (int)data.ID == 42) || (data.ProviderGuid == D3D9Provider && (int)data.ID == 1);
            if (!isPresent)
            {
                return;
            }

            var swapChain = data.PayloadByName("pIDXGISwapChain") ?? data.PayloadByName("pSwapchain");
            var chain = swapChain is null ? 0UL : Convert.ToUInt64(swapChain, System.Globalization.CultureInfo.InvariantCulture);
            var timestamp = data.TimeStampRelativeMSec;
            if (_selector.OnPresent(chain, timestamp) is { } frameTime)
            {
                _channel.Writer.TryWrite(new FrameSample(timestamp / 1000.0, frameTime));
            }
        }
    }
}
