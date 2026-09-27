# VM boot smoke test: boots StormOS.iso in QEMU or Hyper-V, captures screenshots and checks that the VM stays up
# and shows something. This proves the media boots; a full unattended installation test is a separate, later step
# (docs/STATUS.md). The VM is created and removed by the test; it never touches physical disks.

function Test-StormImageBlank {
  <# Samples an RGB24 frame; "blank" when luminance barely varies (black screen, firmware hang with an empty frame). #>
  param([Parameter(Mandatory)] [byte[]] $Rgb, [Parameter(Mandatory)] [int] $Width, [Parameter(Mandatory)] [int] $Height)
  $pixels = $Width * $Height
  if ($pixels -le 0 -or $Rgb.Length -lt $pixels * 3) { return $true }
  $step = [math]::Max(1, [int]($pixels / 4000))
  $min = 255.0; $max = 0.0
  for ($i = 0; $i -lt $pixels; $i += $step) {
    $o = $i * 3
    $luma = 0.299 * $Rgb[$o] + 0.587 * $Rgb[$o + 1] + 0.114 * $Rgb[$o + 2]
    if ($luma -lt $min) { $min = $luma }
    if ($luma -gt $max) { $max = $luma }
  }
  return ($max - $min) -lt 12
}

function ConvertFrom-StormPpm {
  <# Parses a binary PPM (P6, maxval 255) as written by QEMU's screendump. Returns Width, Height and RGB bytes. #>
  param([Parameter(Mandatory)] [byte[]] $Bytes)
  $position = 0
  $tokens = New-Object System.Collections.Generic.List[string]
  while ($tokens.Count -lt 4 -and $position -lt $Bytes.Length) {
    while ($position -lt $Bytes.Length -and [char]::IsWhiteSpace([char]$Bytes[$position])) { $position++ }
    if ($position -lt $Bytes.Length -and [char]$Bytes[$position] -eq '#') {
      while ($position -lt $Bytes.Length -and $Bytes[$position] -ne 10) { $position++ }
      continue
    }
    $start = $position
    while ($position -lt $Bytes.Length -and -not [char]::IsWhiteSpace([char]$Bytes[$position])) { $position++ }
    $tokens.Add([System.Text.Encoding]::ASCII.GetString($Bytes, $start, $position - $start))
  }
  if ($tokens.Count -lt 4 -or $tokens[0] -ne 'P6' -or $tokens[3] -ne '255') { throw 'Not a P6/255 PPM image.' }
  $position++  # single whitespace after maxval
  $width = [int]$tokens[1]; $height = [int]$tokens[2]
  $length = $width * $height * 3
  if ($Bytes.Length - $position -lt $length) { throw 'PPM pixel data is truncated.' }
  $rgb = New-Object byte[] $length
  [Array]::Copy($Bytes, $position, $rgb, 0, $length)
  return [pscustomobject]@{ Width = $width; Height = $height; Rgb = $rgb }
}

function ConvertFrom-StormRgb565 {
  <# Hyper-V thumbnails are RGB565 little-endian; converts to RGB24. #>
  param([Parameter(Mandatory)] [byte[]] $Bytes, [Parameter(Mandatory)] [int] $Width, [Parameter(Mandatory)] [int] $Height)
  $rgb = New-Object byte[] ($Width * $Height * 3)
  for ($i = 0; $i -lt $Width * $Height; $i++) {
    $value = [int]$Bytes[2 * $i] -bor ([int]$Bytes[2 * $i + 1] -shl 8)
    $rgb[3 * $i] = [byte](($value -shr 11) -band 0x1F) * 8
    $rgb[3 * $i + 1] = [byte](($value -shr 5) -band 0x3F) * 4
    $rgb[3 * $i + 2] = [byte]($value -band 0x1F) * 8
  }
  return , $rgb
}

