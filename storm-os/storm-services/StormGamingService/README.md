# StormGamingService

Status: **NOT IMPLEMENTED** as a separate service. Purpose: Game detection, profiles, Gaming Mode, monitoring and restore.

Existing groundwork: The existing StormOSService (`src/StormOS.Service`) already does game detection, telemetry, frame capture and reversible optimizations. It currently runs as LocalSystem; moving to least privilege (a virtual service account plus narrowly granted rights) is open (docs/STATUS.md).
