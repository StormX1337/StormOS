# Game profiles

Profiles are versioned JSON documents in `profiles/` (bundled) and `%ProgramData%\StormOS\profiles`
(overrides; a profile with the same id in a later directory replaces the bundled one). They contain **data only**: no scripts, no commands, no file paths
to execute. Every profile is validated at load time (`GameProfileValidator`); invalid profiles are skipped and
reported (`storm games`, logs). JSON Schema: [`schemas/game-profile.schema.json`](schemas/game-profile.schema.json).

## Example

```json
{
  "schemaVersion": 1,
  "id": "cs2",
  "version": "1.0.0",
  "name": "Counter-Strike 2",
  "publisher": "Valve",
  "antiCheat": "VAC",
  "detection": {
    "executables": [{ "name": "cs2.exe", "pathContains": "game\\bin\\win64" }],
    "launchers": [{ "launcher": "steam", "gameId": "730" }]
  },
  "launch": { "uri": "steam://rungameid/730", "recommendedArguments": [{ "title": "-fullscreen", "detail": "…" }] },
  "process": { "priority": "aboveNormal", "reason": "…" },
  "systemRecommendations": [{ "ruleId": "windows.game-mode", "reason": "…" }],
  "optimizationRules": [{ "ruleId": "power.plan", "parameters": { "plan": "high-performance" }, "reason": "…", "sessionScoped": true }],
  "graphicsGuidance": [{ "title": "NVIDIA Reflex", "detail": "…" }],
  "networkGuidance": [{ "title": "…", "detail": "…" }],
  "rollback": { "restoreOnExit": true },
  "benchmark": { "durationSeconds": 60, "warmupSeconds": 5, "scenario": "…" }
}
```

## Fields

| Field | Required | Notes |
|---|---|---|
| `schemaVersion` | ✓ | `1` |
| `id` | ✓ | `[a-z0-9-]`, unique |
| `version` | ✓ | semantic version of the profile |
| `name` | ✓ | display name |
| `detection.executables[]` | ✓ (or launchers) | `name` (file name), optional `pathContains`, `commandLineContains` (requires reading the command line, only used when needed) |
| `detection.launchers[]` | | `launcher` (`steam`, `epic`, `xbox`, `battleNet`, `riot`, `ubisoft`, `ea`, `gog`) + launcher game id |
| `launch.uri` | | only `steam:`, `com.epicgames.launcher:`, `uplay:`, `goggalaxy:`, `battlenet:`, `origin2:`, `link2ea:` schemes are launched |
| `process.priority` | | `belowNormal`, `normal`, `aboveNormal`, `high` — applied while the game runs (never `realtime`) |
| `systemRecommendations[]` / `optimizationRules[]` | | `ruleId` must exist; `parameters` are validated by the rule; `sessionScoped` rules are restored when the game exits |
| `graphicsGuidance[]`, `networkGuidance[]` | | advice shown to the user, nothing is changed automatically |
| `antiCheat` | | informational; STORM OS never interacts with anti-cheat software |
| `minWindowsBuild` | | default 19041 |

## Distribution

Profiles can also be published from Storm Admin; the desktop app downloads published profiles from
`GET /api/v1/profiles` when cloud features are enabled. Server-side validation uses the same rules
(`@storm/validation` `gameProfileSchema`).
