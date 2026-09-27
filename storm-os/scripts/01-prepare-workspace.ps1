<#
.SYNOPSIS
  STORM OS build phase 01: Prepare workspace.

.DESCRIPTION
  Creates the build folders, refuses to continue while images from a previous build are still mounted, optionally
  cleans previous media/output (-CleanBuild; the source and logs are never deleted) and starts a new build state.

.EXAMPLE
  .\scripts\01-prepare-workspace.ps1 -BuildVersion 1.0.0 -CleanBuild
#>
[CmdletBinding()]
param(
  [string] $BuildVersion = '1.0.0',
  [string] $OutputDirectory,
  [switch] $CleanBuild,
  [switch] $EnableGamingDefaults,
  [switch] $EnableStormApps,
  [switch] $EnableOverlay,
  [switch] $EnableBenchmarks,
  [string] $StormAppsInstaller
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '01' -Title 'Prepare workspace' -Function 'Invoke-StormPhaseWorkspace' -Options $options -OutputDirectory $OutputDirectory -NewState)
