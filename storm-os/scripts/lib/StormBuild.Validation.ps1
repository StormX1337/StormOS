# Build validation. BUILD SUCCESS is only reported when every critical check passed.

function New-StormValidation {
  param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [bool] $Passed, [Parameter(Mandatory)] [string] $Detail, [bool] $Critical = $true)
  return [pscustomobject]@{ name = $Name; status = $(if ($Passed) { 'PASS' } else { 'FAIL' }); detail = $Detail; critical = $Critical }
}

function Get-StormValidationSummary {
  param([Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Checks)
  if (-not $Checks -or $Checks.Count -eq 0) { return 'FAILED' }
  if ($Checks | Where-Object { $_.critical -and $_.status -ne 'PASS' }) { return 'FAILED' }
  return 'SUCCESS'
}

function Get-StormExpectedImageFiles {
  <# Files every STORM OS install image must contain; Storm app provisioning is expected only when enabled. #>
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $Branding, [bool] $StormApps)
  $files = @()
  foreach ($asset in @($Branding.assets)) { $files += [string]$asset.target }
  $files += 'Windows\Resources\Themes\StormOS.theme'
  $files += 'Windows\System32\oobe\info\oobe.xml'
  $files += 'Windows\Panther\unattend.xml'
  $files += 'ProgramData\StormOS\Config\storm-image.json'
  if ($StormApps) {
    $files += 'Windows\Setup\Scripts\SetupComplete.cmd'
    $files += 'Windows\Setup\Scripts\StormOS\StormOS.msi'
  }
  return $files
}

function Test-StormIsoArtifacts {
  <# ISO-level checks that need no mounting: existence, hash, boot catalog (BIOS + UEFI) and label. #>
  param([Parameter(Mandatory)] [string] $IsoPath, [Parameter(Mandatory)] [string] $ExpectedLabel)
  $checks = New-Object System.Collections.Generic.List[object]
  if (-not (Test-Path -LiteralPath $IsoPath)) {
    $checks.Add((New-StormValidation -Name 'ISO exists' -Passed $false -Detail "Not found: $IsoPath"))
    return $checks.ToArray()
  }
  $checks.Add((New-StormValidation -Name 'ISO exists' -Passed $true -Detail "$IsoPath ($(Format-StormBytes (Get-Item -LiteralPath $IsoPath).Length))"))
  $hashFile = "$IsoPath.sha256"
  if (Test-Path -LiteralPath $hashFile) {
    $recorded = ((Get-Content -LiteralPath $hashFile -Raw).Trim() -split '\s+')[0]
    $actual = Get-StormSha256 -Path $IsoPath
    $checks.Add((New-StormValidation -Name 'ISO hash' -Passed ($recorded -eq $actual) -Detail "SHA-256 $actual$(if ($recorded -ne $actual) { " (hash file says $recorded)" })"))
  }
  else {
    $checks.Add((New-StormValidation -Name 'ISO hash' -Passed $false -Detail "Missing $hashFile"))
  }
  $boot = Get-StormIsoBootInfo -Path $IsoPath
  $platforms = @($boot.Platforms)
  $checks.Add((New-StormValidation -Name 'Installer bootable (BIOS)' -Passed ($boot.HasBootRecord -and $platforms -contains 0) -Detail "El Torito platforms: $(($platforms | ForEach-Object { '0x{0:X2}' -f $_ }) -join ', ')"))
  $checks.Add((New-StormValidation -Name 'Installer bootable (UEFI)' -Passed ($boot.HasBootRecord -and $platforms -contains 0xEF) -Detail "Bootable entries: $($boot.BootableEntries)"))
  $checks.Add((New-StormValidation -Name 'ISO volume label' -Passed ($boot.Label -eq $ExpectedLabel) -Detail "Label '$($boot.Label)'" -Critical $false))
  return $checks.ToArray()
}

function Test-StormMediaUnattended {
  <#
    The installer media must not carry an answer file that Windows Setup picks up automatically (autounattend.xml at
    the root or in sources): Setup stays interactive, so the user chooses the disk. Test answer files live only on
    separate VM media (tests/vm/install).
  #>
  param([Parameter(Mandatory)] [string] $MediaRoot)
  $found = @(foreach ($relative in 'autounattend.xml', 'sources\autounattend.xml', 'unattend.xml', 'sources\unattend.xml') {
      if (Test-Path -LiteralPath (Join-Path $MediaRoot $relative)) { $relative }
    })
  return New-StormValidation -Name 'No automatic answer file on media' -Passed ($found.Count -eq 0) -Detail $(if ($found.Count) { "Found: $($found -join ', ')" } else { 'Windows Setup stays interactive' })
}

