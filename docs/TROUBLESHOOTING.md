# Troubleshooting

Logs: `%LOCALAPPDATA%\StormOS\logs` (app, CLI) and `%ProgramData%\StormOS\logs` (service), structured JSON per
category (Application, Service, Optimization, Hardware, Network, Benchmark, Security). `storm logs` shows the
latest app log, `storm logs --service` the service log (via IPC). Secrets are redacted at write time.

| Symptom | Cause and fix |
|---|---|
| *Limited mode — the STORM OS service is not running* | Start it: `storm service start` (admin) or *services.msc › STORM OS Service*. Check the Windows Event Log (*Application*, source `StormOSService`) and the service log. Monitoring and user-level optimizations keep working without the service. |
| “This operation requires the STORM OS app” (`unauthorized`) | Admin operations are accepted only from `StormOS.exe`/`storm.exe` in the service's install folder. Run the installed app, not a copy from another folder; reinstall if files were moved. |
| CPU temperature *Unavailable* | The PC does not expose a CPU thermal zone through ACPI. STORM OS does not load kernel drivers to read MSRs, by design. Use the GPU/board vendor tool if you need it. |
| GPU temperature/clock *Unavailable* | NVIDIA: install a current driver (NVML). AMD/Intel: requires a WDDM 2.4+ driver that reports D3DKMT performance data. |
| No FPS | Start frame capture on the Performance page or enable *capture frames automatically*. Install PresentMon 2.x into Program Files (or set its path in *Settings › Advanced*) or keep the ETW fallback enabled. Exclusive-fullscreen UWP titles may not expose presents to ETW. |
| Overlay not visible in game | Switch the game to borderless/windowed fullscreen; exclusive fullscreen draws above all windows. Check the hotkey (default `Ctrl+Shift+F10`). |
| Latency / Network Score *Insufficient data* | ICMP to the latency target is blocked. Change the target in *Settings › Network*. |
| An optimization shows *Failed — rolled back* | Verification did not see the expected value (policy, antivirus or another tool reverted it). Nothing is left half-applied; details are in the Optimization log. |
| Restore failed | The setting was changed by something else after STORM OS applied it, or the resource is gone (e.g. adapter removed). The History page shows the stored previous value so you can set it manually. |
| Benchmark “another benchmark is already running” | Only one benchmark runs at a time (including the Dashboard quick benchmark). |
| Disk benchmark fails | Needs ~512 MB free on the system drive in `%LOCALAPPDATA%\StormOS\benchmark`; some encrypted/virtual drives reject unbuffered I/O. |
| Cloud: “STORM Cloud is not reachable” | STORM OS keeps working offline; license tokens stay valid until they expire (up to 72 h). Check the API address in *Settings › STORM Cloud* (HTTPS only). |
| Update check fails integrity | The download did not match the published SHA-256 or signer and was deleted. Retry; if it persists, report it. |

## Cloud API

| Symptom | Fix |
|---|---|
| API exits with `Invalid configuration: …` | The message names the variable; see `.env.example`. |
| `ENTITLEMENT_PRIVATE_KEY must be a P-256 …` | Generate with `openssl ecparam -name prime256v1 -genkey -noout \| openssl pkcs8 -topk8 -nocrypt`. |
| Stripe webhook returns 400 | Wrong `STRIPE_WEBHOOK_SECRET` for this endpoint, or a proxy altered the body. |
| Subscription paid but tier unchanged | The price id is not configured in `STRIPE_PRICE_*` (logged as “unknown price”), or the webhook endpoint lacks `customer.subscription.*` events. |
| `/health` 503 | Database unreachable or Redis configured but down. |
