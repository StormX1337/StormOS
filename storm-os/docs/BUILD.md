# Building StormOS.iso

## Requirements

| Component | Needed for |
|---|---|
| Windows 10 2004+ or Windows 11 host, **elevated** PowerShell 5.1 or 7 | DISM mounting, offline registry |
| Windows ADK for Windows 11, feature *Deployment Tools* | `oscdimg.exe` (ISO creation) |
| Windows PE add-on | only for the Storm WinPE (phase 6, not implemented) |
| NTFS drive with 40 GB free (60 GB recommended), workspace path without spaces | source copy, WIM, mount, ISO |
| A Windows 11 ISO you are licensed to use | the source (ISO, or install.wim/esd + extracted media) |
| .NET SDK + VS Build Tools | only to build the Storm apps MSI (`../scripts/publish.ps1`) |

`scripts/00-check-prerequisites.ps1` checks all of this and prints PASS / WARN / FAIL with a fix for each problem.

## One command

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-all.ps1 -SourceIso "C:\StormOS\source.iso" -EditionIndex 6
```

| Parameter | Meaning |
|---|---|
| `-SourceIso` | Windows 11 ISO |
| `-SourceWim` + `-SourceMediaDirectory` | install.wim/install.esd plus an extracted Windows 11 ISO for the boot files |
| `-EditionIndex` | edition to build (required; without it the build lists editions and asks, or fails in CI) |
| `-OutputDirectory` | where StormOS.iso and the report go (default `build/output`) |
| `-BuildVersion` | STORM OS version (semantic, default `1.0.0`) |
| `-EnableGamingDefaults` | apply `config/gaming/gaming-defaults.json` |
| `-EnableStormApps` + `-StormAppsInstaller` | stage the STORM OS MSI for first-boot installation |
| `-EnableOverlay`, `-EnableBenchmarks` | defaults for the Storm apps (only with `-EnableStormApps`) |
| `-CleanBuild` | remove previous media/output first (sources and logs are kept) |
| `-IsoName` | output file name (default `StormOS.iso`) |
| `-NoBootPrompt` | UEFI boot without "Press any key" (automated VM tests only) |
| `-RunVmTest`, `-Hypervisor Qemu|HyperV`, `-VmBootMinutes` | boot smoke test after the build |
| `-KeepTemp` | skip cleanup (keeps the build state for standalone phase scripts) |

To list the editions first: `.\scripts\02-import-source.ps1 -SourceIso <iso>` after `01-prepare-workspace.ps1`, then
`.\scripts\03-inspect-image.ps1`.

## Phase by phase

Every phase is also a script that reads and updates `build/temp/build-state.json`:

```powershell
.\scripts\00-check-prerequisites.ps1 -SourceIso C:\ISO\Win11.iso
.\scripts\01-prepare-workspace.ps1 -BuildVersion 1.0.0 -CleanBuild -EnableGamingDefaults
.\scripts\02-import-source.ps1 -SourceIso C:\ISO\Win11.iso
.\scripts\03-inspect-image.ps1 -EditionIndex 6
.\scripts\04-mount-install.ps1
.\scripts\05-apply-branding.ps1
.\scripts\06-apply-defaults.ps1
.\scripts\07-apply-gaming-config.ps1
.\scripts\08-install-storm-apps.ps1            # skipped unless -EnableStormApps
.\scripts\09-configure-oobe.ps1
.\scripts\10-build-winpe.ps1                   # records NOT IMPLEMENTED
.\scripts\11-build-installer.ps1
.\scripts\12-build-iso.ps1
.\scripts\13-validate-image.ps1
.\scripts\14-test-vm.ps1 -Hypervisor HyperV
.\scripts\15-cleanup.ps1
```

## Validation and report

`13-validate-image.ps1` checks: ISO exists, SHA-256 file matches, El Torito boot catalog has BIOS **and** UEFI entries,
volume label, media layout, install.wim (exactly the selected edition), boot.wim, and — by mounting install.wim
read-only — the Storm files, a safe answer file, the image identity and an intact Windows image. `BUILD SUCCESS` is
printed only when every critical check passes.

`build/output/storm-build-report.json`: STORM OS version, Windows source (path, SHA-256, edition, architecture,
version, languages), timestamps and duration, per-phase results, applications, gaming features, every registry value
with its previous and new value, validation results, VM test result and hashes (ISO, install.wim, branding files).

## Exit codes

| Code | Meaning |
|---|---|
| 0 | success |
| 1 | unexpected error |
| 10 | prerequisites missing |
| 15 | workspace problem (e.g. images still mounted) |
| 20 | source invalid |
| 21 | edition selection required or invalid |
| 30 | export/mount failed |
| 40 | customization failed |
| 45 | commit/installer media failed |
| 50 | ISO build failed |
| 60 | validation failed |
| 70 | VM test failed |
| 80 | rollback incomplete |

## Failed builds

The build stops at the failing phase, prints the error and a fix, discards mounted images, unloads offline hives,
dismounts the source ISO and writes the report with `"result": "FAILED"` and the failure details. Logs stay in
`build/logs`. Run `.\scripts\rollback.ps1` any time to clean up an interrupted build.

## USB media

The ISO is UDF and `install.wim` can exceed 4 GB. For FAT32 USB sticks, split it:
`dism /Split-Image /ImageFile:sources\install.wim /SWMFile:sources\install.swm /FileSize:3800`, or use an NTFS/UEFI-capable tool.

## CI

`.github/workflows/storm-os.yml` runs the Pester suites on Windows PowerShell 5.1 and PowerShell 7, then builds a real
StormOS.iso from Microsoft's Windows 11 Enterprise evaluation media on a Windows runner, validates it and boots it in
QEMU. Only the report, logs and screenshots are published (branch `refs/ci/storm-os-build` and the workflow artifact);
the evaluation media and the resulting ISO are never uploaded.
