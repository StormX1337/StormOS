<#
.SYNOPSIS
  Installs StormOS.iso in a throw-away, Windows 11-compliant VM and captures screenshots of the installed system.

.DESCRIPTION
  Linux host with KVM (CI: ubuntu runner). The VM has UEFI Secure Boot with Microsoft keys (OVMF secboot), a TPM 2.0
  (swtpm), 6 GB RAM, 4 vCPUs and an 80 GB virtual disk, so Windows 11 Setup's requirement checks pass without any
  bypass. The test-only answer file (tests/vm/install/autounattend.xml) is attached as a second CD; StormOS.iso is used
  unchanged. storm-vm-tour.ps1 signals each screen over the VM's serial port and this script captures the display.

  Nothing leaves the VM except screenshots and the serial log. The VM disk is deleted at the end.

.EXAMPLE
  pwsh ./tests/vm/Invoke-StormVmInstallTest.ps1 -IsoPath /mnt/storm/StormOS.iso -WorkDirectory /mnt/storm/vm -OutputDirectory ./storm-os-vm
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $IsoPath,
  [Parameter(Mandatory)] [string] $WorkDirectory,
  [Parameter(Mandatory)] [string] $OutputDirectory,
  [int] $TimeoutMinutes = 100,
  [int] $MemoryMB = 6144,
  [int] $Cpus = 4
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\StormBuild.psm1') -Force

foreach ($tool in 'qemu-system-x86_64', 'qemu-img', 'swtpm', 'xorriso') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is required (apt-get install qemu-system-x86 qemu-utils swtpm xorriso)." }
}
$code = '/usr/share/OVMF/OVMF_CODE_4M.secboot.fd'
$varsTemplate = '/usr/share/OVMF/OVMF_VARS_4M.ms.fd'
foreach ($file in $code, $varsTemplate, '/dev/kvm') { if (-not (Test-Path $file)) { throw "$file is required (ovmf package, KVM)." } }

New-Item -ItemType Directory -Path $WorkDirectory, $OutputDirectory -Force | Out-Null
$tpmDir = Join-Path $WorkDirectory 'tpm'
New-Item -ItemType Directory -Path $tpmDir -Force | Out-Null
$vars = Join-Path $WorkDirectory 'OVMF_VARS.fd'
Copy-Item $varsTemplate $vars -Force
$disk = Join-Path $WorkDirectory 'disk.qcow2'
& qemu-img create -f qcow2 $disk 80G | Out-Null
$answerIso = Join-Path $WorkDirectory 'answer.iso'
& xorriso -as mkisofs -quiet -o $answerIso -J -r -V STORM_TEST (Join-Path $PSScriptRoot 'install') 2>&1 | Out-Null
$serialLog = Join-Path $OutputDirectory 'vm-serial.log'
Set-Content -Path $serialLog -Value '' -NoNewline
$socket = Join-Path $tpmDir 'swtpm.sock'
$port = Get-Random -Minimum 45000 -Maximum 55000

$swtpm = Start-Process -FilePath 'swtpm' -ArgumentList @('socket', '--tpm2', '--tpmstate', "dir=$tpmDir", '--ctrl', "type=unixio,path=$socket", '--log', "file=$(Join-Path $OutputDirectory 'swtpm.log')") -PassThru
for ($i = 0; $i -lt 20 -and -not (Test-Path $socket); $i++) { Start-Sleep -Milliseconds 250 }

