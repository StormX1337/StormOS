# Prerequisite checks. Evaluation functions (Test-*) are pure and take measured values; Get-StormPrerequisiteReport
# probes the machine and feeds them.

$script:StormRequiredFreeBytes = 40GB     # source copy + exported WIM + mounted image + ISO for one edition
$script:StormRecommendedFreeBytes = 60GB  # room for a second build or a VM disk
$script:StormMinimumHostBuild = 19041     # Windows 10 2004: first release whose DISM services Windows 11 images reliably

function New-StormCheck {
  param(
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [ValidateSet('PASS', 'WARN', 'FAIL')] [string] $Status,
    [Parameter(Mandatory)] [string] $Detail,
    [string] $Fix = '',
    [bool] $Mandatory = $true
  )
  return [pscustomobject]@{ Name = $Name; Status = $Status; Detail = $Detail; Fix = $Fix; Mandatory = $Mandatory }
}

function Test-StormHostVersion {
  param([Parameter(Mandatory)] [version] $Version, [string] $Caption = 'Windows')
  if ($Version.Major -lt 10 -or ($Version.Major -eq 10 -and $Version.Build -lt $script:StormMinimumHostBuild)) {
    return New-StormCheck -Name 'Windows version' -Status FAIL -Detail "$Caption $Version is too old to service Windows 11 images." -Fix "Build on Windows 10 2004 (build $script:StormMinimumHostBuild) or later, ideally Windows 11."
  }
  return New-StormCheck -Name 'Windows version' -Status PASS -Detail "$Caption $Version"
}

function Test-StormFreeSpace {
  param([Parameter(Mandatory)] [double] $FreeBytes, [string] $Drive = 'workspace',
    [double] $RequiredBytes = $script:StormRequiredFreeBytes, [double] $RecommendedBytes = $script:StormRecommendedFreeBytes)
  $detail = "$(Format-StormBytes $FreeBytes) free on $Drive"
  if ($FreeBytes -lt $RequiredBytes) {
    return New-StormCheck -Name 'Available storage' -Status FAIL -Detail "$detail; at least $(Format-StormBytes $RequiredBytes) are required." -Fix 'Free up space or pass -OutputDirectory and move the storm-os folder to a larger NTFS drive.'
  }
  if ($FreeBytes -lt $RecommendedBytes) {
    return New-StormCheck -Name 'Available storage' -Status WARN -Detail "$detail; $(Format-StormBytes $RecommendedBytes) recommended." -Fix 'Builds work, but VM tests and a second build may run out of space.' -Mandatory $false
  }
  return New-StormCheck -Name 'Available storage' -Status PASS -Detail $detail
}

function Test-StormFileSystem {
  param([AllowEmptyString()] [string] $FileSystem, [string] $Drive = 'workspace')
  if ($FileSystem -eq 'NTFS') { return New-StormCheck -Name 'NTFS workspace' -Status PASS -Detail "$Drive is NTFS" }
  $shown = if ($FileSystem) { $FileSystem } else { 'unknown' }
  return New-StormCheck -Name 'NTFS workspace' -Status FAIL -Detail "$Drive uses $shown. DISM can only mount images on NTFS (ACLs, reparse points, hard links)." -Fix 'Place the storm-os folder on an NTFS volume.'
}

function Test-StormToolPresence {
  <# Generic "tool found?" evaluation used for DISM, oscdimg, WinPE, .NET, MSBuild. #>
  param(
    [Parameter(Mandatory)] [string] $Name,
    [AllowEmptyString()] [string] $Path,
    [Parameter(Mandatory)] [string] $Fix,
    [bool] $Mandatory = $true,
    [string] $Purpose = ''
  )
  if ($Path) { return New-StormCheck -Name $Name -Status PASS -Detail $Path -Mandatory $Mandatory }
  $detail = "$Name was not found." + $(if ($Purpose) { " Needed for: $Purpose" } else { '' })
  $status = if ($Mandatory) { 'FAIL' } else { 'WARN' }
  return New-StormCheck -Name $Name -Status $status -Detail $detail -Fix $Fix -Mandatory $Mandatory
}

function Test-StormSourcePath {
  param([AllowEmptyString()] [string] $Path, [bool] $Exists)
  if (-not $Path) {
    return New-StormCheck -Name 'Source image' -Status WARN -Detail 'No source given yet (-SourceIso or -SourceWim).' -Fix 'Pass the path of a Windows 11 ISO you are licensed to use.' -Mandatory $false
  }
  if (-not $Exists) {
    return New-StormCheck -Name 'Source image' -Status FAIL -Detail "Source not found: $Path" -Fix 'Check the path. Download Windows 11 media from Microsoft (microsoft.com/software-download/windows11).'
  }
  $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
  if ($extension -notin '.iso', '.wim', '.esd') {
    return New-StormCheck -Name 'Source image' -Status FAIL -Detail "Unsupported source type '$extension'." -Fix 'Use a Windows 11 .iso, install.wim or install.esd.'
  }
  return New-StormCheck -Name 'Source image' -Status PASS -Detail $Path
}