function Save-StormRgbPng {
  <# Writes RGB24 pixels as PNG via System.Drawing (Windows). Returns $false where System.Drawing is unavailable. #>
  param([Parameter(Mandatory)] [byte[]] $Rgb, [Parameter(Mandatory)] [int] $Width, [Parameter(Mandatory)] [int] $Height, [Parameter(Mandatory)] [string] $Path)
  if (-not (Test-StormIsWindows)) { return $false }
  Add-Type -AssemblyName System.Drawing
  $bitmap = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  try {
    $rect = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, $bitmap.PixelFormat)
    try {
      $stride = $data.Stride
      $row = New-Object byte[] $stride
      for ($y = 0; $y -lt $Height; $y++) {
        for ($x = 0; $x -lt $Width; $x++) {
          $source = ($y * $Width + $x) * 3
          $row[3 * $x] = $Rgb[$source + 2]      # GDI+ stores BGR
          $row[3 * $x + 1] = $Rgb[$source + 1]
          $row[3 * $x + 2] = $Rgb[$source]
        }
        [System.Runtime.InteropServices.Marshal]::Copy($row, 0, [IntPtr]($data.Scan0.ToInt64() + [long]$y * $stride), $stride)
      }
    }
    finally { $bitmap.UnlockBits($data) }
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    return $true
  }
  finally { $bitmap.Dispose() }
}

function Find-StormQemu {
  $command = Get-Command 'qemu-system-x86_64' -ErrorAction SilentlyContinue
  if ($command) { return $command.Source }
  if (Test-StormIsWindows) {
    $default = Join-Path $env:ProgramFiles 'qemu\qemu-system-x86_64.exe'
    if (Test-Path -LiteralPath $default) { return $default }
  }
  return $null
}

function Find-StormUefiFirmware {
  <# Returns @{ Code; Vars } (split flash) or @{ Code; Vars = $null } (combined image) for x64 UEFI, or $null. #>
  param([string] $QemuPath)
  $pairs = @()
  if ($QemuPath) {
    $share = Join-Path (Split-Path -Parent $QemuPath) 'share'
    $pairs += , @((Join-Path $share 'edk2-x86_64-code.fd'), (Join-Path $share 'edk2-i386-vars.fd'))
  }
  $pairs += , @('/usr/share/OVMF/OVMF_CODE_4M.fd', '/usr/share/OVMF/OVMF_VARS_4M.fd')
  $pairs += , @('/usr/share/OVMF/OVMF_CODE.fd', '/usr/share/OVMF/OVMF_VARS.fd')
  $pairs += , @('/usr/share/qemu/edk2-x86_64-code.fd', '/usr/share/qemu/edk2-i386-vars.fd')
  foreach ($pair in $pairs) {
    if (Test-Path -LiteralPath $pair[0]) {
      $vars = if (Test-Path -LiteralPath $pair[1]) { $pair[1] } else { $null }
      return [pscustomobject]@{ Code = $pair[0]; Vars = $vars }
    }
  }
  foreach ($combined in '/usr/share/ovmf/OVMF.fd', '/usr/share/OVMF/OVMF.fd') {
    if (Test-Path -LiteralPath $combined) { return [pscustomobject]@{ Code = $combined; Vars = $null } }
  }
  return $null
}

function Get-StormQemuAccelerator {
  if (Test-StormIsWindows) {
    $feature = Get-CimInstance -ClassName Win32_OptionalFeature -Filter "Name='HypervisorPlatform'" -ErrorAction SilentlyContinue
    if ($feature -and $feature.InstallState -eq 1) { return 'whpx' }
    return 'tcg'
  }
  if (Test-Path '/dev/kvm') { return 'kvm' }
  return 'tcg'
}

