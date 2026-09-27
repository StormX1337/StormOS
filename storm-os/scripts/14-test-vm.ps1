<#
.SYNOPSIS
  STORM OS build phase 14: VM boot test.

.DESCRIPTION
  Boots StormOS.iso in a temporary QEMU or Hyper-V VM (UEFI; Hyper-V with Secure Boot and vTPM), captures
  screenshots and passes when the VM keeps running and shows output. A full unattended installation test is not
  implemented yet (docs/STATUS.md).

.EXAMPLE
  .\scripts\14-test-vm.ps1 -Hypervisor HyperV
#>
[CmdletBinding()]
param(
  [ValidateSet('Qemu', 'HyperV')] [string] $Hypervisor = 'Qemu',
  [int] $VmBootMinutes = 10
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force
$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
$options['RunVmTest'] = $true
exit (Invoke-StormPhase -Phase '14' -Title 'VM boot test' -Function 'Invoke-StormPhaseVmTest' -Options $options)
