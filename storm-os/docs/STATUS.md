# STATUS

Last updated: 2026-09-27. Milestone 1 (valid Storm ISO pipeline) is in progress.

Legend: **Done** = implemented and validated · **Built** = implemented, validated by unit tests, end-to-end run on
Windows pending · **Not implemented** = does not exist yet.

## Completed

| Item | Evidence |
|---|---|
| Phase 0 architecture | docs/ARCHITECTURE.md, repository structure |
| Build engine module, structured logs, exit codes, build state, report | `scripts/lib`, 176 Pester tests (Linux, PowerShell 7) |
| Registry defaults engine with security deny list | `tests/build/Registry.Tests.ps1` |
| Answer-file safety (no disk, key, account, auto-logon, bypass settings) | `tests/build/Branding.Tests.ps1` |
| El Torito BIOS/UEFI boot verification, hash check | `tests/build/Iso.Tests.ps1` |
| Storm branding assets (logos, 4K wallpapers, lock screen, OEM/OOBE logos) | `branding/` |

## In progress

| Item | State |
|---|---|
| Phase 1 prerequisite checker | Built; checked on Linux (clean FAIL with fixes, exit 10); Windows run in CI pending |
| Phases 2-3 source import, edition inspection/selection | Built |
| Phase 3 safe mount/unmount, stale mounts, rollback | Built |
| Phases 4-5 Storm branding and defaults (+ gaming defaults) | Built |
| Phase 7 first StormOS.iso | Built; CI builds it from Windows 11 Enterprise evaluation media (`.github/workflows/storm-os.yml`) |
| Phase 8 VM boot | QEMU boot smoke test built (CI); Hyper-V path built, not exercised in CI |

## Blocked

| Item | Blocker |
|---|---|
| Hyper-V test in CI | GitHub-hosted runners provide no Hyper-V; needs a self-hosted runner or manual run |
| Real hardware tests (NVIDIA/AMD/Intel, laptop/desktop) | needs physical machines |
| Public distribution of StormOS.iso | needs Microsoft distribution rights (docs/LICENSING.md) |

## Not implemented

| Item | Phase |
|---|---|
| Storm WinPE (STORM OS SETUP menu: install/repair/recovery/diagnostics) | 6 |
| Full unattended VM installation test (to the desktop) | 8 |
| Storm Welcome, Storm Settings, Storm Control as separate apps | 9-11 |
| StormGamingService as least-privilege service; Gaming Mode with automatic restore on exit; Balanced/Gaming/Competitive/Max Performance profiles | 12-15 |
| Storm Explorer, Storm Terminal | later |
| StormUpdater/StormUpdateService (separate), Storm Recovery app | 20-21 |
| Storm sound scheme, localization (EN/DE) of new apps, signing of the ISO pipeline outputs | 22 |

Existing groundwork for the gaming and app phases (game detection, profiles, optimization with rollback, telemetry,
frame times, benchmarks, overlay, updater, onboarding) is in the STORM OS app platform (`../src`) and is integrated into
the image with `-EnableStormApps`.

## Next

1. Run the CI pipeline end to end on Windows, fix findings, record the result here (report + screenshots in `refs/ci/storm-os-build`).
2. Full unattended VM installation test with a test-only answer file on separate media, screenshot of the Storm desktop.
3. Storm WinPE (phase 6).