function New-StormQemuArguments {
  param(
    [Parameter(Mandatory)] [string] $IsoPath,
    [Parameter(Mandatory)] [string] $FirmwareCode,
    [string] $FirmwareVars,
    [Parameter(Mandatory)] [int] $MonitorPort,
    [ValidateSet('kvm', 'whpx', 'tcg')] [string] $Accelerator = 'tcg',
    [int] $MemoryMB = 4096,
    [int] $Cpus = 4
  )
  $accel = if ($Accelerator -eq 'tcg') { 'tcg,thread=multi' } else { $Accelerator }
  $arguments = @('-machine', 'q35', '-accel', $accel, '-cpu', 'max', '-smp', "$Cpus", '-m', "$MemoryMB")
  if ($FirmwareVars) {
    $arguments += @('-drive', "if=pflash,format=raw,unit=0,readonly=on,file=$FirmwareCode", '-drive', "if=pflash,format=raw,unit=1,file=$FirmwareVars")
  }
  else {
    $arguments += @('-bios', $FirmwareCode)
  }
  $arguments += @('-drive', "file=$IsoPath,media=cdrom,readonly=on", '-boot', 'order=d', '-vga', 'std', '-display', 'none',
    '-monitor', "tcp:127.0.0.1:$MonitorPort,server,nowait", '-no-reboot', '-nic', 'none')
  return $arguments
}

function Send-StormQemuMonitor {
  param([Parameter(Mandatory)] [int] $Port, [Parameter(Mandatory)] [string[]] $Commands)
  $client = New-Object System.Net.Sockets.TcpClient
  try {
    $client.Connect('127.0.0.1', $Port)
    $stream = $client.GetStream()
    $writer = New-Object System.IO.StreamWriter($stream)
    $writer.AutoFlush = $true
    Start-Sleep -Milliseconds 300
    foreach ($command in $Commands) {
      $writer.Write("$command`n")
      Start-Sleep -Milliseconds 400
    }
  }
  finally { $client.Dispose() }
}

function Save-StormQemuScreenshot {
  param([Parameter(Mandatory)] [int] $Port, [Parameter(Mandatory)] [string] $BasePath)
  $ppm = "$BasePath.ppm"
  if (Test-Path -LiteralPath $ppm) { Remove-Item -LiteralPath $ppm -Force }
  Send-StormQemuMonitor -Port $Port -Commands @("screendump $($ppm.Replace('\', '/'))")
  for ($i = 0; $i -lt 20 -and -not (Test-Path -LiteralPath $ppm); $i++) { Start-Sleep -Milliseconds 500 }
  if (-not (Test-Path -LiteralPath $ppm)) { return $null }
  Start-Sleep -Milliseconds 500
  $image = ConvertFrom-StormPpm -Bytes ([System.IO.File]::ReadAllBytes($ppm))
  $png = "$BasePath.png"
  if (Save-StormRgbPng -Rgb $image.Rgb -Width $image.Width -Height $image.Height -Path $png) { Remove-Item -LiteralPath $ppm -Force; $file = $png } else { $file = $ppm }
  return [pscustomobject]@{ Path = $file; Blank = (Test-StormImageBlank -Rgb $image.Rgb -Width $image.Width -Height $image.Height); Width = $image.Width; Height = $image.Height }
}

