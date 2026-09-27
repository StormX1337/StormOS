BeforeAll {
  Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\StormBuild.psm1') -Force
  $script:Editions = @(
    [pscustomobject]@{ Index = 1; Name = 'Windows 11 Home'; EditionId = 'Core'; Architecture = 'x64'; Version = '10.0.26100.1742'; Languages = @('en-US'); SizeBytes = 17GB }
    [pscustomobject]@{ Index = 6; Name = 'Windows 11 Pro'; EditionId = 'Professional'; Architecture = 'x64'; Version = '10.0.26100.1742'; Languages = @('en-US'); SizeBytes = 17GB }
    [pscustomobject]@{ Index = 7; Name = 'Windows 10 Pro'; EditionId = 'Professional'; Architecture = 'x64'; Version = '10.0.19045.3803'; Languages = @('en-US'); SizeBytes = 15GB }
    [pscustomobject]@{ Index = 8; Name = 'Windows 11 Pro'; EditionId = 'Professional'; Architecture = 'x86'; Version = '10.0.22631.1'; Languages = @('en-US'); SizeBytes = 15GB }
  )
}

Describe 'Source resolution' {
  It 'accepts an ISO' { (Resolve-StormSource -SourceIso 'C:\w.iso').Type | Should -Be 'ISO' }
  It 'accepts WIM and ESD with boot media' {
    (Resolve-StormSource -SourceWim 'C:\install.esd' -SourceMediaDirectory 'C:\media').Type | Should -Be 'ESD'
    (Resolve-StormSource -SourceWim 'C:\install.wim' -SourceMediaDirectory 'C:\media').Type | Should -Be 'WIM'
  }
  It 'requires boot media for WIM sources' { { Resolve-StormSource -SourceWim 'C:\install.wim' } | Should -Throw '*boot media*' }
  It 'rejects ambiguous or missing sources' {
    { Resolve-StormSource -SourceIso 'a.iso' -SourceWim 'b.wim' } | Should -Throw
    { Resolve-StormSource } | Should -Throw '*No source*'
    { Resolve-StormSource -SourceIso 'C:\w.img' } | Should -Throw
  }
}

Describe 'Edition selection' {
  It 'selects an existing Windows 11 x64 edition' { (Select-StormEdition -Editions $Editions -Index 6).Name | Should -Be 'Windows 11 Pro' }
  It 'rejects unknown indexes with the available list as fix' {
    try { Select-StormEdition -Editions $Editions -Index 42; throw 'expected failure' }
    catch { (Get-StormErrorInfo $_).Fix | Should -Match '6 = Windows 11 Pro' }
  }
  It 'rejects Windows 10 and x86 images' {
    { Select-StormEdition -Editions $Editions -Index 7 } | Should -Throw '*not Windows 11*'
    { Select-StormEdition -Editions $Editions -Index 8 } | Should -Throw '*architecture*'
  }
  It 'maps DISM architectures' {
    ConvertTo-StormArchitecture 9 | Should -Be 'x64'
    ConvertTo-StormArchitecture 12 | Should -Be 'arm64'
    ConvertTo-StormArchitecture 0 | Should -Be 'x86'
  }
  It 'classifies edition families' {
    Get-StormEditionFamily 'Windows 11 Pro' | Should -Be 'Pro'
    Get-StormEditionFamily 'Windows 11 Pro for Workstations' | Should -Be 'Pro'
    Get-StormEditionFamily 'Windows 11 Pro Education' | Should -Be 'Education'
    Get-StormEditionFamily 'Windows 11 Enterprise Evaluation' | Should -Be 'Enterprise'
    Get-StormEditionFamily 'Windows 11 Home' | Should -Be 'Home'
  }
  It 'prints a table with one row per edition' { (Format-StormEditionTable -Editions $Editions).Count | Should -Be 5 }
}

Describe 'Media layout' {
  It 'lists missing boot files' {
    $missing = Test-StormMediaLayout -MediaRoot $TestDrive
    $missing | Should -Contain 'efi\microsoft\boot\efisys.bin'
    $missing | Should -Contain 'sources\boot.wim'
  }
  It 'accepts a complete layout' {
    foreach ($file in 'boot\etfsboot.com', 'efi\microsoft\boot\efisys.bin', 'bootmgr', 'bootmgr.efi', 'sources\boot.wim', 'setup.exe') {
      $path = Join-Path $TestDrive ($file -replace '\\', [IO.Path]::DirectorySeparatorChar)
      New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
      Set-Content -Path $path -Value 'x'
    }
    (Test-StormMediaLayout -MediaRoot $TestDrive).Count | Should -Be 0
  }
}
