# Gaming: metrics

The implementation lives in the STORM OS application code base; this folder is the OS-distribution entry point.

Telemetry, frame-time capture (PresentMon, ETW) and statistics (1% / 0.1% lows): `src/StormOS.Performance`.

Integration into the image: `scripts/08-install-storm-apps.ps1` (`-EnableStormApps`). Status: docs/STATUS.md.
