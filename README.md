# STORM OS

**Your PC. Your performance. Your control.**

STORM OS is a Windows 11 gaming and performance command center: live hardware telemetry, real frame-time
analysis, automatic game detection with data-driven profiles, reversible system optimizations with snapshots and
rollback, benchmarks, network diagnostics, an in-game overlay and an optional cloud backend.

STORM OS measures, it never invents: every value on screen comes from Windows, the GPU driver or a documented
measurement. When a metric is not available on a system, STORM OS shows *Unavailable* and explains why.

## Components

| Component | Path | Purpose |
|---|---|---|
| Desktop app | `src/StormOS.App` | WinUI 3 / Windows App SDK, MVVM, runs **without** admin rights |
| Windows service | `src/StormOS.Service` | `StormOSService` (LocalSystem): telemetry, frame capture, privileged optimizations, rollback |
| CLI | `src/StormOS.Cli` | `storm status · hardware · games · benchmark · optimize · restore · network · scan · logs · service` |
| Core libraries | `src/StormOS.*` | Core, Security, Infrastructure, Services, Windows, Hardware, Performance, Network, Games, Optimization, Benchmark, Overlay |
| Game profiles | `profiles/*.json` | Versioned, validated JSON profiles (no code) |
| Cloud | `cloud/` | NestJS API, Next.js web + admin, Prisma/PostgreSQL, Redis, Stripe (optional) |
| Installer | `installer/` | WiX MSI: app, service, CLI, shortcuts, clean uninstall |

## Quick start

```powershell
# Windows 11 x64, .NET 10 SDK, Visual Studio 2022 17.14+ (Windows App SDK workload) or the .NET CLI
dotnet build StormOS.slnx -c Debug
dotnet test  StormOS.slnx

# Cross-platform subset (CI on Linux): everything except the WinUI app
dotnet build StormOS.Portable.slnf
dotnet test  StormOS.Portable.slnf

# Cloud
cd cloud && pnpm install && pnpm build
```

See [DEVELOPMENT.md](DEVELOPMENT.md) for the service/app development loop and [BUILD.md](docs/BUILD.md) for release builds.

## Documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) — layers, processes, data flow
- [SECURITY.md](SECURITY.md) — threat model, IPC hardening, what STORM OS will never do
- [DEVELOPMENT.md](DEVELOPMENT.md), [DEPLOYMENT.md](DEPLOYMENT.md), [CONTRIBUTING.md](CONTRIBUTING.md)
- `docs/`: IPC, PERFORMANCE, OPTIMIZATION, PROFILES, BENCHMARK, OVERLAY, CLOUD, API, BUILD, TROUBLESHOOTING

## Principles

- **Real data only.** No fake FPS, scores or "optimized" messages.
- **Least privilege.** The UI never runs elevated; the service exposes a small, validated, allow-listed API.
- **Reversible.** Every change is detected, snapshotted, applied, verified, logged and can be restored.
- **Conservative.** No security feature is ever disabled; nothing touches anti-cheat, drivers or game memory.
- **Private by default.** Telemetry, crash reports and cloud sync are off until you turn them on.