function Invoke-StormQemuBootTest {
  <#
    Boots the ISO in QEMU (UEFI), presses a key during the "Press any key to boot from CD" window, captures a
    screenshot every interval and passes when the VM is still running at the end and the last frame is not blank.
  #>
  param(
    [Parameter(Mandatory)] [string] $IsoPath,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [int] $BootMinutes = 10,
    [int] $IntervalSeconds = 60,
    [string] $Phase = '14'
  )
  $qemu = Find-StormQemu
  if (-not $qemu) { throw (New-StormError -Code VmTest -Message 'QEMU (qemu-system-x86_64) was not found.' -Fix 'Install QEMU (https://www.qemu.org/download) or use -Hypervisor HyperV.') }
  $firmware = Find-StormUefiFirmware -QemuPath $qemu
  if (-not $firmware) { throw (New-StormError -Code VmTest -Message 'No x64 UEFI firmware (OVMF/edk2) was found for QEMU.' -Fix 'Install the ovmf package (Linux) or a QEMU build that ships share\edk2-x86_64-code.fd.') }
  New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
  $vars = $null
  if ($firmware.Vars) {
    $vars = Join-Path $OutputDirectory 'uefi-vars.fd'
    Copy-Item -LiteralPath $firmware.Vars -Destination $vars -Force
  }
  $accelerator = Get-StormQemuAccelerator
  $port = Get-Random -Minimum 45000 -Maximum 55000
  $arguments = New-StormQemuArguments -IsoPath $IsoPath -FirmwareCode $firmware.Code -FirmwareVars $vars -MonitorPort $port -Accelerator $accelerator
  Write-StormLog -Phase $Phase -Message "Starting QEMU ($accelerator) for $BootMinutes minutes: $qemu"
  $stdout = Join-Path $OutputDirectory 'qemu-stdout.log'
  $stderr = Join-Path $OutputDirectory 'qemu-stderr.log'
  $process = Start-Process -FilePath $qemu -ArgumentList ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
  $shots = New-Object System.Collections.Generic.List[object]
  try {
    Start-Sleep -Seconds 3
    # Answer "Press any key to boot from CD or DVD" (shown for about 5 seconds after the firmware hands over).
    for ($i = 0; $i -lt 15 -and -not $process.HasExited; $i++) {
      try { Send-StormQemuMonitor -Port $port -Commands @('sendkey ret') } catch { Write-StormLog -Level Detail -Phase $Phase -Message "monitor not ready: $($_.Exception.Message)" }
      Start-Sleep -Seconds 2
    }
    $deadline = (Get-Date).AddMinutes($BootMinutes)
    $index = 0
    while ((Get-Date) -lt $deadline -and -not $process.HasExited) {
      Start-Sleep -Seconds $IntervalSeconds
      if ($process.HasExited) { break }
      $index++
      $shot = Save-StormQemuScreenshot -Port $port -BasePath (Join-Path $OutputDirectory ('vm-boot-{0:D2}' -f $index))
      if ($shot) {
        $shots.Add($shot)
        Write-StormLog -Phase $Phase -Message "screenshot $(Split-Path -Leaf $shot.Path)$(if ($shot.Blank) { ' (blank)' })"
      }
    }
    $running = -not $process.HasExited
  }
  finally {
    if (-not $process.HasExited) {
      try { Send-StormQemuMonitor -Port $port -Commands @('quit') } catch { Write-StormLog -Level Detail -Phase $Phase -Message 'monitor quit failed' }
      if (-not $process.WaitForExit(15000)) { $process.Kill() }
    }
  }
  $last = $shots | Select-Object -Last 1
  $passed = $running -and $last -and -not $last.Blank
  $detail = if (-not $running) { "QEMU exited early (exit code $($process.ExitCode)); see $stderr" } elseif (-not $last) { 'No screenshot could be captured.' } elseif ($last.Blank) { 'The VM is running but the display is blank.' } else { "VM running after $BootMinutes min; last frame shows output." }
  return [pscustomobject]@{ hypervisor = 'QEMU'; accelerator = $accelerator; passed = [bool]$passed; detail = $detail; screenshots = @($shots | ForEach-Object { Split-Path -Leaf $_.Path }) }
}

