<#
.SYNOPSIS
  STORM OS build phase 13: Validate build.

.DESCRIPTION
  Validates the result: ISO present, hash matches, El Torito BIOS and UEFI boot entries, install.wim and boot.wim
  readable, Storm files and a safe answer file inside the image. Fails (exit 60) if a critical check fails.

.EXAMPLE
  .\scripts\13-validate-image.ps1
#>
[CmdletBinding()]
param(
  [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '13' -Title 'Validate build' -Function 'Invoke-StormPhaseValidate' -Options $options -OutputDirectory $OutputDirectory)
