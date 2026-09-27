BeforeAll {
  Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\StormBuild.psm1') -Force
  $script:BrandingRoot = Join-Path $PSScriptRoot '..\..\branding'
  $script:Branding = Read-StormJson -Path (Join-Path $PSScriptRoot '..\..\config\branding\branding.json')
}

Describe 'Branding assets' {
  It 'every configured asset exists: <_>' -ForEach @(
    'wallpapers/storm-dark.jpg', 'wallpapers/storm-light.jpg', 'wallpapers/storm-lockscreen.jpg',
    'logos/StormOS-oem-logo.bmp', 'logos/StormOS-oobe-logo.png', 'logos/storm-mark.png', 'logos/storm-logo.png', 'icons/StormOS.ico'
  ) {
    (Get-Item -LiteralPath (Join-Path $BrandingRoot $_)).Length | Should -BeGreaterThan 1000
  }

  It 'configures only assets that exist' {
    foreach ($asset in $Branding.assets) { Test-Path -LiteralPath (Join-Path $BrandingRoot $asset.source) | Should -BeTrue }
  }

  It 'ships the OEM logo as a 120x120 24-bit BMP' {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $BrandingRoot 'logos/StormOS-oem-logo.bmp'))
    [Text.Encoding]::ASCII.GetString($bytes, 0, 2) | Should -Be 'BM'
    [BitConverter]::ToInt32($bytes, 18) | Should -Be 120
    [BitConverter]::ToInt32($bytes, 22) | Should -Be 120
    [BitConverter]::ToInt16($bytes, 28) | Should -Be 24
  }

  It 'ships 4K wallpapers' {
    foreach ($name in 'storm-dark.jpg', 'storm-light.jpg', 'storm-lockscreen.jpg') {
      $bytes = [IO.File]::ReadAllBytes((Join-Path $BrandingRoot "wallpapers/$name"))
      $bytes[0] | Should -Be 0xFF
      $bytes[1] | Should -Be 0xD8
    }
  }
}

Describe 'Theme' {
  It 'uses the Storm wallpaper, dark mode and accent color' {
    $theme = New-StormThemeContent -Branding $Branding
    $theme | Should -Match 'Wallpaper=%SystemRoot%\\Web\\Wallpaper\\StormOS\\storm-dark\.jpg'
    $theme | Should -Match 'SystemMode=Dark'
    $theme | Should -Match 'ColorizationColor=0XFF3FC1FF'
    $theme | Should -Match 'DisplayName=STORM OS'
  }
  It 'converts colors' {
    ConvertTo-StormThemeColor '#0b84c6' | Should -Be '0XFF0B84C6'
    { ConvertTo-StormThemeColor 'blue' } | Should -Throw
  }
}

Describe 'OOBE and answer file' {
  It 'writes well-formed oobe.xml with the Storm name and logo' {
    [xml]$xml = New-StormOobeXml -Branding $Branding
    $xml.FirstExperience.oobe.oem.name | Should -Be 'STORM OS'
    $xml.FirstExperience.oobe.oem.logopath | Should -Match 'StormOS-oobe-logo\.png$'
  }

  It 'generates a safe answer file for <_>' -ForEach @('x64', 'arm64') {
    $xml = New-StormUnattendXml -Branding $Branding -Architecture $_
    Test-StormUnattendSafety -XmlText $xml | Should -BeNullOrEmpty
    ([xml]$xml).unattend.settings.pass | Should -Be 'oobeSystem'
  }

  It 'rejects unsafe answer files: <Case>' -TestCases @(
    @{ Case = 'windowsPE pass'; Fragment = '<settings pass="windowsPE"><component name="x"/></settings>' }
    @{ Case = 'disk wiping'; Fragment = '<settings pass="specialize"><component name="x"><DiskConfiguration><Disk><WillWipeDisk>true</WillWipeDisk></Disk></DiskConfiguration></component></settings>' }
    @{ Case = 'product keys'; Fragment = '<settings pass="specialize"><component name="x"><ProductKey>XXXXX</ProductKey></component></settings>' }
    @{ Case = 'auto logon'; Fragment = '<settings pass="oobeSystem"><component name="x"><AutoLogon><Enabled>true</Enabled></AutoLogon></component></settings>' }
    @{ Case = 'accounts'; Fragment = '<settings pass="oobeSystem"><component name="x"><UserAccounts/></component></settings>' }
    @{ Case = 'skipping OOBE'; Fragment = '<settings pass="oobeSystem"><component name="x"><OOBE><HideOnlineAccountScreens>true</HideOnlineAccountScreens></OOBE></component></settings>' }
    @{ Case = 'TPM bypass'; Fragment = '<settings pass="specialize"><component name="x"><Path>reg add HKLM\SYSTEM\Setup\LabConfig /v BypassTPMCheck</Path></component></settings>' }
    @{ Case = 'setup commands'; Fragment = '<settings pass="specialize"><component name="x"><RunSynchronous/></component></settings>' }
  ) {
    param($Case, $Fragment)
    $xml = "<?xml version=`"1.0`"?><unattend xmlns=`"urn:schemas-microsoft-com:unattend`">$Fragment</unattend>"
    Test-StormUnattendSafety -XmlText $xml | Should -Not -BeNullOrEmpty
  }

  It 'rejects malformed XML' { Test-StormUnattendSafety -XmlText '<unattend>' | Should -Not -BeNullOrEmpty }
}
