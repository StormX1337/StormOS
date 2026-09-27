# Security

STORM OS customizes Windows; it never weakens it.

## Never

- No activation, licensing or DRM bypass; no product keys in images or answer files.
- No Windows Update, Defender, firewall, SmartScreen, UAC, Secure Boot or code-integrity changes.
- No Windows 11 requirement bypasses (TPM, Secure Boot, CPU, RAM, storage: `LabConfig`, `MoSetup`) and no OOBE
  network/account bypasses (`BypassNRO`, hidden account screens).
- No automatic logon, no accounts or passwords in answer files.
- No disk partitioning without the user: the answer file has no `windowsPE` pass; Windows Setup asks for the disk.
- No removed Windows packages or disabled services.
- No arbitrary command execution from configuration: registry defaults are data, applied with reg.exe argument
  arrays; no `Invoke-Expression`.

These rules are enforced in code (registry deny list, answer-file safety check) and covered by tests
(`tests/build/Registry.Tests.ps1`, `tests/build/Branding.Tests.ps1`). A build that violates them fails.

## Build machine

The build needs administrator rights for DISM and offline hives. It only reads the source, works inside `build/`,
unloads every hive and discards every mount on failure (`scripts/rollback.ps1`). Logs contain no secrets; the build
handles no credentials.

## Least privilege in the running system

Storm apps run as the user. Administrator changes go through the Storm service with explicit confirmation
(`../SECURITY.md` for the app platform). Moving the gaming service from LocalSystem to a least-privilege account is an
open item ([STATUS.md](STATUS.md)).

## Reporting

Report vulnerabilities privately to security@stormos.app.
