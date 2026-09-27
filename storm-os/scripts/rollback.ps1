<#
.SYNOPSIS
  Cleans up after an interrupted or failed STORM OS build.

.DESCRIPTION
  Unloads offline registry hives (HKLM\STORM_BUILD_*), discards every image mounted under build/mounts (changes are
  never committed by a rollback), cleans corrupt DISM mount points and dismounts the source ISO. The source image,
  build/logs and build/output are never deleted. Safe to run repeatedly.

.EXAMPLE
  .\scripts\rollback.ps1
#>
[CmdletBinding()]
param([string] $OutputDirectory)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force

$paths = Get-StormPaths -OutputDirectory $OutputDirectory
$state = Read-StormState -Paths $paths -AllowMissing
Initialize-StormLog -LogDirectory $paths.Logs -BuildId $(if ($state) { $state.buildId } else { 'rollback' })
Write-StormPhaseHeader -Phase 'rollback' -Title 'Roll back interrupted build'
try {
  Assert-StormWindows -Operation 'Rollback'
  if (-not (Test-StormAdministrator)) {
    throw (New-StormError -Code Rollback -Message 'Rollback needs administrator rights (DISM and reg.exe).' -Fix 'Start PowerShell with "Run as administrator".')
  }
  $actions = @(Invoke-StormRollback -Paths $paths -State $state)
  foreach ($action in $actions) { Write-StormLog -Phase 'rollback' -Message $action }
  $remaining = @(Get-StormMountedImages | Where-Object { $_.Path -like "$($paths.Mounts)*" })
  if ($remaining.Count) {
    throw (New-StormError -Code Rollback -Message "Images are still mounted: $(($remaining | ForEach-Object { $_.Path }) -join ', ')" -Fix 'Close programs using build/mounts, restart Windows, then run rollback again.')
  }
  if ($state) {
    Set-StormPhaseResult -State $state -Phase 'rollback' -Status Completed -Message ($actions -join '; ')
    Save-StormState -Paths $paths -State $state
  }
  Write-StormLog -Level Success -Phase 'rollback' -Message 'Rollback complete. Source, logs and output were kept.'
  exit 0
}
catch {
  $info = Get-StormErrorInfo $_
  Write-StormLog -Level Error -Phase 'rollback' -Message "ROLLBACK FAILED: $($info.Message)"
  Write-StormLog -Level Warn -Phase 'rollback' -Message "Suggested fix: $($info.Fix)"
  exit $info.Code
}
