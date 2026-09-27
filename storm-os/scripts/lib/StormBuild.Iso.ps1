# ISO creation with oscdimg and structural verification of the result (El Torito boot catalog, volume label).

$script:StormIsoSectorSize = 2048

function Test-StormIsoLabel {
  <# ISO/UDF volume labels: 1-32 characters, A-Z 0-9 and underscore (what oscdimg and firmware menus handle well). #>
  param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Label)
  return $Label -match '^[A-Z0-9_]{1,32}$'
}

function New-StormIsoLabel {
  param([Parameter(Mandatory)] [string] $BuildVersion)
  $label = 'STORMOS_' + ($BuildVersion.ToUpperInvariant() -replace '[^A-Z0-9]', '_')
  if ($label.Length -gt 32) { $label = $label.Substring(0, 32) }
  return $label
}

function New-StormOscdimgArguments {
  <#
    Dual-boot (BIOS + UEFI) UDF image, the layout Microsoft documents for Windows media:
      -m  ignore the 700 MB size limit      -o  optimize duplicate files
      -u2 UDF file system (install.wim may exceed 4 GB)   -udfver102  UDF 1.02 for firmware compatibility
      -bootdata:2  BIOS entry (etfsboot.com) + UEFI entry (efisys.bin)
    -NoBootPrompt uses efisys_noprompt.bin (no "Press any key to boot from CD"), meant for automated VM tests only.
  #>
  param([Parameter(Mandatory)] [string] $MediaRoot, [Parameter(Mandatory)] [string] $IsoPath, [Parameter(Mandatory)] [string] $Label, [switch] $NoBootPrompt)
  if (-not (Test-StormIsoLabel $Label)) { throw (New-StormError -Code Iso -Message "Invalid ISO label '$Label'." -Fix 'Use 1-32 characters A-Z, 0-9 or _.') }
  $bios = Join-StormPath $MediaRoot 'boot\etfsboot.com'
  $efiName = if ($NoBootPrompt) { 'efisys_noprompt.bin' } else { 'efisys.bin' }
  $efi = Join-StormPath $MediaRoot "efi\microsoft\boot\$efiName"
  return @('-m', '-o', '-u2', '-udfver102', "-l$Label", "-bootdata:2#p0,e,b$bios#pEF,e,b$efi", $MediaRoot, $IsoPath)
}

function Invoke-StormOscdimg {
  param([Parameter(Mandatory)] [string] $Oscdimg, [Parameter(Mandatory)] [string[]] $Arguments, [string] $Phase = '12')
  Invoke-StormNative -Phase $Phase -FilePath $Oscdimg -Arguments $Arguments -ErrorCode Iso -Fix 'Check free space in the output directory and that build/iso contains boot\etfsboot.com and efi\microsoft\boot\efisys.bin.' | Out-Null
}

function Read-StormIsoSector {
  param([Parameter(Mandatory)] [System.IO.Stream] $Stream, [Parameter(Mandatory)] [long] $Sector)
  $buffer = New-Object byte[] $script:StormIsoSectorSize
  [void]$Stream.Seek($Sector * $script:StormIsoSectorSize, [System.IO.SeekOrigin]::Begin)
  $read = 0
  while ($read -lt $buffer.Length) {
    $count = $Stream.Read($buffer, $read, $buffer.Length - $read)
    if ($count -le 0) { break }
    $read += $count
  }
  if ($read -lt $buffer.Length) { return $null }
  return , $buffer
}

function Get-StormElToritoInfo {
  <#
    Reads the El Torito boot record (sector 17) and boot catalog of an ISO and returns the boot platforms it
    declares: 0 = x86 BIOS, 0xEF = UEFI. A Windows install ISO must declare both to boot on BIOS and UEFI machines.
  #>
  param([Parameter(Mandatory)] [System.IO.Stream] $Stream)
  $result = [ordered]@{ HasBootRecord = $false; CatalogSector = $null; Platforms = @(); BootableEntries = 0 }
  $record = Read-StormIsoSector -Stream $Stream -Sector 17
  if (-not $record) { return [pscustomobject]$result }
  $identifier = [System.Text.Encoding]::ASCII.GetString($record, 1, 5)
  $system = [System.Text.Encoding]::ASCII.GetString($record, 7, 23)
  if ($record[0] -ne 0 -or $identifier -ne 'CD001' -or $system -ne 'EL TORITO SPECIFICATION') { return [pscustomobject]$result }
  $result.HasBootRecord = $true
  $catalogSector = [System.BitConverter]::ToUInt32($record, 0x47)
  $result.CatalogSector = [long]$catalogSector
  $catalog = Read-StormIsoSector -Stream $Stream -Sector $catalogSector
  if (-not $catalog -or $catalog[0] -ne 1 -or $catalog[30] -ne 0x55 -or $catalog[31] -ne 0xAA) { return [pscustomobject]$result }
  $platforms = New-Object System.Collections.Generic.List[int]
  $platforms.Add([int]$catalog[1])
  $bootable = 0
  if ($catalog[32] -eq 0x88) { $bootable++ }
  $offset = 64
  while ($offset + 32 -le $catalog.Length) {
    $header = $catalog[$offset]
    if ($header -ne 0x90 -and $header -ne 0x91) { break }
    $platform = [int]$catalog[$offset + 1]
    $entries = [int][System.BitConverter]::ToUInt16($catalog, $offset + 2)
    if (-not $platforms.Contains($platform)) { $platforms.Add($platform) }
    for ($i = 1; $i -le $entries; $i++) {
      $entry = $offset + 32 * $i
      if ($entry + 32 -le $catalog.Length -and $catalog[$entry] -eq 0x88) { $bootable++ }
    }
    $offset += 32 * ($entries + 1)
    if ($header -eq 0x91) { break }
  }
  $result.Platforms = $platforms.ToArray()
  $result.BootableEntries = $bootable
  return [pscustomobject]$result
}

function Get-StormIsoVolumeLabel {
  <# Volume identifier from the ISO 9660 primary volume descriptor (sector 16, bytes 40-71). #>
  param([Parameter(Mandatory)] [System.IO.Stream] $Stream)
  $pvd = Read-StormIsoSector -Stream $Stream -Sector 16
  if (-not $pvd -or $pvd[0] -ne 1 -or [System.Text.Encoding]::ASCII.GetString($pvd, 1, 5) -ne 'CD001') { return $null }
  return [System.Text.Encoding]::ASCII.GetString($pvd, 40, 32).Trim()
}

function Get-StormIsoBootInfo {
  param([Parameter(Mandatory)] [string] $Path)
  $stream = [System.IO.File]::OpenRead($Path)
  try {
    $info = Get-StormElToritoInfo -Stream $stream
    $label = Get-StormIsoVolumeLabel -Stream $stream
    return [pscustomobject]@{ HasBootRecord = $info.HasBootRecord; Platforms = $info.Platforms; BootableEntries = $info.BootableEntries; Label = $label; SizeBytes = $stream.Length }
  }
  finally {
    $stream.Dispose()
  }
}
