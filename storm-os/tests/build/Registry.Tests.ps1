BeforeAll {
  Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\StormBuild.psm1') -Force
  $script:Config = Join-Path $PSScriptRoot '..\..\config'
  function New-TestSetting([hashtable] $Override = @{}) {
    $setting = [ordered]@{ id = 'test.value'; name = 'n'; description = 'd'; reason = 'r'; category = 'SYSTEM'; risk = 'Low'; hive = 'DefaultUser'; key = 'Software\Storm\Test'; valueName = 'Value'; type = 'REG_DWORD'; value = 1; editions = @('*'); userChange = 'Settings' }
    foreach ($key in $Override.Keys) { $setting[$key] = $Override[$key] }
    return $setting
  }
}

Describe 'Registry defaults configuration' {
  It 'desktop defaults are valid, documented and low risk' {
    $settings = Import-StormRegistryConfig -Path (Join-Path $Config 'defaults\desktop-defaults.json')
    $settings.Count | Should -BeGreaterThan 0
    foreach ($setting in $settings) { $setting.risk | Should -BeIn @('None', 'Low') }
  }

  It 'gaming defaults are valid, documented and low risk' {
    $settings = Import-StormRegistryConfig -Path (Join-Path $Config 'gaming\gaming-defaults.json')
    $settings.Count | Should -BeGreaterThan 0
    foreach ($setting in $settings) { $setting.userChange | Should -Not -BeNullOrEmpty }
  }

  It 'branding registry values are valid' {
    $branding = Read-StormJson -Path (Join-Path $Config 'branding\branding.json')
    foreach ($setting in (Get-StormBrandingRegistrySettings -Branding $branding -EditionName 'Windows 11 Pro')) {
      Test-StormRegistrySetting -Setting $setting | Should -BeNullOrEmpty
    }
  }

  It 'describes the edition honestly in the OEM model' {
    $branding = Read-StormJson -Path (Join-Path $Config 'branding\branding.json')
    $model = (Get-StormBrandingRegistrySettings -Branding $branding -EditionName 'Windows 11 Pro' | Where-Object { $_.valueName -eq 'Model' }).value
    $model | Should -Be 'Storm customization of Windows 11 Pro'
  }

  It 'does not enforce the lock screen unless configured' {
    $branding = Read-StormJson -Path (Join-Path $Config 'branding\branding.json')
    Get-StormBrandingRegistrySettings -Branding $branding -EditionName 'Windows 11 Pro' | Where-Object { $_.id -eq 'branding.lock-screen' } | Should -BeNullOrEmpty
  }
}

Describe 'Registry deny list' {
  It 'blocks <Name>' -TestCases @(
    @{ Name = 'Defender'; Key = 'Policies\Microsoft\Windows Defender'; Value = 'DisableAntiSpyware' }
    @{ Name = 'UAC'; Key = 'Microsoft\Windows\CurrentVersion\Policies\System'; Value = 'EnableLUA' }
    @{ Name = 'Windows Update'; Key = 'Policies\Microsoft\Windows\WindowsUpdate\AU'; Value = 'NoAutoUpdate' }
    @{ Name = 'TPM bypass'; Key = 'Setup\LabConfig'; Value = 'BypassTPMCheck' }
    @{ Name = 'upgrade bypass'; Key = 'Setup\MoSetup'; Value = 'AllowUpgradesWithUnsupportedTPMOrCPU' }
    @{ Name = 'OOBE network bypass'; Key = 'Microsoft\Windows\CurrentVersion\OOBE'; Value = 'BypassNRO' }
    @{ Name = 'activation'; Key = 'Microsoft\Windows NT\CurrentVersion\SoftwareProtectionPlatform'; Value = 'x' }
    @{ Name = 'firewall'; Key = 'Policies\Microsoft\WindowsFirewall\DomainProfile'; Value = 'EnableFirewall' }
    @{ Name = 'SmartScreen'; Key = 'Policies\Microsoft\Windows\System'; Value = 'EnableSmartScreen' }
    @{ Name = 'auto logon'; Key = 'Microsoft\Windows NT\CurrentVersion\Winlogon'; Value = 'AutoAdminLogon' }
    @{ Name = 'services'; Key = 'ControlSet001\Services\SysMain'; Value = 'Start' }
    @{ Name = 'code integrity'; Key = 'ControlSet001\Control\DeviceGuard'; Value = 'EnableVirtualizationBasedSecurity' }
  ) {
    param($Name, $Key, $Value)
    $errors = Test-StormRegistrySetting -Setting (New-TestSetting @{ key = $Key; valueName = $Value })
    ($errors -join ' ') | Should -Match 'protected'
  }

  It 'allows ordinary user preferences' {
    Test-StormRegistrySetting -Setting (New-TestSetting @{ key = 'Software\Microsoft\GameBar'; valueName = 'AutoGameModeEnabled' }) | Should -BeNullOrEmpty
  }
}

