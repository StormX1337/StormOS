# STORM OS

**STORM OS is a Storm customization and deployment environment built on Windows.**
It turns a legitimate Windows 11 ISO into `StormOS.iso`: Windows 11 with Storm branding, a gaming-first desktop,
Storm defaults and (optionally) the Storm applications, built by a reproducible, validated pipeline.

Windows remains Microsoft's product and requires a valid Windows license; STORM OS adds its own branding, defaults and
software and never bypasses activation, Setup requirements or security features ([docs/LICENSING.md](docs/LICENSING.md)).

## Build

On Windows 10 2004+ / Windows 11 with the Windows ADK *Deployment Tools*, in an elevated PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-all.ps1 -SourceIso "C:\StormOS\source.iso" -EditionIndex 6
```

```powershell
.\scripts\build-all.ps1 -SourceIso "C:\ISO\Windows11.iso" -EditionIndex 6 -BuildVersion "1.0.0" `
  -EnableGamingDefaults -EnableStormApps -StormAppsInstaller "..\artifacts\installer\StormOS-1.0.0-x64.msi" `
  -EnableOverlay -EnableBenchmarks -CleanBuild
```

Result: `build/output/StormOS.iso`, `StormOS.iso.sha256` and `storm-build-report.json`. Details: [docs/BUILD.md](docs/BUILD.md).

## Layout

| Path | Purpose |
|---|---|
| `scripts/` | phases `00`-`15`, `build-all.ps1`, `rollback.ps1`, build engine module `lib/StormBuild.psm1` |
| `config/` | declarative branding, desktop and gaming defaults, provisioning |
| `branding/` | original Storm logos, wallpapers, icons (+ generator) |
| `storm-apps/`, `storm-services/`, `gaming/` | Storm software (entry points; implementation status in docs/STATUS.md) |
| `tests/` | Pester tests for the build engine, VM and CI helpers |
| `build/` | generated workspace (not committed) |
| `docs/` | architecture, build, gaming, security, licensing, troubleshooting, status |

Status of every phase and milestone: [docs/STATUS.md](docs/STATUS.md).
