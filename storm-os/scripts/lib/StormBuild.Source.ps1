# Source media: ISO/WIM/ESD resolution, ISO import (read-only mount + copy), edition listing and selection.
# The source is never modified: ISOs are mounted read-only and copied, WIM/ESD files are only read.

function Resolve-StormSource {
  <#
    Decides the source mode from the parameters. ISO: media and install image come from the ISO. WIM/ESD: the image
    comes from the file and the boot files from -SourceMediaDirectory (an extracted Windows 11 ISO), because a WIM
    alone cannot boot.
  #>
  param([string] $SourceIso, [string] $SourceWim, [string] $SourceMediaDirectory)
  if ($SourceIso -and $SourceWim) {
    throw (New-StormError -Code Source -Message 'Pass either -SourceIso or -SourceWim, not both.' -Fix 'Use -SourceIso for a Windows 11 ISO, or -SourceWim together with -SourceMediaDirectory.')
  }
  if ($SourceIso) {
    if ([System.IO.Path]::GetExtension($SourceIso).ToLowerInvariant() -ne '.iso') {
      throw (New-StormError -Code Source -Message "-SourceIso must point to an .iso file: $SourceIso" -Fix 'Use -SourceWim for .wim/.esd files.')
    }
    return [pscustomobject]@{ Type = 'ISO'; Path = $SourceIso; MediaDirectory = $null }
  }
  if ($SourceWim) {
    $extension = [System.IO.Path]::GetExtension($SourceWim).ToLowerInvariant()
    if ($extension -notin '.wim', '.esd') {
      throw (New-StormError -Code Source -Message "-SourceWim must point to a .wim or .esd file: $SourceWim" -Fix 'Use -SourceIso for ISO files.')
    }
    if (-not $SourceMediaDirectory) {
      throw (New-StormError -Code Source -Message 'A WIM/ESD source needs boot media to build a bootable ISO.' -Fix 'Pass -SourceMediaDirectory with the extracted contents of a Windows 11 ISO (boot, efi, sources folders).')
    }
    return [pscustomobject]@{ Type = $extension.TrimStart('.').ToUpperInvariant(); Path = $SourceWim; MediaDirectory = $SourceMediaDirectory }
  }
  throw (New-StormError -Code Source -Message 'No source image given.' -Fix 'Pass -SourceIso "C:\path\Win11.iso" (or -SourceWim with -SourceMediaDirectory).')
}

function Test-StormMediaLayout {
  <# Returns the list of missing boot/setup files for an extracted Windows 11 media folder. #>
  param([Parameter(Mandatory)] [string] $MediaRoot)
  $required = @('boot\etfsboot.com', 'efi\microsoft\boot\efisys.bin', 'bootmgr', 'bootmgr.efi', 'sources\boot.wim', 'setup.exe')
  $missing = @()
  foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $MediaRoot $relative))) { $missing += $relative }
  }
  return , $missing
}

function Find-StormInstallImage {
  param([Parameter(Mandatory)] [string] $MediaRoot)
  foreach ($name in 'install.wim', 'install.esd') {
    $path = Join-Path (Join-Path $MediaRoot 'sources') $name
    if (Test-Path -LiteralPath $path) { return $path }
  }
  foreach ($name in 'install.swm') {
    if (Test-Path -LiteralPath (Join-Path (Join-Path $MediaRoot 'sources') $name)) {
      throw (New-StormError -Code Source -Message 'The media contains a split image (install.swm), which cannot be serviced directly.' -Fix 'Use the original Microsoft ISO, or merge the parts with "dism /Export-Image /SourceImageFile:install.swm /SWMFile:install*.swm".')
    }
  }
  throw (New-StormError -Code Source -Message "No install.wim or install.esd found in $MediaRoot\sources." -Fix 'Use an original Windows 11 ISO from Microsoft; media-creation-tool USB layouts must be converted back to an ISO first.')
}