Describe 'Registry setting validation' {
  It 'rejects <Case>' -TestCases @(
    @{ Case = 'medium risk'; Override = @{ risk = 'Medium' }; Pattern = 'risk' }
    @{ Case = 'absolute keys'; Override = @{ key = 'HKLM\SOFTWARE\Storm' }; Pattern = 'relative' }
    @{ Case = 'the SYSTEM hive'; Override = @{ hive = 'System' }; Pattern = 'hive' }
    @{ Case = 'binary values'; Override = @{ type = 'REG_BINARY' }; Pattern = 'type' }
    @{ Case = 'out-of-range DWORDs'; Override = @{ value = -1 }; Pattern = 'REG_DWORD' }
    @{ Case = 'unknown editions'; Override = @{ editions = @('Ultimate') }; Pattern = 'edition' }
    @{ Case = 'bad ids'; Override = @{ id = 'Bad Id' }; Pattern = 'lowercase' }
    @{ Case = 'missing reasons'; Override = @{ reason = '' }; Pattern = 'reason' }
  ) {
    param($Case, $Override, $Pattern)
    (Test-StormRegistrySetting -Setting (New-TestSetting $Override)) -join ' ' | Should -Match $Pattern
  }

  It 'applies settings by edition family' {
    Test-StormSettingApplies -Setting (New-TestSetting @{ editions = @('Enterprise', 'Education') }) -EditionFamily 'Pro' | Should -BeFalse
    Test-StormSettingApplies -Setting (New-TestSetting @{ editions = @('Enterprise', 'Education') }) -EditionFamily 'Enterprise' | Should -BeTrue
    Test-StormSettingApplies -Setting (New-TestSetting) -EditionFamily 'Home' | Should -BeTrue
  }

  It 'builds reg.exe arguments as an array' {
    $arguments = New-StormRegAddArguments -Setting (New-TestSetting @{ value = 42 }) -HiveRoot 'HKLM\STORM_BUILD_DEFAULTUSER'
    $arguments | Should -Be @('add', 'HKLM\STORM_BUILD_DEFAULTUSER\Software\Storm\Test', '/v', 'Value', '/t', 'REG_DWORD', '/d', '42', '/f')
  }
}

Describe 'reg query parsing' {
  It 'parses DWORDs as decimal' {
    $lines = @('', 'HKEY_LOCAL_MACHINE\STORM_BUILD_DEFAULTUSER\Software\Microsoft\GameBar', '    AutoGameModeEnabled    REG_DWORD    0x1', '')
    $value = ConvertFrom-StormRegQuery -Lines $lines -ValueName 'AutoGameModeEnabled'
    $value.Type | Should -Be 'REG_DWORD'
    $value.Data | Should -Be '1'
  }
  It 'parses strings with spaces and names with spaces' {
    $lines = @('    My Value    REG_SZ    Storm customization of Windows 11 Pro')
    (ConvertFrom-StormRegQuery -Lines $lines -ValueName 'My Value').Data | Should -Be 'Storm customization of Windows 11 Pro'
  }
  It 'returns null for other values' {
    ConvertFrom-StormRegQuery -Lines @('    Other    REG_SZ    x') -ValueName 'Value' | Should -BeNullOrEmpty
  }
}
