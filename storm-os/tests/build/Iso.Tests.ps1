BeforeAll {
  Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\StormBuild.psm1') -Force

  # Builds a minimal ISO 9660 image: PVD (sector 16), El Torito boot record (17), terminator (18), boot catalog (19).
  function New-TestIso([int[]] $SectionPlatforms = @(0xEF), [string] $Label = 'STORMOS_1_0_0', [switch] $NoBootRecord) {
    $bytes = New-Object byte[] (24 * 2048)
    $pvd = 16 * 2048
    $bytes[$pvd] = 1
    [Text.Encoding]::ASCII.GetBytes('CD001').CopyTo($bytes, $pvd + 1)
    $bytes[$pvd + 6] = 1
    [Text.Encoding]::ASCII.GetBytes($Label.PadRight(32)).CopyTo($bytes, $pvd + 40)
    if (-not $NoBootRecord) {
      $br = 17 * 2048
      $bytes[$br] = 0
      [Text.Encoding]::ASCII.GetBytes('CD001').CopyTo($bytes, $br + 1)
      $bytes[$br + 6] = 1
      [Text.Encoding]::ASCII.GetBytes('EL TORITO SPECIFICATION').CopyTo($bytes, $br + 7)
      [BitConverter]::GetBytes([uint32]19).CopyTo($bytes, $br + 0x47)
      $catalog = 19 * 2048
      $bytes[$catalog] = 1        # validation entry
      $bytes[$catalog + 1] = 0    # platform: x86 BIOS
      $bytes[$catalog + 30] = 0x55
      $bytes[$catalog + 31] = 0xAA
      $bytes[$catalog + 32] = 0x88 # default entry bootable
      $offset = $catalog + 64
      for ($i = 0; $i -lt $SectionPlatforms.Count; $i++) {
        $bytes[$offset] = if ($i -eq $SectionPlatforms.Count - 1) { 0x91 } else { 0x90 }
        $bytes[$offset + 1] = [byte]$SectionPlatforms[$i]
        [BitConverter]::GetBytes([uint16]1).CopyTo($bytes, $offset + 2)
        $bytes[$offset + 32] = 0x88
        $offset += 64
      }
    }
    $terminator = 18 * 2048
    $bytes[$terminator] = 255
    [Text.Encoding]::ASCII.GetBytes('CD001').CopyTo($bytes, $terminator + 1)
    return , $bytes
  }
}

Describe 'ISO arguments' {
  It 'builds a dual BIOS/UEFI UDF image' {
    $arguments = New-StormOscdimgArguments -MediaRoot 'C:\storm-os\build\iso' -IsoPath 'C:\out\StormOS.iso' -Label 'STORMOS_1_0_0'
    $arguments | Should -Contain '-u2'
    $arguments | Should -Contain '-udfver102'
    $arguments | Should -Contain '-lSTORMOS_1_0_0'
    ($arguments | Where-Object { $_ -like '-bootdata:2#p0,e,b*etfsboot.com#pEF,e,b*efisys.bin' }) | Should -Not -BeNullOrEmpty
    $arguments[-2] | Should -Be 'C:\storm-os\build\iso'
    $arguments[-1] | Should -Be 'C:\out\StormOS.iso'
  }
  It 'uses the no-prompt UEFI loader only on request' {
    (New-StormOscdimgArguments -MediaRoot 'M' -IsoPath 'I' -Label 'L' -NoBootPrompt) -join ' ' | Should -Match 'efisys_noprompt\.bin'
    (New-StormOscdimgArguments -MediaRoot 'M' -IsoPath 'I' -Label 'L') -join ' ' | Should -Not -Match 'noprompt'
  }
  It 'validates labels' {
    Test-StormIsoLabel 'STORMOS_1_0_0' | Should -BeTrue
    Test-StormIsoLabel 'storm os' | Should -BeFalse
    Test-StormIsoLabel ('A' * 33) | Should -BeFalse
    New-StormIsoLabel -BuildVersion '1.0.0-beta.1' | Should -Be 'STORMOS_1_0_0_BETA_1'
    { New-StormOscdimgArguments -MediaRoot 'M' -IsoPath 'I' -Label 'bad label' } | Should -Throw
  }
}

Describe 'El Torito verification' {
  It 'finds BIOS and UEFI boot entries' {
    $stream = New-Object IO.MemoryStream(, (New-TestIso))
    $info = Get-StormElToritoInfo -Stream $stream
    $info.HasBootRecord | Should -BeTrue
    $info.Platforms | Should -Contain 0
    $info.Platforms | Should -Contain 0xEF
    $info.BootableEntries | Should -Be 2
  }
  It 'reports a BIOS-only image' {
    $info = Get-StormElToritoInfo -Stream (New-Object IO.MemoryStream(, (New-TestIso -SectionPlatforms @())))
    $info.Platforms | Should -Not -Contain 0xEF
  }
  It 'reports images without a boot record' {
    (Get-StormElToritoInfo -Stream (New-Object IO.MemoryStream(, (New-TestIso -NoBootRecord)))).HasBootRecord | Should -BeFalse
  }
  It 'reads the volume label' {
    Get-StormIsoVolumeLabel -Stream (New-Object IO.MemoryStream(, (New-TestIso -Label 'STORMOS_2_0_0'))) | Should -Be 'STORMOS_2_0_0'
  }
  It 'handles truncated files' {
    (Get-StormElToritoInfo -Stream (New-Object IO.MemoryStream(, (New-Object byte[] 100)))).HasBootRecord | Should -BeFalse
  }
  It 'validates a written ISO file end to end' {
    $path = Join-Path $TestDrive 'StormOS.iso'
    [IO.File]::WriteAllBytes($path, (New-TestIso))
    Write-StormHashFile -Path $path | Out-Null
    $checks = Test-StormIsoArtifacts -IsoPath $path -ExpectedLabel 'STORMOS_1_0_0'
    ($checks | Where-Object { $_.status -ne 'PASS' }) | Should -BeNullOrEmpty
    Get-StormValidationSummary -Checks $checks | Should -Be 'SUCCESS'
  }
  It 'detects a tampered ISO through its hash' {
    $path = Join-Path $TestDrive 'Tampered.iso'
    [IO.File]::WriteAllBytes($path, (New-TestIso))
    Write-StormHashFile -Path $path | Out-Null
    $stream = [IO.File]::OpenWrite($path); $stream.Seek(100, 'Begin') | Out-Null; $stream.WriteByte(7); $stream.Dispose()
    (Test-StormIsoArtifacts -IsoPath $path -ExpectedLabel 'STORMOS_1_0_0' | Where-Object { $_.name -eq 'ISO hash' }).status | Should -Be 'FAIL'
  }
}
