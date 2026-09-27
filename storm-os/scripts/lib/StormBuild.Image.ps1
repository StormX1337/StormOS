# Image servicing: export of the selected edition, safe mount/unmount with stale-mount handling, offline hives.

$script:StormHivePrefix = 'STORM_BUILD_'

function Get-StormMountedImages {
  Assert-StormWindows -Operation 'Inspecting mounted images'
  return @(Get-WindowsImage -Mounted)
}

function Test-StormPathEqual {
  param([string] $Left, [string] $Right)
  if (-not $Left -or -not $Right) { return $false }
  $normalize = { param($p) [System.IO.Path]::GetFullPath($p).TrimEnd('\', '/') }
  return [string]::Equals((& $normalize $Left), (& $normalize $Right), [System.StringComparison]::OrdinalIgnoreCase)
}

function Clear-StormStaleMount {
  <#
    Detects a previous mount at MountPath. A healthy leftover mount is discarded (never committed: its state is
    unknown); corrupt mounts ("NeedsRemount"/"Invalid") are discarded and the DISM mount registry is cleaned.
  #>
  param([Parameter(Mandatory)] [string] $MountPath, [string] $LogPath, [string] $Phase = '04')
  $existing = Get-StormMountedImages | Where-Object { Test-StormPathEqual $_.Path $MountPath }
  foreach ($mount in $existing) {
    Write-StormLog -Level Warn -Phase $Phase -Message "Found a previous mount at $MountPath (status $($mount.MountStatus), image $($mount.ImagePath)); discarding it."
    try {
      Dismount-WindowsImage -Path $MountPath -Discard -LogPath $LogPath | Out-Null
    }
    catch {
      Write-StormLog -Level Warn -Phase $Phase -Message "Discard failed ($($_.Exception.Message)); cleaning corrupt mount points."
    }
  }
  if ($existing -or (Get-StormMountedImages | Where-Object { $_.MountStatus -ne 'Ok' })) {
    Clear-WindowsCorruptMountPoint -LogPath $LogPath | Out-Null
  }
}

function Export-StormEdition {
  <#
    Exports the selected edition into a new single-edition WIM. This is also how an ESD (not mountable read-write)
    becomes a serviceable WIM. The source file is only read.
  #>
  param([Parameter(Mandatory)] [string] $SourceImage, [Parameter(Mandatory)] [int] $Index, [Parameter(Mandatory)] [string] $Destination, [string] $LogPath, [string] $Phase = '04')
  Assert-StormWindows -Operation 'Exporting an edition'
  if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
  Write-StormLog -Phase $Phase -Message "Exporting edition $Index from $(Split-Path -Leaf $SourceImage) to $Destination (this takes several minutes)..."
  try {
    Export-WindowsImage -SourceImagePath $SourceImage -SourceIndex $Index -DestinationImagePath $Destination -CompressionType Max -CheckIntegrity -LogPath $LogPath | Out-Null
  }
  catch {
    throw (New-StormError -Code Mount -Message "Exporting edition $Index failed: $($_.Exception.Message)" -Fix 'Check free space and the DISM log in build/logs/dism.log; verify the source ISO hash.' -InnerException $_.Exception)
  }
}

function Mount-StormImage {
  param([Parameter(Mandatory)] [string] $ImagePath, [int] $Index = 1, [Parameter(Mandatory)] [string] $MountPath, [string] $LogPath, [switch] $ReadOnly, [string] $Phase = '04')
  Assert-StormWindows -Operation 'Mounting an image'
  if (-not (Test-Path -LiteralPath $MountPath)) { New-Item -ItemType Directory -Path $MountPath -Force | Out-Null }
  Clear-StormStaleMount -MountPath $MountPath -LogPath $LogPath -Phase $Phase
  if (-not (Test-StormDirectoryEmpty -Path $MountPath)) {
    throw (New-StormError -Code Mount -Message "Mount directory is not empty: $MountPath" -Fix 'Run scripts/rollback.ps1; if files remain, delete the folder contents manually after checking that no image is mounted (Get-WindowsImage -Mounted).')
  }
  Write-StormLog -Phase $Phase -Message "Mounting $(Split-Path -Leaf $ImagePath) index $Index at $MountPath$(if ($ReadOnly) { ' (read-only)' })"
  try {
    if ($ReadOnly) {
      Mount-WindowsImage -ImagePath $ImagePath -Index $Index -Path $MountPath -ReadOnly -LogPath $LogPath | Out-Null
    }
    else {
      Mount-WindowsImage -ImagePath $ImagePath -Index $Index -Path $MountPath -CheckIntegrity -LogPath $LogPath | Out-Null
    }
  }
  catch {
    throw (New-StormError -Code Mount -Message "Mounting failed: $($_.Exception.Message)" -Fix 'Run scripts/rollback.ps1, make sure no antivirus scan or Explorer window holds the mount folder, then retry.' -InnerException $_.Exception)
  }
  $mounted = Get-StormMountedImages | Where-Object { Test-StormPathEqual $_.Path $MountPath } | Select-Object -First 1
  if (-not $mounted -or $mounted.MountStatus -ne 'Ok') {
    throw (New-StormError -Code Mount -Message "The image did not mount cleanly at $MountPath." -Fix 'Run scripts/rollback.ps1 and retry; check build/logs/dism.log.')
  }
}

function Test-StormMountedWindows {
  <# A mounted Windows image must contain the offline hives and the default user profile. #>
  param([Parameter(Mandatory)] [string] $MountPath)
  $missing = @()
  foreach ($relative in 'Windows\System32\config\SOFTWARE', 'Windows\System32\config\SYSTEM', 'Users\Default\NTUSER.DAT', 'Windows\explorer.exe') {
    if (-not (Test-Path -LiteralPath (Join-Path $MountPath $relative))) { $missing += $relative }
  }
  return , $missing
}

function Dismount-StormImage {
  param([Parameter(Mandatory)] [string] $MountPath, [switch] $Save, [string] $LogPath, [string] $Phase = '11')
  Assert-StormWindows -Operation 'Unmounting an image'
  Dismount-StormAllHives -Phase $Phase
  $mounted = Get-StormMountedImages | Where-Object { Test-StormPathEqual $_.Path $MountPath }
  if (-not $mounted) {
    Write-StormLog -Level Warn -Phase $Phase -Message "No image is mounted at $MountPath."
    return
  }
  try {
    if ($Save) {
      Write-StormLog -Phase $Phase -Message 'Committing changes and unmounting (this takes several minutes)...'
      Dismount-WindowsImage -Path $MountPath -Save -CheckIntegrity -LogPath $LogPath | Out-Null
    }
    else {
      Write-StormLog -Phase $Phase -Message 'Discarding changes and unmounting...'
      Dismount-WindowsImage -Path $MountPath -Discard -LogPath $LogPath | Out-Null
    }
  }
  catch {
    throw (New-StormError -Code Installer -Message "Unmounting failed: $($_.Exception.Message)" -Fix 'Close Explorer/terminal windows inside build/mounts, then run scripts/rollback.ps1.' -InnerException $_.Exception)
  }
}

function Get-StormHiveName {
  param([Parameter(Mandatory)] [ValidateSet('Software', 'System', 'DefaultUser')] [string] $Hive)
  return "$script:StormHivePrefix$($Hive.ToUpperInvariant())"
}

function Get-StormHiveFile {
  param([Parameter(Mandatory)] [string] $MountPath, [Parameter(Mandatory)] [ValidateSet('Software', 'System', 'DefaultUser')] [string] $Hive)
  switch ($Hive) {
    'Software' { return Join-Path $MountPath 'Windows\System32\config\SOFTWARE' }
    'System' { return Join-Path $MountPath 'Windows\System32\config\SYSTEM' }
    'DefaultUser' { return Join-Path $MountPath 'Users\Default\NTUSER.DAT' }
  }
}

function Mount-StormHive {
  <# Loads an offline hive under HKLM\STORM_BUILD_<HIVE> with reg.exe (no PowerShell provider handles to leak). #>
  param([Parameter(Mandatory)] [string] $MountPath, [Parameter(Mandatory)] [ValidateSet('Software', 'System', 'DefaultUser')] [string] $Hive, [string] $Phase = '')
  $name = Get-StormHiveName -Hive $Hive
  $file = Get-StormHiveFile -MountPath $MountPath -Hive $Hive
  if (-not (Test-Path -LiteralPath $file)) {
    throw (New-StormError -Code Customization -Message "Offline hive not found: $file" -Fix 'The mounted image is not a complete Windows installation image; check the selected edition.')
  }
  Invoke-StormNative -Phase $Phase -FilePath 'reg.exe' -Arguments @('load', "HKLM\$name", $file) -ErrorCode Customization -Fix 'Run scripts/rollback.ps1 to unload leftover hives, then retry.' | Out-Null
  return "HKLM\$name"
}

function Dismount-StormHive {
  param([Parameter(Mandatory)] [ValidateSet('Software', 'System', 'DefaultUser')] [string] $Hive, [string] $Phase = '')
  $name = Get-StormHiveName -Hive $Hive
  for ($attempt = 1; $attempt -le 6; $attempt++) {
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
    & reg.exe unload "HKLM\$name" 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { return }
    Start-Sleep -Seconds $attempt
  }
  throw (New-StormError -Code Customization -Message "Could not unload the offline hive HKLM\$name." -Fix 'Close Registry Editor and any tool that browses HKLM\STORM_BUILD_*, then run scripts/rollback.ps1.')
}

function Get-StormLoadedHives {
  if (-not (Test-StormIsWindows)) { return @() }
  return @(Get-ChildItem -Path 'Registry::HKEY_LOCAL_MACHINE' -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -like "$script:StormHivePrefix*" } | ForEach-Object { $_.PSChildName })
}

function Dismount-StormAllHives {
  <# Unloads every STORM_BUILD_* hive (used before commit/discard and by rollback). #>
  param([string] $Phase = '')
  foreach ($loaded in Get-StormLoadedHives) {
    $hive = switch ($loaded.Substring($script:StormHivePrefix.Length)) { 'SOFTWARE' { 'Software' } 'SYSTEM' { 'System' } 'DEFAULTUSER' { 'DefaultUser' } default { $null } }
    if ($hive) {
      Write-StormLog -Level Warn -Phase $Phase -Message "Unloading leftover hive HKLM\$loaded"
      Dismount-StormHive -Hive $hive -Phase $Phase
    }
  }
}