function Invoke-StormHyperVBootTest {
  <#
    Boots the ISO in a temporary Generation 2 Hyper-V VM with Secure Boot (Microsoft Windows template) and a
    virtual TPM, so Windows 11 requirements are met without any bypass. Removes the VM afterwards.
  #>
  param(
    [Parameter(Mandatory)] [string] $IsoPath,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [int] $BootMinutes = 5,
    [int] $IntervalSeconds = 60,
    [string] $Phase = '14'
  )
  Assert-StormWindows -Operation 'Hyper-V tests'
  if (-not (Get-Module -ListAvailable -Name Hyper-V)) { throw (New-StormError -Code VmTest -Message 'The Hyper-V PowerShell module is not available.' -Fix 'Enable Hyper-V (Windows Pro/Enterprise): Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All') }
  Import-Module Hyper-V
  New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
  $name = 'StormOS-BootTest-' + (Get-Date -Format 'yyyyMMddHHmmss')
  $vmPath = Join-Path $OutputDirectory $name
  $shots = New-Object System.Collections.Generic.List[object]
  $running = $false
  try {
    New-VM -Name $name -Generation 2 -MemoryStartupBytes 4GB -NoVHD -Path $vmPath | Out-Null
    Set-VMProcessor -VMName $name -Count 2
    Set-VMFirmware -VMName $name -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows
    Set-VMKeyProtector -VMName $name -NewLocalKeyProtector
    Enable-VMTPM -VMName $name
    Add-VMDvdDrive -VMName $name -Path $IsoPath
    Set-VMFirmware -VMName $name -FirstBootDevice (Get-VMDvdDrive -VMName $name)
    Start-VM -Name $name
    $system = Get-CimInstance -Namespace 'root\virtualization\v2' -ClassName Msvm_ComputerSystem -Filter "ElementName='$name'"
    $keyboard = Get-CimAssociatedInstance -InputObject $system -ResultClassName Msvm_Keyboard
    for ($i = 0; $i -lt 10; $i++) { Invoke-CimMethod -InputObject $keyboard -MethodName TypeKey -Arguments @{ keyCode = [uint32]13 } | Out-Null; Start-Sleep -Seconds 1 }
    $service = Get-CimInstance -Namespace 'root\virtualization\v2' -ClassName Msvm_VirtualSystemManagementService
    $settings = Get-CimAssociatedInstance -InputObject $system -ResultClassName Msvm_VirtualSystemSettingData | Where-Object { $_.VirtualSystemType -eq 'Microsoft:Hyper-V:System:Realized' }
    $deadline = (Get-Date).AddMinutes($BootMinutes)
    $index = 0
    while ((Get-Date) -lt $deadline) {
      Start-Sleep -Seconds $IntervalSeconds
      $index++
      $thumbnail = Invoke-CimMethod -InputObject $service -MethodName GetVirtualSystemThumbnailImage -Arguments @{ TargetSystem = $settings; WidthPixels = [uint16]1024; HeightPixels = [uint16]768 }
      if ($thumbnail.ImageData) {
        $rgb = ConvertFrom-StormRgb565 -Bytes $thumbnail.ImageData -Width 1024 -Height 768
        $path = Join-Path $OutputDirectory ('vm-boot-{0:D2}.png' -f $index)
        [void](Save-StormRgbPng -Rgb $rgb -Width 1024 -Height 768 -Path $path)
        $shots.Add([pscustomobject]@{ Path = $path; Blank = (Test-StormImageBlank -Rgb $rgb -Width 1024 -Height 768) })
      }
    }
    $running = (Get-VM -Name $name).State -eq 'Running'
  }
  finally {
    if (Get-VM -Name $name -ErrorAction SilentlyContinue) {
      Stop-VM -Name $name -TurnOff -Force -ErrorAction SilentlyContinue
      Remove-VM -Name $name -Force
    }
    if (Test-Path -LiteralPath $vmPath) { Remove-Item -LiteralPath $vmPath -Recurse -Force -ErrorAction SilentlyContinue }
  }
  $last = $shots | Select-Object -Last 1
  $passed = $running -and $last -and -not $last.Blank
  $detail = if (-not $running) { 'The VM stopped before the end of the test.' } elseif (-not $last) { 'No thumbnail could be captured.' } elseif ($last.Blank) { 'The VM is running but the display is blank.' } else { "VM running after $BootMinutes min; last frame shows output." }
  return [pscustomobject]@{ hypervisor = 'Hyper-V'; accelerator = 'hyper-v'; passed = [bool]$passed; detail = $detail; screenshots = @($shots | ForEach-Object { Split-Path -Leaf $_.Path }) }
}
