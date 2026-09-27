# build/

Generated workspace, never committed (see `.gitignore`).

| Folder | Content |
|---|---|
| `input/` | optional place for your source ISO (the build only reads it) |
| `iso/` | copy of the installer media; `sources/install.wim` is replaced by the Storm image |
| `mounts/install`, `mounts/verify` | DISM mount points (must be empty between builds) |
| `temp/` | single-edition working WIM and `build-state.json` (phase results) |
| `logs/` | `build-<id>.log`, `build-<id>.jsonl`, `dism.log`, `prerequisites.json`, `editions.json` |
| `output/` | `StormOS.iso`, `StormOS.iso.sha256`, `storm-build-report.json` |
| `artifacts/vm/` | VM boot test screenshots |

Clean up with `scripts/rollback.ps1` (after a failure) and `scripts/15-cleanup.ps1`.
