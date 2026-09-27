<#
.SYNOPSIS
  STORM OS build phase 05: Apply Storm branding.

.DESCRIPTION
  Copies the Storm wallpapers, lock screen image and logos into the mounted image, writes the STORM OS theme and
  sets OEM information and the default theme for new users (config/branding/branding.json).

.EXAMPLE
  .\scripts\05-apply-branding.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '05' -Title 'Apply Storm branding' -Function 'Invoke-StormPhaseBranding' -Options $options)
