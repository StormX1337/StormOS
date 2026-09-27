@echo off
rem STORM OS provisioning. Windows runs Windows\Setup\Scripts\SetupComplete.cmd once as SYSTEM after Setup
rem finishes and before the first sign-in. It installs the STORM OS app, StormOSService and the storm CLI from
rem the MSI staged by scripts/08-install-storm-apps.ps1. Failure is logged and never blocks Windows.
if not exist "%ProgramData%\StormOS\Logs" mkdir "%ProgramData%\StormOS\Logs"
if exist "%WINDIR%\Setup\Scripts\StormOS\StormOS.msi" (
  msiexec.exe /i "%WINDIR%\Setup\Scripts\StormOS\StormOS.msi" /qn /norestart /l*v "%ProgramData%\StormOS\Logs\storm-apps-install.log"
  echo STORM OS apps install exit code %ERRORLEVEL% >> "%ProgramData%\StormOS\Logs\storm-provisioning.log"
)
