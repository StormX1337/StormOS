# STATUS

Last updated: 2026-09-28.

**Milestone 1 (valid Storm ISO pipeline): reached, including an automated installation to the desktop in a Windows
11-compliant VM (Secure Boot + TPM 2.0, no requirement bypassed; CI run 6).**

First end-to-end run (CI run 2, `.github/workflows/storm-os.yml`, report in `refs/ci/storm-os-build`):

| | |
|---|---|
| Source | Windows 11 Enterprise Evaluation, x64, 10.0.26200.6584, en-US (Microsoft Evaluation Center) |
| Build | `build-all.ps1` BUILD SUCCESS in 34 min (import 5.5, export+mount 2.4, commit+export 9.5, oscdimg 8.2, validation 7.9 min) |
| Output | StormOS.iso 6.6 GB, label `STORMOS_1_0_0_CI_2`, SHA-256 written and verified |
| Customizations | 19 registry values written and read back (5 branding, 8 desktop, 6 gaming), 12 Storm files in the image |
| Validation | 14/14 PASS: BIOS + UEFI El Torito entries, install.wim (1 edition), boot.wim, media layout, Storm files, answer-file safety, image identity, intact Windows image |
| VM boot | QEMU (UEFI, WHPX): StormOS.iso boots to Windows Setup ("Select language settings") within the first minute |
| VM installation (run 6) | Linux KVM, OVMF Secure Boot with Microsoft keys, swtpm TPM 2.0, 6 GB, 80 GB: installed with the test answer file (separate media), signed in. Read back in the VM: theme `StormOS.theme`, wallpaper `storm-dark.jpg`, dark mode, OEM "STORM OS / Storm customization of Windows 11 Enterprise Evaluation", Game Mode on, `storm-image.json` present. Screenshots: `refs/ci/storm-os-vm` |

Observations from the installed VM:

- Settings > Personalization > Themes shows "STORM OS" as current theme, but lists the background as "Windows
  spotlight" while the Storm wallpaper is displayed (VM without network). Whether Spotlight replaces the wallpaper once
  online is not verified yet.
- The "Windows License is expired" watermark comes from the Microsoft evaluation media used in CI (its evaluation
  period has ended), not from STORM OS. Built from a licensed Windows 11 ISO it does not appear.

## Completed

| Item | Evidence |
|---|---|
| Phase 0 architecture | docs/ARCHITECTURE.md, repository structure |
| Phase 1 prerequisite checker | PASS on Windows Server 2025 (CI); clean FAIL with fixes and exit 10 on unsupported hosts |
| Phase 2 source import (ISO; WIM/ESD with boot media) | CI run 2 (ISO path) |
| Phase 3 image inspection, explicit edition selection, safe mount/unmount, stale mounts, rollback | CI run 2 |
| Phase 4 Storm branding (theme, wallpapers, lock screen image, OEM information, OOBE logo) | CI run 2, validation "Storm files in image" |
| Phase 5 Storm defaults + gaming defaults with deny list and read-back | CI run 2, 19/19 values verified |
| Phase 7 first StormOS.iso, SHA-256, build report | CI run 2 |
| Phase 8 VM boot (QEMU) | CI runs 2, 5 and 6 screenshots; QEMU's "display not initialized" frame no longer counts as booted (false pass in run 3) |
| Phase 8 VM installation (KVM, Secure Boot, TPM 2.0) | CI run 6: installed, signed in, 7 screenshots of the installed system |
| Build engine tests | 186 Pester tests, green on Windows PowerShell 5.1 and PowerShell 7 |

## In progress

| Item | State |
|---|---|
| WIM/ESD source path | implemented and unit tested, not yet exercised end to end in CI |
| Storm apps in the image (`-EnableStormApps`) | implemented (MSI + SetupComplete), not yet exercised end to end |

## Blocked

| Item | Blocker | Way forward |
|---|---|---|
| Hyper-V test in CI | no Hyper-V on hosted runners | self-hosted runner |
| Real hardware tests (NVIDIA/AMD/Intel, laptop/desktop) | needs physical machines | manual test plan |
| Public distribution of StormOS.iso | needs Microsoft distribution rights (docs/LICENSING.md) | licensing |

## Not implemented

| Item | Phase |
|---|---|
| Storm WinPE (STORM OS SETUP menu: install/repair/recovery/diagnostics); the ISO uses the unmodified Windows Setup | 6 |
| Storm Welcome, Storm Settings, Storm Control as separate apps | 9-11 |
| StormGamingService as least-privilege service; Gaming Mode with automatic restore on exit; Balanced/Gaming/Competitive/Max Performance profiles | 12-15 |
| Storm Explorer, Storm Terminal | later |
| StormUpdater/StormUpdateService (separate), Storm Recovery app | 20-21 |
| Storm sound scheme, EN/DE localization of new apps, signed pipeline outputs | 22 |

Existing groundwork for the gaming and app phases (game detection, profiles, optimization with rollback, telemetry,
frame times, benchmarks, overlay, updater, onboarding) is in the STORM OS app platform (`../src`) and is integrated into
the image with `-EnableStormApps`.

## Next

1. Check Windows spotlight vs. the Storm wallpaper with network access; if Spotlight takes over, set the documented
   background type for new users in the image.
2. CI run with `-EnableStormApps` (MSI from the app pipeline) and the WIM/ESD source path.
3. Phase 6: Storm WinPE.