$arguments = @(
  '-machine', 'q35,smm=on,accel=kvm', '-global', 'driver=cfi.pflash01,property=secure,value=on',
  '-cpu', 'host,hv_relaxed,hv_vapic,hv_spinlocks=0x1fff,hv_time', '-smp', "$Cpus", '-m', "$MemoryMB",
  '-drive', "if=pflash,format=raw,unit=0,readonly=on,file=$code", '-drive', "if=pflash,format=raw,unit=1,file=$vars",
  '-chardev', "socket,id=chrtpm,path=$socket", '-tpmdev', 'emulator,id=tpm0,chardev=chrtpm', '-device', 'tpm-tis,tpmdev=tpm0',
  '-device', 'ahci,id=ahci',
  '-drive', "id=disk,file=$disk,if=none,format=qcow2", '-device', 'ide-hd,drive=disk,bus=ahci.0,bootindex=1',
  '-drive', "id=cd0,file=$IsoPath,if=none,media=cdrom,readonly=on", '-device', 'ide-cd,drive=cd0,bus=ahci.1,bootindex=2',
  '-drive', "id=cd1,file=$answerIso,if=none,media=cdrom,readonly=on", '-device', 'ide-cd,drive=cd1,bus=ahci.2',
  '-device', 'VGA,edid=on,xres=1600,yres=900', '-display', 'none', '-rtc', 'base=utc',
  '-serial', "file:$serialLog", '-monitor', "tcp:127.0.0.1:$port,server,nowait", '-nic', 'none'
)
Write-Host "Starting the VM (Secure Boot, TPM 2.0, $MemoryMB MB, $Cpus vCPU)"
$qemu = Start-Process -FilePath 'qemu-system-x86_64' -ArgumentList $arguments -PassThru -RedirectStandardError (Join-Path $OutputDirectory 'qemu-stderr.log')

$captured = New-Object System.Collections.Generic.List[string]
$status = New-Object System.Collections.Generic.List[string]
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$progress = 0
$nextProgress = Get-Date
$done = $false
try {
  while ((Get-Date) -lt $deadline -and -not $qemu.HasExited -and -not $done) {
    Start-Sleep -Seconds 5
    $lines = @(Get-Content -Path $serialLog -ErrorAction SilentlyContinue | ForEach-Object { $_.Trim() })
    foreach ($line in ($lines | Where-Object { $_ -like 'STORM-TOUR-STATUS:*' })) {
      if (-not $status.Contains($line)) { $status.Add($line); Write-Host $line }
    }
    foreach ($marker in ($lines | Where-Object { $_ -match '^STORM-TOUR:[a-z-]+$' })) {
      $name = $marker.Substring('STORM-TOUR:'.Length)
      if ($captured.Contains($name)) { continue }
      $captured.Add($name)
      if ($name -eq 'done') { $done = $true; break }
      Start-Sleep -Seconds 3
      $shot = Save-StormQemuScreenshot -Port $port -BasePath (Join-Path $OutputDirectory ('installed-{0:D2}-{1}' -f $captured.Count, $name))
      Write-Host "captured $name$(if ($shot -and $shot.Blank) { ' (blank)' })"
    }
    if (-not $done -and (Get-Date) -ge $nextProgress) {
      $progress++
      $shot = Save-StormQemuScreenshot -Port $port -BasePath (Join-Path $OutputDirectory ('progress-{0:D3}' -f $progress))
      Write-Host "progress screenshot $progress$(if ($shot -and $shot.Blank) { ' (blank)' })"
      $nextProgress = (Get-Date).AddMinutes(2)
    }
  }
}
finally {
  if (-not $qemu.HasExited) {
    try { Send-StormQemuMonitor -Port $port -Commands @('quit') } catch { Write-Host 'monitor quit failed' }
    if (-not $qemu.WaitForExit(20000)) { $qemu.Kill() }
  }
  if (-not $swtpm.HasExited) { $swtpm.Kill() }
  Remove-Item -LiteralPath $disk, $vars -Force -ErrorAction SilentlyContinue
}

$result = [ordered]@{
  passed    = $done
  screens   = @($captured | Where-Object { $_ -ne 'done' })
  status    = @($status)
  detail    = if ($done) { 'Installed, signed in and captured the tour.' } elseif ($qemu.HasExited) { "QEMU exited (code $($qemu.ExitCode)) before the tour finished." } else { "Timed out after $TimeoutMinutes minutes." }
}
$result | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'vm-install-result.json')
Write-Host ($result | ConvertTo-Json -Depth 4)
if (-not $done) { exit 70 }
