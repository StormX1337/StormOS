# Gaming

STORM OS is gaming-first, but never promises "FPS boosts": everything is measured, reversible and explained.

## In the image (milestone 1)

With `-EnableGamingDefaults`, new user profiles get these documented Windows settings (`config/gaming/gaming-defaults.json`):

| Setting | Why | Change back |
|---|---|---|
| Game Mode on | Windows prioritizes the game, defers driver installs/restart prompts while playing | Settings > Gaming > Game Mode |
| Optimizations for windowed games | flip model for DX10/11 borderless games: lower latency, VRR support | Settings > System > Display > Graphics |
| Background recording off | no continuous GPU encoder/disk load | Settings > Gaming > Captures |
| Enhance pointer precision off | 1:1 mouse movement, consistent aim | Mouse settings > Pointer Options |

Not changed in the image: power plans (laptops would drain faster), drivers, services, network stack, security
features. Those are user decisions made in the Storm apps with confirmation and rollback.

## Storm gaming software

The gaming layer exists today as the STORM OS app platform in this repository and is added to the image with
`-EnableStormApps`:

| Capability | Where |
|---|---|
| Game detection (Steam, Epic, Xbox, Battle.net, EA, Ubisoft, Riot, GOG, custom) | `src/StormOS.Games` |
| Game profiles (JSON) | `profiles/`, `docs/PROFILES.md` |
| Reversible optimization engine (detect, snapshot, apply, verify, rollback) | `src/StormOS.Optimization`, `docs/OPTIMIZATION.md` |
| Telemetry, frame times, 1% / 0.1% lows | `src/StormOS.Performance`, `docs/PERFORMANCE.md` |
| Benchmarks with before/after comparison of measured runs only | `src/StormOS.Benchmark`, `docs/BENCHMARK.md` |
| Overlay | `src/StormOS.App`, `docs/OVERLAY.md` |

Open items for the OS distribution (Gaming Mode as an automatic per-game state with restore on exit, performance
profiles Balanced/Gaming/Competitive/Max Performance, Storm Gaming Center as its own app, least-privilege gaming
service) are tracked in [STATUS.md](STATUS.md).
