# Gaming: profiles

The implementation lives in the STORM OS application code base; this folder is the OS-distribution entry point.

Game profiles are JSON files in the repository root `profiles/` (schema: `docs/schemas/game-profile.schema.json`), loaded by `src/StormOS.Games`.

Integration into the image: `scripts/08-install-storm-apps.ps1` (`-EnableStormApps`). Status: docs/STATUS.md.
