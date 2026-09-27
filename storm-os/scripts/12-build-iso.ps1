<#
.SYNOPSIS
  STORM OS build phase 12: Build ISO.

.DESCRIPTION
  Builds the BIOS + UEFI bootable StormOS.iso with oscdimg and writes StormOS.iso.sha256. -NoBootPrompt removes the
  "Press any key to boot from CD" prompt (for automated VM tests only).

.EXAMPLE
  .\scripts\12-build-iso.ps1
#>
[CmdletBinding()]
param(
  [string] $OutputDirectory,
  [string] $IsoName,
  [switch] $NoBootPrompt
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '12' -Title 'Build ISO' -Function 'Invoke-StormPhaseIso' -Options $options -OutputDirectory $OutputDirectory)
