<#
.SYNOPSIS
  STORM OS build phase 00: Prerequisites.

.DESCRIPTION
  Checks the build machine: Windows version, administrator rights, DISM, Windows ADK (oscdimg), WinPE add-on,
  PowerShell, .NET SDK, MSBuild, NTFS workspace, free space and the source image. Prints PASS/WARN/FAIL with a
  suggested fix for every problem and exits with code 10 when a mandatory component is missing.

.EXAMPLE
  .\scripts\00-check-prerequisites.ps1 -SourceIso C:\ISO\Win11.iso
#>
[CmdletBinding()]
param(
  [string] $SourceIso,
  [string] $SourceWim,
  [switch] $RequireWinPe
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '00' -Title 'Prerequisites' -Function 'Invoke-StormPhasePrerequisites' -Options $options -NoState)
