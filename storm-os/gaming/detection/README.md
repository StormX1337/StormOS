# Gaming: detection

The implementation lives in the STORM OS application code base; this folder is the OS-distribution entry point.

Launcher scanning (Steam, Epic, Xbox, Battle.net, EA, Ubisoft, Riot, GOG) and running-game detection: `src/StormOS.Games`.

Integration into the image: `scripts/08-install-storm-apps.ps1` (`-EnableStormApps`). Status: docs/STATUS.md.
