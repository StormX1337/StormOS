# Registry defaults format

Every registry value STORM OS writes into the image is declared in JSON (`config/defaults`, `config/gaming`,
and generated branding values). `scripts/lib/StormBuild.Registry.ps1` validates each entry before anything is written:

| Field | Meaning |
|---|---|
| `id` | lowercase dotted id, e.g. `gaming.game-mode` |
| `name`, `description`, `reason` | what it does and why STORM OS sets it |
| `category` | POWER, GRAPHICS, GAMING, BACKGROUND, STARTUP, NETWORK, STORAGE, SYSTEM or BRANDING |
| `risk` | `None` or `Low` (image defaults never use higher-risk settings) |
| `hive` | `DefaultUser` (new user profiles) or `Software` (HKLM\SOFTWARE) |
| `key`, `valueName`, `type`, `value` | relative key, value name, REG_SZ / REG_EXPAND_SZ / REG_DWORD, data |
| `editions` | `*` or edition families: Home, Pro, Education, Enterprise |
| `userChange` | where the user changes it back |

A deny list rejects anything touching Defender, UAC, Windows Update, activation, Secure Boot / code integrity,
firewall, SmartScreen, setup requirement bypasses (LabConfig/MoSetup), OOBE bypasses, automatic logon, credentials
and service start types. Each write is read back and recorded (before/after) in the build report.
