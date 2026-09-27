# Security

STORM OS changes system settings on gaming PCs, so its design starts from what it must never do and from the
assumption that every input crossing a process or network boundary is hostile.

## Reporting a vulnerability

Please report vulnerabilities privately to **security@stormos.app** (or a private GitHub security advisory).
Include affected version, reproduction steps and impact. We acknowledge within 3 business days and coordinate
disclosure; please do not open public issues for security problems.

## What STORM OS never does

- No bootloader changes, no Secure Boot, Defender, firewall, SmartScreen, UAC or Windows Update deactivation.
- No kernel drivers, no driver injection, no WinRing0-style port/MSR access, no kernel exploits.
- No process or DLL injection, no game memory access, no hooks into games, no anti-cheat interaction or bypass.
  The overlay is an ordinary layered, click-through, top-most window (`docs/OVERLAY.md`).
- No hidden persistence: the only autostart entries are the visible `StormOSService` service (installed by the MSI)
  and, if the user enables it, a per-user `Run` value named “STORM OS”.
- No arbitrary command execution from UI, IPC, profiles, cloud or AI input. Parameters are data, never code.
- No system change without explicit user confirmation; nothing is applied “in the background”.
- No credential extraction, no logging of passwords, tokens or keys.
- No fabricated measurements: unavailable sensors are reported as *Unavailable* with a reason.

## Privilege model

| Process | Account | Can do |
|---|---|---|
| `StormOS.exe` (app) | interactive user, **not elevated** | read telemetry, run user-scope rules (HKCU, user startup entries, power plan activation, own processes) |
| `storm.exe` (CLI) | interactive user | same as the app; admin actions via the service |
| `StormOS.Service.exe` | LocalSystem | allow-listed privileged operations only (HKLM rules, services, DNS, machine startup, ETW frame capture, gaming benchmark) |

The optimization engine runs in two executors (`app` and `service`); each only executes rules whose
`RequiresAdmin` flag matches its privileges (`OptimizationExecutor`).

## IPC hardening (`\\.\pipe\StormOS.Service.v1`)

- **Pipe ACL**: SYSTEM and Administrators full control, *Interactive* users read/write, *Network* denied,
  `FirstPipeInstance` prevents another process from creating the pipe first.
- **Server identity (client side)**: the client verifies the pipe server process runs as LocalSystem or an
  administrator before sending anything (anti-squatting, `WindowsPipeServerVerifier`).
- **Client identity (server side)**: the service reads the client PID (`GetNamedPipeClientProcessId`), resolves the
  user from that process's token and the executable path from the process. It never impersonates the client, so its
  own threads keep the LocalSystem security context. Clients connect with *Identification* level only. Trust levels:
  - `Untrusted` – `system.hello`, `system.health` only.
  - `LocalUser` – read-only operations (telemetry, inventory, rule catalog, history).
  - `TrustedClient` – `StormOS.exe` / `storm.exe` located in the service's own install directory (Program Files,
    admin-writable only) and, when the service binary is Authenticode-signed, signed by the same signer.
- **Allow-list**: every operation has a policy (`OperationPolicies`) with required trust, mutating flag and cost.
  Unknown operations are rejected (`unknown_operation`).
- **Validation**: typed payloads; ids, parameter names and values are validated (`InputValidator`): identifiers
  are `[a-z0-9._:-]`, values are length-bounded and reject control and shell/markup metacharacters. Rules validate
  their own parameters again (e.g. DNS servers must be public unicast IPv4).
- **Limits**: 4 MB max message, protocol version check, ±5 minute timestamp window, per-connection token-bucket
  rate limiting weighted by operation cost, max concurrent connections and in-flight requests.
- **Audit**: every mutating request is logged to the `Security` log category with client identity and outcome.

## Optimization safety

Every change runs *validate → detect → snapshot → apply → verify → record*. A failed apply or verification rolls back
automatically from the snapshot. Changes are journaled in SQLite and can be restored individually or all at once.
Rules that cannot be reversed (`CanRollback = false`, e.g. temp cleanup) say so before confirmation.
Protected processes (system, security products, anti-cheat) are never re-prioritized or terminated
(`ProcessProtection`). Service changes are limited to a curated knowledge base with recommendations only for
optional services; there is no “disable all services” action.

## Data protection

- Local data: `%LOCALAPPDATA%\StormOS` (user) and `%ProgramData%\StormOS` (service), SQLite with WAL.
- Secrets (cloud refresh token, license token) are encrypted with DPAPI (`SecureValueStore`, user scope).
- Logs are structured JSON with a redaction enricher that masks bearer tokens, JWTs and `password=`, `token=`, `secret=`, `api_key=` style values.
- Privacy defaults are data-minimal: telemetry, crash reports, cloud sync and anonymous diagnostics are **off**.

## Updates

Update metadata comes from STORM Cloud over HTTPS. The installer is downloaded over HTTPS, its SHA-256 is verified
against the release record, and (when configured) its Authenticode signer must match the running application's
signer before it is started. Failed checks delete the file.

## Cloud

- Passwords: scrypt (N=2^15, r=8, p=1) with per-user salt; constant-time verification; unknown accounts take the
  same time.
- Access tokens: HS256 JWT, 15 minutes. Refresh tokens: 256-bit random, stored as SHA-256 hashes, rotated on every
  use; reuse of a rotated token revokes the whole token family.
- License tokens: ES256 compact JWS bound to a device id, time-limited, verified offline by the desktop app.
- Stripe webhooks: signature verified on the raw body; events processed exactly once (advisory lock + event table).
- All input validated with Zod; uniform error bodies without internals; Helmet headers; CORS allow-list;
  per-IP and per-route rate limits (Redis-backed when available); audit log for security and admin actions.
- The optional AI analysis receives aggregated measurements only; its output is schema-constrained, re-validated,
  every evidence line must quote a submitted measurement, and actions are limited to an allow-list of reversible
  rules that the user must still confirm in the app.

## Build and supply chain

Central package management with pinned versions, `TreatWarningsAsErrors` with .NET analyzers, `pnpm` lockfile with
`--frozen-lockfile` in CI and an allow-list for dependency install scripts. Release artifacts are signed when a
signing certificate is configured (`.github/workflows/release.yml`), and SHA-256 files are published with each MSI.
