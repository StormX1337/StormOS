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
  It 'treats a nearly empty frame (QEMU "display not initialized" text) as blank' {
    $width = 640; $height = 480
    $text = New-Object byte[] ($width * $height * 3)
    for ($y = 224; $y -lt 240; $y++) { for ($x = 144; $x -lt 496; $x += 4) { $o = ($y * $width + $x) * 3; $text[$o] = 192; $text[$o + 1] = 192; $text[$o + 2] = 192 } }
    Test-StormImageBlank -Rgb $text -Width $width -Height $height | Should -BeTrue
    $dialog = New-Object byte[] ($width * $height * 3)
    for ($y = 120; $y -lt 360; $y++) { for ($x = 160; $x -lt 480; $x++) { $o = ($y * $width + $x) * 3; $dialog[$o] = 230; $dialog[$o + 1] = 230; $dialog[$o + 2] = 230 } }
    Test-StormImageBlank -Rgb $dialog -Width $width -Height $height | Should -BeFalse
  }
  It 'converts Hyper-V RGB565 thumbnails' {
    $rgb = ConvertFrom-StormRgb565 -Bytes ([byte[]](0x00, 0xF8, 0xE0, 0x07)) -Width 2 -Height 1
    $rgb[0] | Should -Be 248   # pure red
    $rgb[4] | Should -Be 252   # pure green
  }
}

Describe 'Display stability' {
  It 'needs three identical, non-blank frames' {
    $frame = { param($f, $blank) [pscustomobject]@{ Fingerprint = $f; Blank = $blank } }
    Test-StormDisplayStable -Shots @((& $frame 'a' $false), (& $frame 'a' $false)) | Should -BeFalse
    Test-StormDisplayStable -Shots @((& $frame 'x' $false), (& $frame 'a' $false), (& $frame 'a' $false), (& $frame 'a' $false)) | Should -BeTrue
    Test-StormDisplayStable -Shots @((& $frame 'a' $false), (& $frame 'b' $false), (& $frame 'a' $false)) | Should -BeFalse
    Test-StormDisplayStable -Shots @((& $frame 'a' $true), (& $frame 'a' $true), (& $frame 'a' $true)) | Should -BeFalse
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

Describe 'PNG encoder' {
  It 'writes a valid PNG that round-trips the pixels' {
    $rgb = [byte[]](255, 0, 0, 0, 255, 0, 0, 0, 255, 10, 20, 30)
    $png = ConvertTo-StormPng -Rgb $rgb -Width 2 -Height 2
    $png[0..7] | Should -Be @(137, 80, 78, 71, 13, 10, 26, 10)
    [Text.Encoding]::ASCII.GetString($png, 12, 4) | Should -Be 'IHDR'
    ($png[16] * 16777216 + $png[17] * 65536 + $png[18] * 256 + $png[19]) | Should -Be 2
    $png[24] | Should -Be 8
    $png[25] | Should -Be 2
    $expectedCrc = [StormPngEncoder]::Crc32($png, 12, 17)
    $crc = [uint32]$png[29] * 16777216 + [uint32]$png[30] * 65536 + [uint32]$png[31] * 256 + [uint32]$png[32]
    $crc | Should -Be $expectedCrc
    $length = $png[33] * 16777216 + $png[34] * 65536 + $png[35] * 256 + $png[36]
    [Text.Encoding]::ASCII.GetString($png, 37, 4) | Should -Be 'IDAT'
    $zlib = New-Object IO.MemoryStream(, $png[43..(41 + $length - 4 - 1 + 0)])
    $inflate = New-Object IO.Compression.DeflateStream($zlib, [IO.Compression.CompressionMode]::Decompress)
    $out = New-Object IO.MemoryStream
    $inflate.CopyTo($out)
    $out.ToArray() | Should -Be ([byte[]](0, 255, 0, 0, 0, 255, 0, 0, 0, 0, 255, 10, 20, 30))
  }
  It 'knows the standard CRC-32 check value' {
    [StormPngEncoder]::Crc32([Text.Encoding]::ASCII.GetBytes('123456789'), 0, 9) | Should -Be ([uint32]3421780262)  # 0xCBF43926
  }
  It 'rejects short pixel buffers' { { ConvertTo-StormPng -Rgb ([byte[]](1, 2, 3)) -Width 2 -Height 2 } | Should -Throw }
}
