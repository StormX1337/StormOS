<#
.SYNOPSIS
  STORM OS build phase 02: Import source.

.DESCRIPTION
  Imports the Windows 11 source. ISO: mounted read-only, copied to build/iso, dismounted. WIM/ESD: the image is
  used in place (read only) and the boot media comes from -SourceMediaDirectory. Records the source SHA-256.

.EXAMPLE
  .\scripts\02-import-source.ps1 -SourceIso C:\ISO\Win11.iso
#>
[CmdletBinding()]
param(
  [string] $SourceIso,
  [string] $SourceWim,
  [string] $SourceMediaDirectory
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '02' -Title 'Import source' -Function 'Invoke-StormPhaseImport' -Options $options)
