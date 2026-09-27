<#
.SYNOPSIS
  STORM OS build phase 04: Export and mount edition.

.DESCRIPTION
  Exports the selected edition into a single-edition WIM (build/temp) and mounts it at build/mounts/install after
  checking that the mount folder is empty and discarding stale mounts. The source image is never mounted read-write.

.EXAMPLE
  .\scripts\04-mount-install.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '04' -Title 'Export and mount edition' -Function 'Invoke-StormPhaseMount' -Options $options)
