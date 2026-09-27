# Gaming: optimization

The implementation lives in the STORM OS application code base; this folder is the OS-distribution entry point.

Reversible optimization engine (detect, snapshot, apply, verify, rollback): `src/StormOS.Optimization`.

Integration into the image: `scripts/08-install-storm-apps.ps1` (`-EnableStormApps`). Status: docs/STATUS.md.