function Get-StormPrerequisiteSummary {
  <# PASS if every check passed, WARN if only optional checks warn, FAIL if any mandatory check failed. #>
  param([Parameter(Mandatory)] [object[]] $Checks)
  if ($Checks | Where-Object { $_.Status -eq 'FAIL' }) { return 'FAIL' }
  if ($Checks | Where-Object { $_.Status -eq 'WARN' }) { return 'WARN' }
  return 'PASS'
}

function Find-StormAdkRoot {
  <# Returns the Windows ADK folder ("...\Windows Kits\10\Assessment and Deployment Kit") or $null. #>
  if (-not (Test-StormIsWindows)) { return $null }
  $candidates = New-Object System.Collections.Generic.List[string]
  foreach ($key in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots', 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots') {
    $root = (Get-ItemProperty -Path $key -Name KitsRoot10 -ErrorAction SilentlyContinue).KitsRoot10
    if ($root) { $candidates.Add((Join-Path $root 'Assessment and Deployment Kit')) }
  }
  $candidates.Add((Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Assessment and Deployment Kit'))
  foreach ($candidate in $candidates) {
    if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'Deployment Tools'))) { return $candidate }
  }
  return $null
}

function Get-StormHostArchitectureFolder {
  if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { return 'arm64' }
  return 'amd64'
}

function Find-StormOscdimg {
  param([string] $AdkRoot = (Find-StormAdkRoot))
  if ($AdkRoot) {
    $path = Join-Path $AdkRoot ("Deployment Tools\{0}\Oscdimg\oscdimg.exe" -f (Get-StormHostArchitectureFolder))
    if (Test-Path -LiteralPath $path) { return $path }
  }
  $command = Get-Command oscdimg.exe -ErrorAction SilentlyContinue
  if ($command) { return $command.Source }
  return $null
}

function Find-StormWinPe {
  param([string] $AdkRoot = (Find-StormAdkRoot))
  if (-not $AdkRoot) { return $null }
  $path = Join-Path $AdkRoot 'Windows Preinstallation Environment'
  if (Test-Path -LiteralPath $path) { return $path }
  return $null
}

function Find-StormMsBuild {
  if (-not (Test-StormIsWindows)) { return $null }
  $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
  if (Test-Path -LiteralPath $vswhere) {
    $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1
    if ($found) { return $found }
  }
  $command = Get-Command msbuild.exe -ErrorAction SilentlyContinue
  if ($command) { return $command.Source }
  return $null
}

