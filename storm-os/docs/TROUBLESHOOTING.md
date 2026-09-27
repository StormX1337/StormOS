# Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Administrator: FAIL` | not elevated | Run PowerShell as administrator |
| `oscdimg: FAIL` | ADK Deployment Tools missing | Install the Windows ADK, feature *Deployment Tools* |
| `NTFS workspace: FAIL` | FAT/exFAT/network drive | Move `storm-os` to a local NTFS drive |
| `Workspace path: FAIL` | spaces in the path | Use e.g. `C:\StormOS\storm-os` (oscdimg boot arguments) |
| Exit 21, "explicit edition selection is required" | no `-EditionIndex` in a non-interactive run | Pass `-EditionIndex`; the error lists all editions |
| "is not Windows 11" | Windows 10 or x86 image | Use a Windows 11 x64/ARM64 ISO |
| "split image (install.swm)" | USB-tool layout | Use the original ISO or merge with `dism /Export-Image` |
| Mount fails / "Mount directory is not empty" | leftover mount, open Explorer window, antivirus scan | Close windows in `build\mounts`, run `scripts\rollback.ps1`, retry |
| "Could not unload the offline hive" | Registry Editor has `HKLM\STORM_BUILD_*` open | Close it, run `scripts\rollback.ps1` |
| Images still mounted after rollback | handles held by another process | Restart Windows, run `scripts\rollback.ps1` again (`dism /Cleanup-Mountpoints`) |
| Validation: "Installer bootable (UEFI)" FAIL | media without `efi\microsoft\boot\efisys.bin` | Use complete original Microsoft media |
| USB stick won't take the ISO | install.wim > 4 GB on FAT32 | `dism /Split-Image` (docs/BUILD.md) |
| VM test: display blank | slow emulation (TCG) or firmware issue | Increase `-VmBootMinutes`, prefer KVM/WHPX/Hyper-V, check `build\artifacts\vm` |
| Storm apps not installed after setup | OEM product key (SetupComplete disabled) or MSI error | See `%ProgramData%\StormOS\Logs\storm-apps-install.log` |

Logs: `build/logs/build-<id>.log` (readable), `build-<id>.jsonl` (structured), `dism.log`, `prerequisites.json`,
`editions.json`; report: `build/output/storm-build-report.json`.
