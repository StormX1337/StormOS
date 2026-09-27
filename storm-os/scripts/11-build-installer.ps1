<#
.SYNOPSIS
  STORM OS build phase 11: Commit image and build installer media.

.DESCRIPTION
  Commits and unmounts the customized image, exports it (recompressed) as the only sources\install.wim of the
  installer media and records its SHA-256.

.EXAMPLE
  .\scripts\11-build-installer.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '11' -Title 'Commit image and build installer media' -Function 'Invoke-StormPhaseInstaller' -Options $options)
