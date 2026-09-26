# Benchmarks and scores

All benchmarks run locally, record the hardware they ran on and the average CPU/GPU load during the run, and are
stored in the local history. A run that cannot execute (missing GPU, unsupported disk, no network) is stored as
*not completed* with the reason — it never produces numbers.

## Workloads

| Type | Metrics | Method |
|---|---|---|
| CPU | `cpu.single.mops`, `cpu.multi.mops`, `cpu.scaling`, `cpu.sha256.mbps` | integer/FP mixed kernel (xorshift, FMA, popcount) on 1 and N threads for the chosen duration; SHA-256 throughput |
| Memory | `memory.copy.gbps`, `memory.latency.ns` | large-buffer copy bandwidth; random pointer chase over a single Sattolo cycle (defeats prefetching) |
| Disk | `disk.seqwrite.mbps`, `disk.seqread.mbps`, `disk.rand4k.iops`, `disk.rand4k.mbps` | unbuffered write-through file (512 MB default) in `%LOCALAPPDATA%\StormOS\benchmark`, deleted afterwards; 4K random reads at QD1 |
| GPU | `gpu.compute.gflops`, `gpu.compute.peak` | Direct3D 11 compute shader (FMA chains) timed with GPU timestamp queries; median of dispatches |
| Network | `net.latency.ms`, `net.jitter.ms`, `net.loss.percent`, `net.download.mbps` | ICMP to the latency target; optional HTTPS download |
| Gaming | average FPS, 1 % / 0.1 % low, frame time | frame capture of a running game via the service (warm-up excluded) |

Use *labels* (“before”, “after”) to organise runs. The comparison view only lists metrics measured in **both**
runs; a difference is never computed against a missing value. For gaming comparisons use the same scene,
resolution and settings.

## Scores

Every score is a weighted mean of normalized inputs (0–100). Inputs that were not measured are excluded and listed
as *Not measured*; with less than 50 % of the weight measured the score is *Insufficient data*. The UI shows every
input, its measured value, normalized value, weight and the formula.

### Component scores (per benchmark)

| Score | Inputs (weight) | 100 points at |
|---|---|---|
| CPU | single-thread (0.45), multi-thread (0.55) | 1 200 / 16 000 MOPS |
| Memory | copy bandwidth (0.65), latency (0.35) | 60 GB/s; 60 ns (scaled inversely) |
| Storage | sequential read (0.5), 4K random read (0.5) | 6 000 MB/s; 80 000 IOPS |
| GPU | FP32 compute (1.0) | 40 000 GFLOPS |

### STORM Performance Score

Latest CPU, memory, disk and GPU results: CPU single (0.20), CPU multi (0.25), GPU compute (0.25), memory bandwidth
(0.10), memory latency (0.05), disk sequential (0.08), disk random (0.07).

### STORM Gaming Score

From frame statistics: average FPS relative to the display refresh rate (0.40, capped at 100), 1 % low / average
ratio (0.35; 0.75 = 100, 0.3 = 0), stutter frames per 1 000 (0.25; 0 = 100, 20 = 0).

### STORM Network Score

Latency (0.35; 10 ms = 100, 150 ms = 0), jitter (0.20; 1 ms = 100, 30 ms = 0), packet loss (0.30; 0 % = 100,
5 % = 0), DNS response of the configured servers (0.15; 10 ms = 100, 200 ms = 0).

### System Health Score

Memory usage (0.20; ≤ 50 % = 100, ≥ 95 % = 0), system drive free space (0.20; ≥ 25 % = 100, ≤ 5 % = 0), enabled
startup entries (0.15; ≤ 5 = 100, ≥ 25 = 0), running processes (0.15; ≤ 120 = 100, ≥ 320 = 0), CPU temperature
(0.15; ≤ 60 °C = 100, ≥ 95 °C = 0), GPU temperature (0.15; ≤ 65 °C = 100, ≥ 90 °C = 0).

Reference values are constants in `StormOS.Core/Scoring/StormScores.cs`; changing them changes historic scores'
meaning, so they are versioned with the application.