function Get-StormPrerequisiteReport {
  <#
    Probes the machine. Mandatory for the ISO milestone: Windows host, administrator, DISM, oscdimg (ADK Deployment
    Tools), PowerShell 5.1+, NTFS workspace with enough space, a valid source. WinPE, .NET SDK and MSBuild are only
    needed for later phases (Storm WinPE, building Storm apps) and are reported as warnings.
  #>
  param([Parameter(Mandatory)] [string] $Workspace, [string] $SourcePath, [switch] $RequireWinPe, [switch] $RequireAppToolchain)
  $checks = New-Object System.Collections.Generic.List[object]

  if (-not (Test-StormIsWindows)) {
    $checks.Add((New-StormCheck -Name 'Windows version' -Status FAIL -Detail "This host is $([System.Environment]::OSVersion.VersionString)." -Fix 'Run the build on Windows 10 2004+ or Windows 11.'))
    $checks.Add((Test-StormSourcePath -Path $SourcePath -Exists ([bool]($SourcePath -and (Test-Path -LiteralPath $SourcePath)))))
    return $checks.ToArray()
  }

  $os = Get-CimInstance -ClassName Win32_OperatingSystem
  $checks.Add((Test-StormHostVersion -Version ([version]$os.Version) -Caption $os.Caption))

  if (Test-StormAdministrator) {
    $checks.Add((New-StormCheck -Name 'Administrator' -Status PASS -Detail 'Running elevated'))
  }
  else {
    $checks.Add((New-StormCheck -Name 'Administrator' -Status FAIL -Detail 'DISM image mounting and offline registry editing require administrator rights.' -Fix 'Start PowerShell with "Run as administrator" and run the script again.'))
  }

  $dism = Get-Command dism.exe -ErrorAction SilentlyContinue
  $dismModule = Get-Module -ListAvailable -Name Dism | Select-Object -First 1
  $dismDetail = if ($dism -and $dismModule) { "$($dism.Source) (DISM module $($dismModule.Version))" } else { '' }
  $checks.Add((Test-StormToolPresence -Name 'DISM' -Path $dismDetail -Fix 'DISM ships with Windows; repair the host with "sfc /scannow" or install the Windows ADK Deployment Tools.' -Purpose 'mounting and servicing the Windows image'))

  $adk = Find-StormAdkRoot
  $checks.Add((Test-StormToolPresence -Name 'Windows ADK (Deployment Tools)' -Path $adk -Fix 'Install the Windows ADK for Windows 11 with the "Deployment Tools" feature: https://learn.microsoft.com/windows-hardware/get-started/adk-install' -Purpose 'oscdimg (ISO creation)'))
  $checks.Add((Test-StormToolPresence -Name 'oscdimg' -Path (Find-StormOscdimg -AdkRoot $adk) -Fix 'Install the Windows ADK "Deployment Tools" feature; oscdimg.exe is part of it.' -Purpose 'building the bootable StormOS.iso'))
  $checks.Add((Test-StormToolPresence -Name 'WinPE add-on' -Path (Find-StormWinPe -AdkRoot $adk) -Mandatory ([bool]$RequireWinPe) -Fix 'Install the "Windows PE add-on for the Windows ADK".' -Purpose 'the Storm WinPE setup environment (phase 6)'))

  $psVersion = $PSVersionTable.PSVersion
  if ($psVersion -ge [version]'5.1') {
    $checks.Add((New-StormCheck -Name 'PowerShell' -Status PASS -Detail "PowerShell $psVersion ($($PSVersionTable.PSEdition))"))
  }
  else {
    $checks.Add((New-StormCheck -Name 'PowerShell' -Status FAIL -Detail "PowerShell $psVersion" -Fix 'Use Windows PowerShell 5.1 (built into Windows 10/11) or PowerShell 7.'))
  }

  $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
  $dotnetDetail = ''
  if ($dotnet) {
    $sdks = & $dotnet.Source --list-sdks 2>$null
    if ($sdks) { $dotnetDetail = "$($dotnet.Source) (SDKs: $((($sdks | ForEach-Object { ($_ -split ' ')[0] }) -join ', ')))" }
  }
  $checks.Add((Test-StormToolPresence -Name '.NET SDK' -Path $dotnetDetail -Mandatory ([bool]$RequireAppToolchain) -Fix 'Install the .NET SDK from https://dot.net (version in global.json).' -Purpose 'building the Storm applications'))
  $checks.Add((Test-StormToolPresence -Name 'MSBuild / VS Build Tools' -Path (Find-StormMsBuild) -Mandatory ([bool]$RequireAppToolchain) -Fix 'Install Visual Studio 2022 Build Tools with the ".NET desktop build tools" and "Windows App SDK" workloads.' -Purpose 'building the WinUI Storm applications'))

  $workspaceRoot = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($Workspace))
  $volume = Get-Volume -DriveLetter $workspaceRoot.Substring(0, 1) -ErrorAction SilentlyContinue
  if ($volume) {
    $checks.Add((Test-StormFileSystem -FileSystem $volume.FileSystem -Drive $workspaceRoot))
    $checks.Add((Test-StormFreeSpace -FreeBytes $volume.SizeRemaining -Drive $workspaceRoot))
  }
  else {
    $checks.Add((New-StormCheck -Name 'NTFS workspace' -Status FAIL -Detail "Cannot inspect the volume of $Workspace." -Fix 'Use a workspace on a local fixed drive.'))
  }

  $pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
  if ($pending) {
    $checks.Add((New-StormCheck -Name 'Pending restart' -Status WARN -Detail 'Windows reports a pending restart; DISM mounts can fail until the host restarts.' -Fix 'Restart the build machine before building.' -Mandatory $false))
  }
  else {
    $checks.Add((New-StormCheck -Name 'Pending restart' -Status PASS -Detail 'No pending restart'))
  }

  $checks.Add((Test-StormSourcePath -Path $SourcePath -Exists ([bool]($SourcePath -and (Test-Path -LiteralPath $SourcePath)))))
  return $checks.ToArray()
}

function Write-StormCheckTable {
  param([Parameter(Mandatory)] [object[]] $Checks, [string] $Phase = '00')
  foreach ($check in $Checks) {
    $level = switch ($check.Status) { 'PASS' { 'Success' } 'WARN' { 'Warn' } default { 'Error' } }
    Write-StormLog -Level $level -Phase $Phase -Message ('{0,-5} {1,-32} {2}' -f $check.Status, $check.Name, $check.Detail)
    if ($check.Status -ne 'PASS' -and $check.Fix) {
      Write-StormLog -Level Detail -Phase $Phase -Message ('      fix: {0}' -f $check.Fix)
    }
  }
}
