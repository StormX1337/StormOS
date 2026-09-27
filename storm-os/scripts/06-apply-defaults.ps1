<#
.SYNOPSIS
  STORM OS build phase 06: Apply Storm defaults.

.DESCRIPTION
  Applies the declared desktop defaults (config/defaults/desktop-defaults.json) to the default user profile and
  writes the image identity (ProgramData\StormOS\Config\storm-image.json). Every value is verified by reading it back.

.EXAMPLE
  .\scripts\06-apply-defaults.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '06' -Title 'Apply Storm defaults' -Function 'Invoke-StormPhaseDefaults' -Options $options)
