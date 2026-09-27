# STATUS

Last updated: 2026-09-27.

**Milestone 1 (valid Storm ISO pipeline): reached for build, validation and VM boot. The full VM installation to the
desktop is blocked in CI (no virtual TPM on hosted runners, and Windows 11 requirements are never bypassed).**

First end-to-end run (CI run 2, `.github/workflows/storm-os.yml`, report in `refs/ci/storm-os-build`):

| | |
|---|---|
| Source | Windows 11 Enterprise Evaluation, x64, 10.0.26200.6584, en-US (Microsoft Evaluation Center) |
| Build | `build-all.ps1` BUILD SUCCESS in 34 min (import 5.5, export+mount 2.4, commit+export 9.5, oscdimg 8.2, validation 7.9 min) |
| Output | StormOS.iso 6.6 GB, label `STORMOS_1_0_0_CI_2`, SHA-256 written and verified |
| Customizations | 19 registry values written and read back (5 branding, 8 desktop, 6 gaming), 12 Storm files in the image |
| Validation | 14/14 PASS: BIOS + UEFI El Torito entries, install.wim (1 edition), boot.wim, media layout, Storm files, answer-file safety, image identity, intact Windows image |
| VM boot | QEMU (UEFI, WHPX): StormOS.iso boots to Windows Setup ("Select language settings") within the first minute |

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
| Phase 8 VM boot (QEMU) | CI run 2 screenshot |
| Build engine tests | 179 Pester tests, green on Windows PowerShell 5.1 and PowerShell 7 |

## In progress

| Item | State |
|---|---|
| Phase 8 VM installation | boot verified; installing needs a VM with TPM 2.0 + Secure Boot (see Blocked) |
| WIM/ESD source path | implemented and unit tested, not yet exercised end to end in CI |
| Storm apps in the image (`-EnableStormApps`) | implemented (MSI + SetupComplete), not yet exercised end to end |

## Blocked

| Item | Blocker | Way forward |
|---|---|---|
| Automated full installation test | Windows 11 Setup requires TPM 2.0; hosted Windows runners have no Hyper-V and QEMU for Windows has no TPM emulator. Bypassing the check is not allowed. | self-hosted runner with Hyper-V (`14-test-vm.ps1 -Hypervisor HyperV` creates Gen 2 + Secure Boot + vTPM), or Linux + KVM + swtpm with private artifact storage; meanwhile manual test (tests/vm/README.md) |
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

1. Manual VM installation of StormOS.iso in Hyper-V (Gen 2, Secure Boot, vTPM) to confirm theme, wallpaper, OEM
   information and defaults on the desktop; record screenshots here.
2. CI run with `-EnableStormApps` (MSI from the app pipeline) and the WIM/ESD source path.
3. Phase 6: Storm WinPE.
