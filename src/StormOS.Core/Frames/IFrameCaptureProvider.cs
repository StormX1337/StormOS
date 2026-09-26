using System.Threading.Channels;

namespace StormOS.Core.Frames;

/// <summary>Availability of a frame capture provider.</summary>
/// <param name="IsAvailable">Whether the provider can capture on this system.</param>
/// <param name="Reason">Why the provider is unavailable, or a note about limitations.</param>
public sealed record FrameCaptureAvailability(bool IsAvailable, string? Reason = null);

/// <summary>Captures per-frame presentation timing for a process.</summary>
public interface IFrameCaptureProvider
{
    /// <summary>Gets the provider display name.</summary>
    string Name { get; }

    /// <summary>Gets the provider priority; higher values are preferred.</summary>
    int Priority { get; }

    /// <summary>Checks whether the provider can be used.</summary>
    /// <returns>The availability.</returns>
    FrameCaptureAvailability CheckAvailability();

    /// <summary>Starts capturing frames for a process.</summary>
    /// <param name="processId">The process to capture.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An active capture session.</returns>
    Task<IFrameCaptureSession> StartAsync(int processId, CancellationToken cancellationToken = default);
}

/// <summary>An active frame capture.</summary>
public interface IFrameCaptureSession : IAsyncDisposable
{
    /// <summary>Gets the captured process id.</summary>
    int ProcessId { get; }

    /// <summary>Gets the provider name.</summary>
    string Source { get; }

    /// <summary>Gets the stream of captured frames.</summary>
    ChannelReader<FrameSample> Frames { get; }

    /// <summary>Gets a task that completes when the capture stops, with the reason when it stopped unexpectedly.</summary>
    Task<string?> Completion { get; }
}