function Import-StormSourceIso {
  <#
    Mounts the ISO read-only, copies its contents into build/iso and dismounts again (also on failure).
    Returns the SHA-256 of the ISO for the build report.
  #>
  param([Parameter(Mandatory)] [string] $IsoPath, [Parameter(Mandatory)] [string] $Destination, [string] $Phase = '02')
  Assert-StormWindows -Operation 'Importing an ISO'
  $iso = (Resolve-Path -LiteralPath $IsoPath).Path
  Write-StormLog -Phase $Phase -Message "Hashing source ISO (SHA-256)..."
  $hash = Get-StormSha256 -Path $iso
  Write-StormLog -Phase $Phase -Message "Source SHA-256: $hash"

  $image = $null
  try {
    $image = Mount-DiskImage -ImagePath $iso -Access ReadOnly -StorageType ISO -PassThru
    $letter = ($image | Get-Volume).DriveLetter
    if (-not $letter) { throw (New-StormError -Code Source -Message 'The ISO was mounted but has no drive letter.' -Fix 'Unmount other ISOs or free a drive letter, then retry.') }
    $sourceRoot = "${letter}:\"
    Write-StormLog -Phase $Phase -Message "Copying media from $sourceRoot to $Destination"
    # robocopy: exit codes below 8 mean success (files copied/extra files present).
    Invoke-StormNative -Phase $Phase -FilePath 'robocopy.exe' -Arguments @($sourceRoot, $Destination, '/E', '/R:2', '/W:2', '/NFL', '/NDL', '/NJH', '/NP') -SuccessCodes @(0, 1, 2, 3, 4, 5, 6, 7) -ErrorCode Source -Fix 'Check free space and that the ISO is not damaged (compare its SHA-256 with Microsoft''s published value).' | Out-Null
  }
  finally {
    if ($image) { Dismount-DiskImage -ImagePath $iso | Out-Null }
  }
  # Files copied from an ISO are read-only; the build replaces sources\install.wim later.
  Get-ChildItem -LiteralPath $Destination -Recurse -File -Force | Where-Object { $_.IsReadOnly } | ForEach-Object { $_.IsReadOnly = $false }
  return $hash
}

function ConvertTo-StormArchitecture {
  <# DISM ImageArchitecture values: 0 x86, 5 ARM, 9 x64, 12 ARM64. #>
  param([Parameter(Mandatory)] $Value)
  switch ([string]$Value) {
    '0' { return 'x86' } 'x86' { return 'x86' }
    '5' { return 'arm' } 'ARM' { return 'arm' }
    '9' { return 'x64' } 'x64' { return 'x64' } 'AMD64' { return 'x64' }
    '12' { return 'arm64' } 'ARM64' { return 'arm64' }
    default { return "unknown($Value)" }
  }
}

function Get-StormEditions {
  <# Lists the editions in a WIM/ESD with index, name, architecture, version, languages and size. #>
  param([Parameter(Mandatory)] [string] $ImagePath)
  Assert-StormWindows -Operation 'Reading image editions'
  $editions = @()
  foreach ($summary in (Get-WindowsImage -ImagePath $ImagePath)) {
    $detail = Get-WindowsImage -ImagePath $ImagePath -Index $summary.ImageIndex
    $editions += [pscustomobject]@{
      Index        = [int]$summary.ImageIndex
      Name         = $summary.ImageName
      EditionId    = $detail.EditionId
      Architecture = ConvertTo-StormArchitecture $detail.Architecture
      Version      = [string]$detail.Version
      Languages    = @($detail.Languages)
      SizeBytes    = [double]$summary.ImageSize
    }
  }
  return $editions
}

function Format-StormEditionTable {
  param([Parameter(Mandatory)] [object[]] $Editions)
  $lines = @('INDEX  EDITION                                   ARCH    VERSION          LANGUAGE  SIZE')
  foreach ($edition in $Editions) {
    $lines += '{0,-6} {1,-41} {2,-7} {3,-16} {4,-9} {5}' -f $edition.Index, $edition.Name, $edition.Architecture, $edition.Version, (($edition.Languages | Select-Object -First 1) -join ''), (Format-StormBytes $edition.SizeBytes)
  }
  return $lines
}

function Select-StormEdition {
  <#
    Validates an explicit edition choice. STORM OS requires Windows 11 (build 22000+) on x64 or ARM64; the build
    never picks an edition on its own.
  #>
  param([Parameter(Mandatory)] [object[]] $Editions, [Parameter(Mandatory)] [int] $Index)
  $edition = $Editions | Where-Object { $_.Index -eq $Index } | Select-Object -First 1
  if (-not $edition) {
    $available = ($Editions | ForEach-Object { "$($_.Index) = $($_.Name)" }) -join '; '
    throw (New-StormError -Code EditionSelection -Message "Edition index $Index does not exist in the source image." -Fix "Pick one of: $available")
  }
  $build = 0
  try { $build = ([version]$edition.Version).Build } catch { $build = 0 }
  if ($build -lt 22000) {
    throw (New-StormError -Code Source -Message "Edition $Index ($($edition.Name), $($edition.Version)) is not Windows 11." -Fix 'STORM OS is built on Windows 11 (build 22000 or later). Use a Windows 11 ISO.')
  }
  if ($edition.Architecture -notin 'x64', 'arm64') {
    throw (New-StormError -Code Source -Message "Edition $Index has architecture $($edition.Architecture)." -Fix 'Use an x64 or ARM64 Windows 11 image.')
  }
  return $edition
}

function Get-StormEditionFamily {
  <# Maps an edition name to the family used by edition-specific settings: Home, Pro, Education, Enterprise, Other. #>
  param([Parameter(Mandatory)] [string] $EditionName)
  if ($EditionName -match 'Enterprise') { return 'Enterprise' }
  if ($EditionName -match 'Education') { return 'Education' }
  if ($EditionName -match '\bPro\b') { return 'Pro' }
  if ($EditionName -match '\bHome\b') { return 'Home' }
  return 'Other'
}
