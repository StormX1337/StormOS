# Provisioning

`SetupComplete.storm.cmd` is appended to `Windows\Setup\Scripts\SetupComplete.cmd` by
`scripts/08-install-storm-apps.ps1` when `-EnableStormApps` is used. Windows runs it once as SYSTEM after Setup and
before the first sign-in; it installs the staged STORM OS MSI (app, StormOSService, storm CLI) and logs to
`%ProgramData%\StormOS\Logs`. A failed install is logged and never blocks Windows.

Note: Windows skips SetupComplete.cmd when an OEM product key is used; retail and digital licenses are unaffected.
