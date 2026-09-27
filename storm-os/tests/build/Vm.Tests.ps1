BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\StormBuild.psm1') -Force }

Describe 'Screenshot helpers' {
  It 'parses QEMU PPM screendumps' {
    $header = [Text.Encoding]::ASCII.GetBytes("P6`n# qemu`n2 1`n255`n")
    $bytes = $header + [byte[]](255, 0, 0, 0, 0, 255)
    $image = ConvertFrom-StormPpm -Bytes $bytes
    $image.Width | Should -Be 2
    $image.Height | Should -Be 1
    $image.Rgb | Should -Be ([byte[]](255, 0, 0, 0, 0, 255))
  }
  It 'rejects other formats' { { ConvertFrom-StormPpm -Bytes ([Text.Encoding]::ASCII.GetBytes("P3`n1 1`n255`n0 0 0")) } | Should -Throw }
  It 'detects blank frames' {
    Test-StormImageBlank -Rgb (New-Object byte[] (100 * 100 * 3)) -Width 100 -Height 100 | Should -BeTrue
    $rgb = New-Object byte[] (100 * 100 * 3)
    for ($i = 0; $i -lt $rgb.Length; $i += 7) { $rgb[$i] = 250 }
    Test-StormImageBlank -Rgb $rgb -Width 100 -Height 100 | Should -BeFalse
  }
  It 'converts Hyper-V RGB565 thumbnails' {
    $rgb = ConvertFrom-StormRgb565 -Bytes ([byte[]](0x00, 0xF8, 0xE0, 0x07)) -Width 2 -Height 1
    $rgb[0] | Should -Be 248   # pure red
    $rgb[4] | Should -Be 252   # pure green
  }
}

Describe 'QEMU arguments' {
  It 'boots the ISO with UEFI flash, a monitor and no network' {
    $arguments = New-StormQemuArguments -IsoPath '/tmp/StormOS.iso' -FirmwareCode '/fw/code.fd' -FirmwareVars '/fw/vars.fd' -MonitorPort 45555 -Accelerator kvm
    $joined = $arguments -join ' '
    $joined | Should -Match 'if=pflash,format=raw,unit=0,readonly=on,file=/fw/code.fd'
    $joined | Should -Match 'file=/tmp/StormOS.iso,media=cdrom'
    $joined | Should -Match 'tcp:127.0.0.1:45555,server,nowait'
    $joined | Should -Match '-nic none'
    $joined | Should -Match '-accel kvm'
  }
  It 'falls back to -bios for combined firmware and multi-threaded TCG' {
    $joined = (New-StormQemuArguments -IsoPath 'a.iso' -FirmwareCode 'OVMF.fd' -MonitorPort 1 -Accelerator tcg) -join ' '
    $joined | Should -Match '-bios OVMF.fd'
    $joined | Should -Match 'tcg,thread=multi'
  }
}