function Test-StormMediaImages {
  <# WIM-level checks on the installer media folder: install.wim (single Storm edition) and boot.wim. #>
  param([Parameter(Mandatory)] [string] $MediaRoot, [Parameter(Mandatory)] [System.Collections.IDictionary] $Edition)
  $checks = New-Object System.Collections.Generic.List[object]
  $checks.Add((Test-StormMediaUnattended -MediaRoot $MediaRoot))
  $missing = Test-StormMediaLayout -MediaRoot $MediaRoot
  $checks.Add((New-StormValidation -Name 'Installer media layout' -Passed ($missing.Count -eq 0) -Detail $(if ($missing.Count) { "Missing: $($missing -join ', ')" } else { 'boot, efi, bootmgr, setup and boot.wim present' })))
  $install = Join-Path $MediaRoot 'sources\install.wim'
  try {
    $images = @(Get-WindowsImage -ImagePath $install)
    $detail = Get-WindowsImage -ImagePath $install -Index 1
    $arch = ConvertTo-StormArchitecture $detail.Architecture
    $ok = $images.Count -eq 1 -and $arch -eq $Edition.architecture -and $detail.ImageName -eq $Edition.name
    $checks.Add((New-StormValidation -Name 'install.wim valid' -Passed $ok -Detail "$($images.Count) image(s): $($detail.ImageName) $arch $($detail.Version)"))
  }
  catch {
    $checks.Add((New-StormValidation -Name 'install.wim valid' -Passed $false -Detail $_.Exception.Message))
  }
  try {
    $bootImages = @(Get-WindowsImage -ImagePath (Join-Path $MediaRoot 'sources\boot.wim'))
    $checks.Add((New-StormValidation -Name 'boot.wim valid' -Passed ($bootImages.Count -ge 2) -Detail (($bootImages | ForEach-Object { "$($_.ImageIndex): $($_.ImageName)" }) -join '; ')))
  }
  catch {
    $checks.Add((New-StormValidation -Name 'boot.wim valid' -Passed $false -Detail $_.Exception.Message))
  }
  $esd = Join-Path $MediaRoot 'sources\install.esd'
  $checks.Add((New-StormValidation -Name 'Single install image' -Passed (-not (Test-Path -LiteralPath $esd)) -Detail $(if (Test-Path -LiteralPath $esd) { 'install.esd still present next to install.wim' } else { 'Only sources\install.wim' })))
  return $checks.ToArray()
}

function Test-StormImageContents {
  <# Mounts install.wim read-only and checks that Storm branding, configuration and the answer file are present and safe. #>
  param(
    [Parameter(Mandatory)] [string] $WimPath,
    [Parameter(Mandatory)] [string] $MountPath,
    [Parameter(Mandatory)] [string[]] $ExpectedFiles,
    [string] $LogPath,
    [string] $Phase = '13'
  )
  $checks = New-Object System.Collections.Generic.List[object]
  Mount-StormImage -ImagePath $WimPath -Index 1 -MountPath $MountPath -LogPath $LogPath -ReadOnly -Phase $Phase
  try {
    $missing = @($ExpectedFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $MountPath $_)) })
    $checks.Add((New-StormValidation -Name 'Storm files in image' -Passed ($missing.Count -eq 0) -Detail $(if ($missing.Count) { "Missing: $($missing -join ', ')" } else { "$($ExpectedFiles.Count) files present" })))
    $unattendPath = Join-Path $MountPath 'Windows\Panther\unattend.xml'
    if (Test-Path -LiteralPath $unattendPath) {
      $violations = Test-StormUnattendSafety -XmlText (Get-Content -LiteralPath $unattendPath -Raw)
      $checks.Add((New-StormValidation -Name 'Answer file safe' -Passed ($violations.Count -eq 0) -Detail $(if ($violations.Count) { $violations -join '; ' } else { 'No disk, key, account or bypass settings' })))
    }
    $storm = Join-Path $MountPath 'ProgramData\StormOS\Config\storm-image.json'
    if (Test-Path -LiteralPath $storm) {
      $info = Read-StormJson -Path $storm
      $checks.Add((New-StormValidation -Name 'Storm image identity' -Passed ([bool]$info.stormVersion) -Detail "STORM OS $($info.stormVersion), build $($info.buildId)"))
    }
    $missingWindows = Test-StormMountedWindows -MountPath $MountPath
    $checks.Add((New-StormValidation -Name 'Windows image intact' -Passed ($missingWindows.Count -eq 0) -Detail $(if ($missingWindows.Count) { "Missing: $($missingWindows -join ', ')" } else { 'Hives, default profile and shell present' })))
  }
  finally {
    Dismount-StormImage -MountPath $MountPath -LogPath $LogPath -Phase $Phase
  }
  return $checks.ToArray()
}
