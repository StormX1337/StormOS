using System.Globalization;
using StormOS.Core.Analysis;
using StormOS.Core.Optimization;

namespace StormOS.Services.Analysis;

/// <summary>
/// Deterministic, explainable analysis: every recommendation is derived from measured values with documented
/// thresholds and carries its evidence. It never changes the system; actions require user confirmation.
/// </summary>
public sealed class RuleBasedAnalysisEngine : IAnalysisEngine
{
    /// <inheritdoc />
    public string Name => "STORM rules";

    /// <inheritdoc />
    public Task<IReadOnlyList<Recommendation>> AnalyzeAsync(AnalysisWindow window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        var list = new List<Recommendation>();
        var gaming = window.GameName is not null;
        static string F(double value, string format = "0") => value.ToString(format, CultureInfo.InvariantCulture);

        if (window.SampleCount < 5)
        {
            return Task.FromResult<IReadOnlyList<Recommendation>>(list);
        }

        if (gaming && window.AverageGpuUsage is { } gpu && window.AverageMaxCoreUsage is { } core && gpu < 75 && core > 90)
        {
            list.Add(new Recommendation
            {
                Id = "cpu-limited",
                Title = "The game is CPU-limited",
                Why = "Your GPU utilization is consistently below CPU utilization while the game is running. This may indicate a CPU-limited workload.",
                Evidence = [$"Average GPU usage: {F(gpu)} %", $"Busiest CPU thread: {F(core)} % on average", $"Game: {window.GameName}"],
                Risk = RiskLevel.None,
                Advice = "Raise the resolution or graphics quality (it will cost little), lower CPU-heavy settings (view distance, crowd/physics detail), and close busy background programs.",
                Confidence = gpu < 60 ? Confidence.High : Confidence.Medium,
            });
        }

        if (gaming && window.AverageGpuUsage is > 97)
        {
            list.Add(new Recommendation
            {
                Id = "gpu-limited",
                Title = "The game is GPU-limited",
                Why = "The GPU is fully busy, so it sets the frame rate.",
                Evidence = [$"Average GPU usage: {F(window.AverageGpuUsage.Value)} %"],
                Risk = RiskLevel.None,
                Advice = "Lower the resolution scale or GPU-heavy settings, or enable an upscaler (DLSS/FSR/XeSS). Enable NVIDIA Reflex / AMD Anti-Lag to keep latency low while GPU-bound.",
                Confidence = Confidence.High,
            });
        }

        if (window.AverageVramUsage is > 92)
        {
            list.Add(new Recommendation
            {
                Id = "vram-full",
                Title = "Video memory is nearly full",
                Why = "When VRAM runs out, textures are streamed over PCIe, which causes stutter.",
                Evidence = [$"Average VRAM usage: {F(window.AverageVramUsage.Value)} %"],
                Risk = RiskLevel.None,
                Advice = "Lower texture quality or the texture streaming budget by one step.",
                Confidence = Confidence.High,
            });
        }

        if (window.AverageRamUsage is > 88)
        {
            list.Add(new Recommendation
            {
                Id = "ram-pressure",
                Title = "System memory is under pressure",
                Why = "Above ~90 % memory use Windows starts paging, which causes hitches.",
                Evidence = [$"Average RAM usage: {F(window.AverageRamUsage.Value)} %", $"Running processes: {window.ProcessCount}"],
                Risk = RiskLevel.Low,
                Advice = "Close browsers and launchers you do not need while playing, and review startup programs.",
                Confidence = Confidence.High,
            });
        }

        if (window.MaxCpuTemperature is > 90)
        {
            list.Add(new Recommendation
            {
                Id = "cpu-hot",
                Title = "CPU temperature is very high",
                Why = "Near its limit the CPU lowers its clock speed (thermal throttling).",
                Evidence = [$"Peak thermal zone temperature: {F(window.MaxCpuTemperature.Value)} °C"],
                Risk = RiskLevel.None,
                Advice = "Check cooler mounting and case airflow, and clean dust filters.",
                Confidence = Confidence.Medium,
            });
        }

        if (window.MaxGpuTemperature is > 85)
        {
            list.Add(new Recommendation
            {
                Id = "gpu-hot",
                Title = "GPU temperature is very high",
                Why = "Most GPUs reduce boost clocks above roughly 83–87 °C.",
                Evidence = [$"Peak GPU temperature: {F(window.MaxGpuTemperature.Value)} °C"],
                Risk = RiskLevel.None,
                Advice = "Improve case airflow, clean the GPU fans, or cap the frame rate slightly below your refresh rate.",
                Confidence = Confidence.High,
            });
        }

        if (window.Frames is { FrameCount: > 500 } frames && frames.AverageFps > 0)
        {
            var ratio = frames.OnePercentLowFps / frames.AverageFps;
            if (ratio < 0.5)
            {
                list.Add(new Recommendation
                {
                    Id = "stutter",
                    Title = "Frame pacing is uneven",
                    Why = "The slowest 1 % of frames are much slower than average, which is felt as stutter.",
                    Evidence = [$"Average: {F(frames.AverageFps)} FPS", $"1% low: {F(frames.OnePercentLowFps)} FPS ({F(ratio * 100)} % of average)", $"Stutter frames: {frames.StutterCount}"],
                    Risk = RiskLevel.None,
                    Advice = "A frame rate cap a little below the average (for example with the game's own limiter) often smooths frame times. Also check VRAM and disk activity.",
                    Confidence = Confidence.High,
                });
            }

            if (window.RefreshRateHz is { } hz && frames.AverageFps < hz * 0.6)
            {
                list.Add(new Recommendation
                {
                    Id = "below-refresh",
                    Title = "Frame rate is well below your display's refresh rate",
                    Why = "Your monitor can show more frames than the game currently produces.",
                    Evidence = [$"Average: {F(frames.AverageFps)} FPS", $"Refresh rate: {F(hz)} Hz"],
                    Risk = RiskLevel.None,
                    Advice = "See whether the game is CPU- or GPU-limited (above) and adjust the matching settings.",
                    Confidence = Confidence.Medium,
                });
            }
        }

        if (gaming && window.AverageDiskActive is > 80)
        {
            list.Add(new Recommendation
            {
                Id = "disk-busy",
                Title = "The disk is saturated during gameplay",
                Why = "When the disk is constantly busy, asset streaming stalls and causes hitches.",
                Evidence = [$"Average active time of the busiest disk: {F(window.AverageDiskActive.Value)} %"],
                Risk = RiskLevel.None,
                Advice = "Pause downloads and updates while playing, and install the game on an SSD if it is on a hard disk.",
                Confidence = Confidence.Medium,
            });
        }

        if (window.AverageLatencyMs is > 80)
        {
            list.Add(new Recommendation
            {
                Id = "latency-high",
                Title = "Internet latency is high",
                Why = "Latency above ~80 ms is noticeable in fast online games.",
                Evidence = [$"Average latency: {F(window.AverageLatencyMs.Value)} ms"],
                Risk = RiskLevel.None,
                Advice = "Use a wired connection, stop large downloads, and run the Network test to check jitter and packet loss.",
                Confidence = Confidence.Medium,
            });
        }

        if (gaming && window.PowerPlan is { } plan && plan.Contains("saver", StringComparison.OrdinalIgnoreCase))
        {
            list.Add(new Recommendation
            {
                Id = "power-saver",
                Title = "Power saver plan is active while gaming",
                Why = "Power saver limits CPU clocks.",
                Evidence = [$"Active plan: {plan}"],
                Risk = RiskLevel.Low,
                Advice = "Switch to Balanced or High performance while playing.",
                Action = new RecommendedAction("Switch to Balanced", "power.plan", new Dictionary<string, string> { ["plan"] = "balanced" }),
                Confidence = Confidence.High,
            });
        }

        if (gaming && window.BusyBackgroundProcesses is > 3)
        {
            list.Add(new Recommendation
            {
                Id = "background-busy",
                Title = "Several background programs are using the CPU",
                Why = "They compete with the game for CPU time.",
                Evidence = [$"Busy background processes: {window.BusyBackgroundProcesses}"],
                Risk = RiskLevel.Low,
                Advice = "Close them or lower their priority from the Processes page.",
                Confidence = Confidence.Medium,
            });
        }

        return Task.FromResult<IReadOnlyList<Recommendation>>(list);
    }
}
