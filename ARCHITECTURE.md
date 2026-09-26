# STORM OS architecture

## Processes

```
StormOS.exe (WinUI 3, user, non-elevated) ─┐
storm.exe (CLI, user)                     ─┼─ named pipe \\.\pipe\StormOS.Service.v1 (length-prefixed JSON)
                                           │
StormOS.Service.exe (StormOSService, LocalSystem)
   ├─ telemetry hub (PDH, D3DKMT, NVML, GlobalMemoryStatusEx …)
   ├─ frame capture (PresentMon 2.x, ETW DXGI/D3D9 fallback)
   ├─ running-game detection + session recorder
   └─ optimization engine (admin rules) + rollback journal (SQLite, %ProgramData%\StormOS\service.db)

Optional: StormOS.exe ──HTTPS──> STORM Cloud API (NestJS) ──> PostgreSQL / Redis / Stripe
```

The desktop app executes **user-scope** rules itself (HKCU settings, user startup entries, power plan activation,
process priority of the user's own processes). Anything that needs administrative rights (HKLM, services, DNS, machine
startup entries, Ultimate Performance plan, frame capture via ETW) is executed by the service after the request has been
authenticated, authorized and validated.

## Layers

```
StormOS.App / StormOS.Cli            presentation (WinUI 3 MVVM / System.CommandLine)
        │
StormOS.Services                     application services: service client, optimization coordinator,
        │                            system scan, analysis, history, cloud, licensing, updates
        │
StormOS.Core                         domain models, contracts, IPC messages, scoring, frame statistics
        │
StormOS.Infrastructure               logging (Serilog), configuration, SQLite, IPC transport
StormOS.Security                     validation, authorization policy, rate limiting, integrity, license tokens
        │
StormOS.Windows / Hardware / Performance / Network / Games / Optimization / Benchmark / Overlay
                                     Windows implementations behind Core interfaces
        │
Win32 / WinRT / PDH / D3DKMT / NVML / WMI / ETW / PowrProf / SCM
```

- `StormOS.Core`, `Security`, `Infrastructure`, `Services` and `Network` target `net10.0` and are unit tested on any OS.
- Windows implementations target `net10.0-windows` and are accessed through interfaces
  (`IRegistryAccess`, `IPowerPlanService`, `IStartupManager`, `IProcessInspector`, collectors …), so rules and
  services are testable with in-memory fakes.
- The WinUI app targets `net10.0-windows10.0.22621.0` (min 10.0.19041).

## Key flows

**Telemetry.** `TelemetryHub` samples only while at least one subscriber exists. When a game is running it switches
to *performance mode* (2 s interval, per-core and disk sampling reduced). The service pushes `telemetry.snapshot`
events to subscribed clients. The app falls back to an in-process hub (no frame capture) when the service is not running.

**Frame capture.** `FrameCaptureCoordinator` picks the best available `IFrameCaptureProvider` (PresentMon > ETW),
buffers frames in a ring buffer for live statistics and benchmarks, and keeps a constant-memory frame-time histogram
for whole-session 1 % / 0.1 % lows.

**Optimization.** `OptimizationEngine` enforces: validate parameters → detect → snapshot → apply → verify → record.
Failed apply/verify triggers an automatic rollback. Records (with snapshots) are persisted in SQLite; *Restore* and
*Restore all* replay snapshots and verify the restored state.

**Game detection.** Launcher scanners (Steam, Epic, Xbox/Game Pass, Battle.net, Riot, Ubisoft, EA, GOG) feed the
`GameRegistry`; `RunningGameDetector` matches processes against profiles and install directories every 5 s.

See the `docs/` folder for per-subsystem details.

## Cloud

`cloud/` is a pnpm monorepo: `apps/api` (NestJS REST + socket.io, Prisma/PostgreSQL, optional Redis, Stripe),
`apps/web` (portal) and `apps/admin` (Storm Admin), sharing `packages/types`, `validation` (Zod), `database`
(Prisma schema, migrations, generated client) and `ui`. Entitlements are decided server-side and delivered to the
desktop app as device-bound ES256 tokens that are verified offline. See [docs/CLOUD.md](docs/CLOUD.md).

## Packaging

`scripts/publish.ps1` publishes app, service and CLI self-contained into one folder (the service trusts clients by
install location) and builds the WiX MSI in `installer/`, which registers `StormOSService`, a Start menu shortcut and
the CLI on `PATH`. See [docs/BUILD.md](docs/BUILD.md) and [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).
