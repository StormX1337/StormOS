<#
.SYNOPSIS
  STORM OS build phase 03: Inspect image and select edition.

.DESCRIPTION
  Lists the editions of the install image (index, edition, architecture, version, language, size) and records the
  explicitly selected edition. Without -EditionIndex it asks interactively, or fails with exit code 21 in CI.

.EXAMPLE
  .\scripts\03-inspect-image.ps1 -EditionIndex 6
#>
[CmdletBinding()]
param(
  [int] $EditionIndex
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '03' -Title 'Inspect image and select edition' -Function 'Invoke-StormPhaseInspect' -Options $options)
