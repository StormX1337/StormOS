# IPC protocol

The desktop app and the CLI talk to `StormOSService` over a local named pipe. The protocol is small, versioned,
typed and allow-listed. Implementation: `StormOS.Infrastructure/Ipc` (transport, dispatcher),
`StormOS.Security/Ipc` (identity, trust, policies, rate limits), `StormOS.Service/Handlers` (operations).

## Transport

| Property | Value |
|---|---|
| Pipe | `\\.\pipe\StormOS.Service.v1` (byte mode, asynchronous, `FirstPipeInstance`) |
| Framing | 4-byte little-endian length prefix + UTF-8 JSON |
| Max message | 4 MB (`IpcProtocol.MaxMessageBytes`) |
| Protocol version | `1` (`IpcProtocol.Version`), negotiated by `system.hello` |
| Clock skew | requests older/newer than ±5 minutes are rejected |
| JSON | camelCase properties, camelCase enum strings, polymorphic `kind` discriminator |

## Messages

```jsonc
// request (client → service)
{ "kind": "request", "requestId": "3f0c…", "operation": "optimization.apply",
  "payload": { "ruleId": "windows.game-dvr", "parameters": null }, "timestamp": "2026-09-26T18:00:00Z" }

// response (service → client), exactly one per request
{ "kind": "response", "requestId": "3f0c…", "success": true, "payload": { /* operation result */ },
  "error": null, "timestamp": "…" }

// error response
{ "kind": "response", "requestId": "3f0c…", "success": false,
  "error": { "code": "unauthorized", "message": "This operation requires the STORM OS app." } }

// event (service → subscribed client)
{ "kind": "event", "topic": "telemetry.snapshot", "payload": { /* MetricsSnapshot */ }, "timestamp": "…" }
```

Error codes are the `StormErrorCodes` constants (`validation_failed`, `unauthorized`, `not_found`, `conflict`,
`rate_limited`, `requires_admin`, `unknown_operation`, `service_unavailable`, `internal`, …). Messages are safe to
show to users; exception details are logged, never returned.

## Trust levels

| Level | Who | Allowed |
|---|---|---|
| `Untrusted` | any local process | `system.hello`, `system.health` |
| `LocalUser` | any interactive user process | read-only operations |
| `TrustedClient` | `StormOS.exe` / `storm.exe` from the service's install directory (and same Authenticode signer when the service is signed) | mutating operations |

## Operations

| Operation | Trust | Payload → result |
|---|---|---|
| `system.hello` | Untrusted | `{clientName, clientVersion}` → service version, protocol, trust, capabilities, mock mode |
| `system.health` | Untrusted | → status, uptime, clients, subscribers, sampling mode, provider notes |
| `telemetry.subscribe` | LocalUser | `{intervalMs}` → `true`; then `telemetry.snapshot` events |
| `telemetry.unsubscribe` | LocalUser | → `true` |
| `telemetry.snapshot` | LocalUser | → latest `MetricsSnapshot` |
| `hardware.inventory` | LocalUser | → `HardwareInventory` |
| `frames.status` | LocalUser | → capture status and provider availability |
| `frames.start` | TrustedClient | `{processId}` → status (PresentMon or ETW) |
| `frames.stop` | TrustedClient | → status |
| `games.running` | LocalUser | → running games |
| `optimization.rules` | LocalUser | → service rule descriptors with detection |
| `optimization.detect` | LocalUser | `{ruleId, parameters}` → `RuleDetection` |
| `optimization.apply` | TrustedClient | `{ruleId, parameters}` → `OptimizationRecord` |
| `optimization.history` | LocalUser | → journal |
| `optimization.restore` | TrustedClient | `{changeId}` → record |
| `optimization.restoreAll` | TrustedClient | → records |
| `process.setPriority` | TrustedClient | `{processId, priority}` |
| `process.terminate` | TrustedClient | `{processId, expectedName}` (name must still match) |
| `services.list` | LocalUser | → curated service entries with recommendations |
| `logs.tail` | TrustedClient | `{lines 1–1000}` → recent service log lines (redacted) |
| `sessions.list` | LocalUser | → recorded game sessions |
| `history.metrics` | LocalUser | `{from, to}` (≤ 7 days) → ≤ 2000 aggregated points |
| `benchmark.gaming` | TrustedClient | `{processId, durationSeconds, warmupSeconds, label}` → `BenchmarkResult` |

Events: `telemetry.snapshot`, `game.started`, `game.stopped`, `optimization.changed`, `frames.changed`.

## Limits

Per connection: token bucket (default burst 100, refill 40/s) consumed by each operation's cost; max 8 in-flight
requests; max 8 connections (`Ipc` section of `appsettings.json`). Exceeding returns `rate_limited`.

## Adding an operation

1. Add the name to `IpcOperations` and request/response records to `IpcContracts`.
2. Add an `OperationPolicy` (trust, mutating, cost) — operations without a policy are rejected.
3. Implement `IpcHandler<TRequest>` with `Validate` for every field; the service registers handlers automatically.
4. Add the client method to `IStormServiceClient`/`StormServiceClient` and tests (see
   `tests/StormOS.Integration.Tests`).
