# Architecture

STORM OS = Windows 11 (legitimate source) + Storm build engine + Storm configuration, branding and software.

```
Windows 11 ISO (read only)
      |
      v
Storm Build Engine (PowerShell, DISM, reg.exe, oscdimg)
  00 prerequisites -> 01 workspace -> 02 import -> 03 inspect/select edition -> 04 export + mount
  05 branding -> 06 defaults -> 07 gaming defaults -> 08 Storm apps -> 09 OOBE
  10 Storm WinPE (not implemented) -> 11 commit + installer media -> 12 ISO -> 13 validate -> 14 VM test -> 15 cleanup
      |
      v
StormOS.iso (+ .sha256, storm-build-report.json)
      |
      v
Windows Setup (unmodified) -> OOBE (Storm OEM name/logo) -> first boot (SetupComplete: Storm apps) -> desktop
```

## Build engine

`scripts/lib/StormBuild.psm1` is split by topic: `Common` (exit codes, errors with fixes, structured logging, build
state), `Prerequisites`, `Source`, `Image` (export, mount, offline hives), `Registry` (declarative defaults + deny list),
`Branding`, `Iso` (oscdimg + El Torito verification), `Validation`, `Report`, `Vm`, `Phases`.

- **Pure vs. effectful**: decisions (validation, argument building, parsing) are pure functions covered by Pester on
  Linux and Windows; DISM, reg.exe, oscdimg, QEMU and Hyper-V calls are thin wrappers.
- **One implementation, two entry points**: each numbered script and `build-all.ps1` call the same phase function.
- **State**: `build/temp/build-state.json` records options, source, edition, per-phase results, customizations
  (before/after values), artifacts and validation, so phases can run standalone and the report is exact.
- **Safety**: sources are only read (ISO mounted read-only and copied, the selected edition is exported); mounts are
  checked for emptiness and stale state; failures trigger a rollback (discard, unload hives, dismount ISO) and never
  delete sources or logs.

## Image customization

Only documented customization points are used:

| What | How |
|---|---|
| Wallpapers, lock screen image, logos | files under `Windows\Web`, `Windows\System32\oobe\info`, `ProgramData\StormOS` |
| Default theme | `Windows\Resources\Themes\StormOS.theme` + `InstallTheme` + answer file `CustomDefaultThemeFile` |
| OEM information | `HKLM\SOFTWARE\...\OEMInformation` (Settings > System > About) |
| OOBE | `oobe.xml` (OEM name and logo); OOBE pages are not skipped or pre-answered |
| Desktop and gaming defaults | declared per-user settings in the default user hive |
| Storm apps | MSI staged in the image, installed once by `SetupComplete.cmd` at first boot |
| Image identity | `ProgramData\StormOS\Config\storm-image.json` |

## Storm software

The Storm applications and service exist in this repository as the STORM OS app platform (`../src`: WinUI 3 app,
StormOSService, storm CLI; `../installer`: MSI + setup exe). The distribution integrates them through phase 08.
Separate Storm Welcome/Settings/Control/Explorer/Terminal apps, the Storm WinPE and the least-privilege gaming service
are later phases ([STATUS.md](STATUS.md)).
