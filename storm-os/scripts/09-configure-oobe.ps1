<#
.SYNOPSIS
  STORM OS build phase 09: Configure OOBE.

.DESCRIPTION
  Writes the OEM oobe.xml (STORM OS name and logo during Windows OOBE) and an answer file that only sets the default
  theme. No disk, product key, account, auto-logon or requirement-bypass settings are ever written.

.EXAMPLE
  .\scripts\09-configure-oobe.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
exit (Invoke-StormPhase -Phase '09' -Title 'Configure OOBE' -Function 'Invoke-StormPhaseOobe' -Options $options)
