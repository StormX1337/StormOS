<#
.SYNOPSIS
  STORM OS build phase 07: Apply gaming defaults.

.DESCRIPTION
  Applies the declared, reversible gaming defaults (config/gaming/gaming-defaults.json) when -EnableGamingDefaults
  is set. Nothing touches security, update or power settings.

.EXAMPLE
  .\scripts\07-apply-gaming-config.ps1 -EnableGamingDefaults
#>
[CmdletBinding()]
param(
  [switch] $EnableGamingDefaults
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '07' -Title 'Apply gaming defaults' -Function 'Invoke-StormPhaseGaming' -Options $options)
