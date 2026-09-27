<#
.SYNOPSIS
  STORM OS build phase 10: Build Storm WinPE.

.DESCRIPTION
  Storm WinPE (STORM OS SETUP menu) is development phase 6 and NOT IMPLEMENTED yet; this phase records that
  status. The ISO currently boots the unmodified Windows Setup from the source media.

.EXAMPLE
  .\scripts\10-build-winpe.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '10' -Title 'Build Storm WinPE' -Function 'Invoke-StormPhaseWinPe' -Options $options)
