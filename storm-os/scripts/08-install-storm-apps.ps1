<#
.SYNOPSIS
  STORM OS build phase 08: Integrate Storm apps.

.DESCRIPTION
  Stages the STORM OS MSI (app, StormOSService, storm CLI) in the image and installs it once at first boot through
  Windows\Setup\Scripts\SetupComplete.cmd. Requires -EnableStormApps and -StormAppsInstaller.

.EXAMPLE
  .\scripts\08-install-storm-apps.ps1 -EnableStormApps -StormAppsInstaller ..\artifacts\installer\StormOS-1.0.0-x64.msi
#>
[CmdletBinding()]
param(
  [switch] $EnableStormApps,
  [string] $StormAppsInstaller,
  [switch] $EnableOverlay,
  [switch] $EnableBenchmarks
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '08' -Title 'Integrate Storm apps' -Function 'Invoke-StormPhaseApps' -Options $options)
