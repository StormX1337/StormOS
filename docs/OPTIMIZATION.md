# Optimization engine

Every optimization is a rule (`IOptimizationRule`) with metadata and five operations. The engine
(`StormOS.Optimization/Engine/OptimizationEngine.cs`) enforces the same pipeline for all of them:

```
validate parameters → detect → snapshot → apply → verify → record
                                   └── on apply/verify failure: rollback from snapshot → verify → record
```

## Rule contract

| Member | Meaning |
|---|---|
| `Id`, `Name`, `Description` | stable id (`area.name`), user-facing texts |
| `Category` | Power, GamingServices, Startup, BackgroundProcesses, Network, WindowsSettings, Game, Storage, Process, Services |
| `RiskLevel` | None / Low / Medium / High — Medium+ are hidden unless *Settings › Optimization › Show advanced* is on |
| `RequiresAdmin` | executed by the service instead of the app |
| `CanRollback` | whether a snapshot can restore the previous state (shown before confirmation) |
| `RequiresRestart` | shown before confirmation and after applying |
| `SupportedOs` | minimum/maximum Windows build |
| `Parameters` | declared, validated parameters (allowed values where applicable) |
| `DetectAsync` | current vs. target value and whether the rule applies |
| `CaptureAsync` | snapshot of everything `ApplyAsync` will touch |
| `ApplyAsync`, `VerifyAsync`, `RollbackAsync` | change, confirm, restore |

Records (`OptimizationRecord`) store before/after values, the snapshot, the user, the executor, outcome and rollback
status in SQLite (`optimization_records`). *Restore* replays a snapshot and verifies the result; *Restore all*
restores every active change in reverse order.

## Built-in rules

| Id | What it does | Risk | Admin | Reversible |
|---|---|---|---|---|
| `windows.game-mode` | turns on Windows Game Mode (HKCU) | Low | – | ✓ |
| `windows.game-dvr` | turns off background recording / Game DVR (HKCU) | Low | – | ✓ |
| `windows.windowed-optimizations` | enables *Optimizations for windowed games* (Windows 11 22H2+) | Low | – | ✓ |
| `input.mouse-precision` | turns off *Enhance pointer precision* | Low | – | ✓ |
| `power.plan` | activates Balanced, High performance or a plan by GUID | Low | – | ✓ |
| `power.ultimate-plan` | creates the Ultimate Performance plan (duplicate of the hidden scheme) | Low | ✓ | ✓ |
| `windows.hags` | enables hardware-accelerated GPU scheduling (restart) | Medium | ✓ | ✓ |
| `storage.trim` | ensures TRIM is enabled for SSDs | Low | ✓ | ✓ |
| `startup.entry` / `startup.entry-machine` | enables/disables a startup entry via *StartupApproved* (entries are never deleted) | Low | –/✓ | ✓ |
| `service.start-type` | changes the start type of a service from the curated knowledge base | Medium | ✓ | ✓ |
| `network.dns` | sets 1–2 public IPv4 DNS servers on one adapter | Medium | ✓ | ✓ (previous servers or DHCP) |
| `background.lower-priority` | lowers a busy background program to *below normal* | Low | – | ✓ (until it exits) |
| `process.game-priority` | raises a game's priority while it runs (profiles) | Low | – | ✓ |
| `storage.temp-cleanup` | deletes old files from temp folders | Low | – | ✗ (stated) |

Not included on purpose: disabling Defender/firewall/updates/UAC/SmartScreen, “debloat” scripts, network
stack tweaks without measurable benefit (Nagle, TCP autotuning), timer resolution hacks, registry cleaners, and
blanket service disabling.

## Where rules are triggered

- **Optimizer page**: catalog with detection per rule, *Quick optimize* (only Low-risk, reversible, no restart,
  shown as a plan before applying), *Restore all*.
- **System Scan** findings and **recommendations** may carry a rule id; applying always shows the confirmation dialog.
- **Game profiles** reference rules (`systemRecommendations`, `optimizationRules`); session-scoped rules are applied
  while the game runs and restored on exit when enabled in settings.
- **CLI**: `storm optimize list`, `storm optimize apply <rule> [--param k=v] --yes`, `storm restore <id>|--all --yes`.

## Writing a rule

Prefer `RegistryValueRule` for registry settings: declare `RegistryTarget`s and the base class implements
detect/capture/apply/verify/rollback (including deleting values that did not exist before). Add unit tests with the
in-memory registry (`tests/StormOS.Optimization.Tests/Fakes.cs`) covering apply, verify, rollback and failure
rollback, then register the rule in `OptimizationServiceCollectionExtensions`.
