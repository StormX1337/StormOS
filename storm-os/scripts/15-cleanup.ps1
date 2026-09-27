<#
.SYNOPSIS
  STORM OS build phase 15: Cleanup.

.DESCRIPTION
  Removes temporary files (the build state is kept) after checking that nothing is mounted. -RemoveMedia also
  removes build/iso. Source images, logs and output are never deleted.

.EXAMPLE
  .\scripts\15-cleanup.ps1
#>
[CmdletBinding()]
param(
  [switch] $RemoveMedia
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '15' -Title 'Cleanup' -Function 'Invoke-StormPhaseCleanup' -Options $options)
