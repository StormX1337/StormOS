<#
  STORM OS build engine module.

  Shared by the numbered phase scripts (00-15), build-all.ps1 and rollback.ps1. Functions are split by topic into
  StormBuild.*.ps1 files next to this module. Everything that decides something (validation, argument building,
  parsing) is a pure function so it can be unit tested with Pester on any platform; everything that touches the
  machine (DISM, reg.exe, oscdimg, Hyper-V, QEMU) is isolated in small wrappers.

  Runs on Windows PowerShell 5.1 and PowerShell 7.
#>
# Strict mode 1.0 catches uninitialized variables; higher versions would also fail on absent dictionary keys
# in PowerShell 7 but not in Windows PowerShell 5.1, and the build state is a dictionary graph read from JSON.
Set-StrictMode -Version 1.0

$script:StormRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

foreach ($part in 'Common', 'Prerequisites', 'Source', 'Image', 'Registry', 'Branding', 'Iso', 'Validation', 'Report', 'Vm', 'Phases') {
  . (Join-Path $PSScriptRoot "StormBuild.$part.ps1")
}

Export-ModuleMember -Function '*-Storm*'
