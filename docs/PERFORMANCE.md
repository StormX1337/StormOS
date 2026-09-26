# Telemetry and performance monitoring

STORM OS shows only measured values. Every metric is a `Reading` (`value`, `unavailableReason`, `source`);
when a sensor is missing the UI says *Unavailable* and shows the reason in a tooltip. Nothing is interpolated,
estimated or replaced by a plausible default.

## Sources

| Metric | Source | Notes |
|---|---|---|
| CPU load (total, per core) | PDH `\Processor Information(*)\% Processor Utility` (fallback `% Processor Time`) | Utility matches Task Manager on modern Windows |
| CPU clock | PDH `Processor Frequency` × `% Processor Performance` | effective clock including boost |
| CPU temperature | PDH `\Thermal Zone Information(*)\High Precision Temperature` | ACPI zone; many desktops do not expose a CPU-accurate zone → *Unavailable*. No kernel driver is used (no WinRing0/MSR access). |
| CPU package power | PDH `\Energy Meter(*)\Power` | only where the platform publishes RAPL through Energy Meter |
| GPU load (total / 3D) | PDH `\GPU Engine(*)\Utilization Percentage` aggregated per adapter and engine | same method as Task Manager |
| GPU per-process load | same counters, grouped by PID | used by the process list and background analysis |
| VRAM usage | PDH `\GPU Adapter Memory(*)\Dedicated Usage`, total from DXGI | |
| GPU temperature, fan, clocks, power | NVML (NVIDIA, loaded dynamically) → D3DKMT `QueryAdapterInfo` performance data (WDDM 2.4+ drivers) | values the driver reports as unsupported are rejected |
| Memory | `GlobalMemoryStatusEx`, `GetPerformanceInfo` | used, available, committed, cached |
| Disk throughput, active time | PDH `\PhysicalDisk(*)\…` | active time = 100 − % Idle Time |
| Network throughput | `NetworkInterface` statistics deltas | per adapter |
| Latency | ICMP probe to the configured target (default 1.1.1.1, every 5 s) | *Unavailable* when ICMP is blocked |
| System | process/thread/handle counts, `\System\Context Switches/sec`, uptime | |
| Inventory | WMI (`Win32_Processor`, `Win32_VideoController`, `MSFT_PhysicalDisk`, `Win32_PhysicalMemory`, …), DXGI, EDID | queried on demand, cached |

## Sampling and load

`TelemetryHub` samples only while something subscribes (dashboard, overlay, session recorder). Default interval 1 s;
while a game runs the hub switches to *performance mode* (2 s, configurable) so STORM OS itself stays out of the way.
Collectors reuse one PDH query per subsystem; WMI is never polled in the sampling loop. The app shows the
service's stream when the service runs and falls back to an in-process hub otherwise.

## Frame capture

`FrameCaptureCoordinator` (service) selects the best available provider:

1. **PresentMon 2.x** (`PresentMon.exe`, CSV to stdout). Only executed from Program Files or the STORM OS folder,
   only if Authenticode-signed or matching a pinned SHA-256; arguments are built from the numeric process id only.
2. **ETW** fallback (`Microsoft-Windows-DXGI` Present_Start, `Microsoft-Windows-D3D9` Present), requires the
   service (administrator) and can be disabled in settings. `SwapChainSelector` follows the dominant swap chain so
   launcher/overlay windows do not distort results.

Statistics (`FrameStatisticsCalculator`):

- **Average FPS** = frames / captured seconds (not the mean of instantaneous FPS).
- **1 % / 0.1 % low** = FPS equivalent of the 99th / 99.9th percentile frame time.
- **Stutter** = frames longer than 2× the rolling median of the previous 20 frames.
- Frame times ≤ 0 or > 5 s are discarded as capture artefacts.

Whole sessions use a constant-memory frame-time histogram (0.1 ms bins up to 250 ms), so a three-hour session costs
the same memory as a one-minute one.

## History

The service aggregates snapshots into 10-second points (`MetricHistoryRecorder`) and stores them in SQLite for the
*Historical* chart range (retention configurable, default 30 days). Game sessions store averages, lows, peak
temperatures and the frame source.

## Charts

The Performance page offers 10 seconds, 1 minute, 5 minutes, the current session and historical ranges.
Missing samples break the line instead of being drawn as zero.
