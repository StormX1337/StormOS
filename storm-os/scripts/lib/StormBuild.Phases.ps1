# Phase implementations. Each takes (Paths, State, Options), updates the state and returns an optional
# @{ Status; Message } (Completed by default). build-all.ps1 and the numbered scripts call the same functions.

function Get-StormPhaseTable {
  return @(
    [pscustomobject]@{ Id = '00'; Title = 'Prerequisites'; Function = 'Invoke-StormPhasePrerequisites' }
    [pscustomobject]@{ Id = '01'; Title = 'Prepare workspace'; Function = 'Invoke-StormPhaseWorkspace' }
    [pscustomobject]@{ Id = '02'; Title = 'Import source'; Function = 'Invoke-StormPhaseImport' }
    [pscustomobject]@{ Id = '03'; Title = 'Inspect image and select edition'; Function = 'Invoke-StormPhaseInspect' }
    [pscustomobject]@{ Id = '04'; Title = 'Export and mount edition'; Function = 'Invoke-StormPhaseMount' }
    [pscustomobject]@{ Id = '05'; Title = 'Apply Storm branding'; Function = 'Invoke-StormPhaseBranding' }
    [pscustomobject]@{ Id = '06'; Title = 'Apply Storm defaults'; Function = 'Invoke-StormPhaseDefaults' }
    [pscustomobject]@{ Id = '07'; Title = 'Apply gaming defaults'; Function = 'Invoke-StormPhaseGaming' }
    [pscustomobject]@{ Id = '08'; Title = 'Integrate Storm apps'; Function = 'Invoke-StormPhaseApps' }
    [pscustomobject]@{ Id = '09'; Title = 'Configure OOBE'; Function = 'Invoke-StormPhaseOobe' }
    [pscustomobject]@{ Id = '10'; Title = 'Build Storm WinPE'; Function = 'Invoke-StormPhaseWinPe' }
    [pscustomobject]@{ Id = '11'; Title = 'Commit image and build installer media'; Function = 'Invoke-StormPhaseInstaller' }
    [pscustomobject]@{ Id = '12'; Title = 'Build ISO'; Function = 'Invoke-StormPhaseIso' }
    [pscustomobject]@{ Id = '13'; Title = 'Validate build'; Function = 'Invoke-StormPhaseValidate' }
    [pscustomobject]@{ Id = '14'; Title = 'VM boot test'; Function = 'Invoke-StormPhaseVmTest' }
    [pscustomobject]@{ Id = '15'; Title = 'Cleanup'; Function = 'Invoke-StormPhaseCleanup' }
  )
}

function Get-StormDismLog {
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $Paths)
  return Join-Path $Paths.Logs 'dism.log'
}

function Add-StormCustomizations {
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $State, [object[]] $Results)
  $State.customizations = @(@($State.customizations) + @($Results | ForEach-Object { ConvertTo-StormHashtable $_ }))
}

# ---- 00 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhasePrerequisites {
  param($Paths, $State, $Options)
  $source = Get-StormOption -Options $Options -State $State -Name SourceIso
  if (-not $source) { $source = Get-StormOption -Options $Options -State $State -Name SourceWim }
  $checks = @(Get-StormPrerequisiteReport -Workspace $Paths.Root -SourcePath $source -RequireWinPe:([bool](Get-StormOption -Options $Options -State $State -Name RequireWinPe -Default $false)))
  if ($Paths.Root -match '\s') {
    $checks += New-StormCheck -Name 'Workspace path' -Status FAIL -Detail "The workspace path contains spaces: $($Paths.Root)" -Fix 'Move the storm-os folder to a path without spaces (oscdimg boot-file arguments do not support them), e.g. C:\StormOS.'
  }
  else {
    $checks += New-StormCheck -Name 'Workspace path' -Status PASS -Detail $Paths.Root
  }
  Write-StormCheckTable -Checks $checks -Phase '00'
  if (-not (Test-Path $Paths.Logs)) { New-Item -ItemType Directory -Path $Paths.Logs -Force | Out-Null }
  $checks | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $Paths.Logs 'prerequisites.json') -Encoding UTF8
  $summary = Get-StormPrerequisiteSummary -Checks $checks
  Write-StormLog -Phase '00' -Level $(if ($summary -eq 'FAIL') { 'Error' } elseif ($summary -eq 'WARN') { 'Warn' } else { 'Success' }) -Message "Prerequisites: $summary"
  if ($summary -eq 'FAIL') {
    $failed = ($checks | Where-Object { $_.Status -eq 'FAIL' } | ForEach-Object { $_.Name }) -join ', '
    throw (New-StormError -Code Prerequisites -Message "Mandatory prerequisites are missing: $failed" -Fix 'Apply the fixes listed above (details in build/logs/prerequisites.json).')
  }
  return @{ Status = 'Completed'; Message = $summary }
}

# ---- 01 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseWorkspace {
  param($Paths, $State, $Options)
  $clean = [bool](Get-StormOption -Options $Options -State $State -Name CleanBuild -Default $false)
  foreach ($key in 'Build', 'Input', 'Output', 'Iso', 'Mounts', 'MountInstall', 'MountBoot', 'Temp', 'Logs', 'Artifacts') {
    if (-not (Test-Path -LiteralPath $Paths[$key])) { New-Item -ItemType Directory -Path $Paths[$key] -Force | Out-Null }
  }
  if (Test-StormIsWindows) {
    $mounted = @(Get-StormMountedImages | Where-Object { $_.Path -like "$($Paths.Mounts)*" })
    if ($mounted.Count) {
      throw (New-StormError -Code Workspace -Message "Images are still mounted under build/mounts: $(($mounted | ForEach-Object { $_.Path }) -join ', ')" -Fix 'Run scripts/rollback.ps1 to discard the previous build safely.')
    }
  }
  if ($clean) {
    Write-StormLog -Phase '01' -Message 'Clean build: removing previous media, temporary files and output (source files and logs are kept).'
    foreach ($key in 'Iso', 'Temp', 'Output', 'Artifacts', 'MountInstall', 'MountBoot', 'MountVerify') {
      if (Test-Path -LiteralPath $Paths[$key]) {
        Get-ChildItem -LiteralPath $Paths[$key] -Force | Where-Object { $_.FullName -ne $Paths.State } | Remove-Item -Recurse -Force
      }
    }
  }
  elseif (-not (Test-StormDirectoryEmpty -Path $Paths.Iso)) {
    throw (New-StormError -Code Workspace -Message 'build/iso contains media from a previous build.' -Fix 'Pass -CleanBuild to start fresh.')
  }
  $State.options = [ordered]@{
    sourceIso            = Get-StormOption -Options $Options -Name SourceIso
    sourceWim            = Get-StormOption -Options $Options -Name SourceWim
    sourceMediaDirectory = Get-StormOption -Options $Options -Name SourceMediaDirectory
    editionIndex         = Get-StormOption -Options $Options -Name EditionIndex
    outputDirectory      = $Paths.Output
    enableGamingDefaults = [bool](Get-StormOption -Options $Options -Name EnableGamingDefaults -Default $false)
    enableStormApps      = [bool](Get-StormOption -Options $Options -Name EnableStormApps -Default $false)
    enableOverlay        = [bool](Get-StormOption -Options $Options -Name EnableOverlay -Default $false)
    enableBenchmarks     = [bool](Get-StormOption -Options $Options -Name EnableBenchmarks -Default $false)
    stormAppsInstaller   = Get-StormOption -Options $Options -Name StormAppsInstaller
    noBootPrompt         = [bool](Get-StormOption -Options $Options -Name NoBootPrompt -Default $false)
    isoName              = [string](Get-StormOption -Options $Options -Name IsoName -Default 'StormOS.iso')
    cleanBuild           = $clean
  }
  Save-StormState -Paths $Paths -State $State
  Write-StormLog -Phase '01' -Message "Workspace ready: $($Paths.Build) (build $($State.buildId), STORM OS $($State.buildVersion))"
}

# ---- 02 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseImport {
  param($Paths, $State, $Options)
  $source = Resolve-StormSource -SourceIso (Get-StormOption $Options $State SourceIso) -SourceWim (Get-StormOption $Options $State SourceWim) -SourceMediaDirectory (Get-StormOption $Options $State SourceMediaDirectory)
  if (-not (Test-Path -LiteralPath $source.Path)) {
    throw (New-StormError -Code Source -Message "Source not found: $($source.Path)" -Fix 'Check the path passed to -SourceIso / -SourceWim.')
  }
  if (-not (Test-StormDirectoryEmpty -Path $Paths.Iso)) {
    throw (New-StormError -Code Workspace -Message 'build/iso is not empty.' -Fix 'Run with -CleanBuild (or scripts/01-prepare-workspace.ps1 -CleanBuild).')
  }
  if ($source.Type -eq 'ISO') {
    $hash = Import-StormSourceIso -IsoPath $source.Path -Destination $Paths.Iso
    $installImage = Find-StormInstallImage -MediaRoot $Paths.Iso
  }
  else {
    Assert-StormWindows -Operation 'Importing media'
    Write-StormLog -Phase '02' -Message "Copying boot media from $($source.MediaDirectory) (install image excluded)"
    Invoke-StormNative -Phase '02' -FilePath 'robocopy.exe' -Arguments @($source.MediaDirectory, $Paths.Iso, '/E', '/R:2', '/W:2', '/NFL', '/NDL', '/NJH', '/NP', '/XF', 'install.wim', 'install.esd', 'install*.swm') -SuccessCodes @(0, 1, 2, 3, 4, 5, 6, 7) -ErrorCode Source -Fix 'Check the media folder path and free space.' | Out-Null
    Write-StormLog -Phase '02' -Message 'Hashing source image (SHA-256)...'
    $hash = Get-StormSha256 -Path $source.Path
    $installImage = (Resolve-Path -LiteralPath $source.Path).Path
  }
  $missing = Test-StormMediaLayout -MediaRoot $Paths.Iso
  if ($missing.Count) {
    throw (New-StormError -Code Source -Message "The media is missing boot/setup files: $($missing -join ', ')" -Fix 'Use an original Windows 11 ISO from Microsoft (or its fully extracted contents).')
  }
  $State.source = [ordered]@{ type = $source.Type; path = $source.Path; sha256 = $hash; mediaRoot = $Paths.Iso; installImage = $installImage }
  $State.options.sourceIso = if ($source.Type -eq 'ISO') { $source.Path } else { $null }
  $State.options.sourceWim = if ($source.Type -ne 'ISO') { $source.Path } else { $null }
  $State.options.sourceMediaDirectory = $source.MediaDirectory
  Write-StormLog -Phase '02' -Message "Source imported: $($source.Type) $($source.Path) -> install image $installImage"
}

# ---- 03 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseInspect {
  param($Paths, $State, $Options)
  if (-not $State.source.installImage) { throw (New-StormError -Code Source -Message 'No imported source in the build state.' -Fix 'Run scripts/02-import-source.ps1 first.') }
  $editions = @(Get-StormEditions -ImagePath $State.source.installImage)
  foreach ($line in (Format-StormEditionTable -Editions $editions)) { Write-StormLog -Phase '03' -Message $line }
  $editions | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $Paths.Logs 'editions.json') -Encoding UTF8
  $index = Get-StormOption $Options $State EditionIndex
  if ($null -eq $index -or "$index" -eq '') {
    if ([Environment]::UserInteractive -and -not $env:CI) {
      $answer = Read-Host 'Select the edition index to build'
      if ($answer -match '^\d+$') { $index = [int]$answer }
    }
  }
  if ($null -eq $index -or "$index" -eq '') {
    $list = ($editions | ForEach-Object { "$($_.Index) = $($_.Name)" }) -join '; '
    throw (New-StormError -Code EditionSelection -Message 'An explicit edition selection is required.' -Fix "Pass -EditionIndex <n>. Available: $list")
  }
  $edition = Select-StormEdition -Editions $editions -Index ([int]$index)
  $State.edition = [ordered]@{
    index        = $edition.Index
    name         = $edition.Name
    editionId    = $edition.EditionId
    family       = Get-StormEditionFamily -EditionName $edition.Name
    architecture = $edition.Architecture
    version      = $edition.Version
    languages    = @($edition.Languages)
    sizeBytes    = $edition.SizeBytes
  }
  $State.options.editionIndex = $edition.Index
  Write-StormLog -Level Success -Phase '03' -Message "Selected edition $($edition.Index): $($edition.Name) $($edition.Architecture) $($edition.Version)"
}

# ---- 04 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseMount {
  param($Paths, $State, $Options)
  if (-not $State.edition.index) { throw (New-StormError -Code EditionSelection -Message 'No edition selected.' -Fix 'Run scripts/03-inspect-image.ps1 -EditionIndex <n> first.') }
  $working = Join-Path $Paths.Temp 'install.wim'
  $log = Get-StormDismLog $Paths
  Export-StormEdition -SourceImage $State.source.installImage -Index $State.edition.index -Destination $working -LogPath $log
  Mount-StormImage -ImagePath $working -Index 1 -MountPath $Paths.MountInstall -LogPath $log
  $missing = Test-StormMountedWindows -MountPath $Paths.MountInstall
  if ($missing.Count) {
    throw (New-StormError -Code Mount -Message "The mounted image is not a complete Windows image (missing $($missing -join ', '))." -Fix 'Select a Windows 11 client edition from an original Microsoft ISO.')
  }
  $State.artifacts.workingWim = $working
  Write-StormLog -Level Success -Phase '04' -Message "Mounted and verified at $($Paths.MountInstall)"
}

function Assert-StormMounted {
  param([Parameter(Mandatory)] $Paths, [string] $Phase)
  $mounted = Get-StormMountedImages | Where-Object { (Test-StormPathEqual $_.Path $Paths.MountInstall) -and $_.MountStatus -eq 'Ok' }
  if (-not $mounted) { throw (New-StormError -Code Mount -Message 'The install image is not mounted.' -Fix 'Run scripts/04-mount-install.ps1 first (or scripts/build-all.ps1).') }
}

# ---- 05 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseBranding {
  param($Paths, $State, $Options)
  Assert-StormMounted -Paths $Paths -Phase '05'
  $branding = Get-StormBrandingConfig -Paths $Paths
  $files = Install-StormBrandingFiles -MountPath $Paths.MountInstall -Paths $Paths -Branding $branding
  $settings = Get-StormBrandingRegistrySettings -Branding $branding -EditionName $State.edition.name
  foreach ($setting in $settings) {
    $problems = Test-StormRegistrySetting -Setting $setting
    if ($problems.Count) { throw (New-StormError -Code Customization -Message "Invalid branding setting: $($problems -join '; ')" -Fix 'Fix config/branding/branding.json.') }
  }
  $results = Invoke-StormRegistrySettings -MountPath $Paths.MountInstall -Settings $settings -EditionFamily $State.edition.family -Source 'branding' -Phase '05'
  Add-StormCustomizations -State $State -Results $results
  $State.artifacts.brandingFiles = @($files | ForEach-Object { ConvertTo-StormHashtable $_ })
  return @{ Status = 'Completed'; Message = "$($files.Count) files, $(@($results | Where-Object { $_.status -ne 'NotApplicable' }).Count) registry values" }
}

# ---- 06 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseDefaults {
  param($Paths, $State, $Options)
  Assert-StormMounted -Paths $Paths -Phase '06'
  $settings = Import-StormRegistryConfig -Path (Join-Path $Paths.Config 'defaults\desktop-defaults.json')
  $results = Invoke-StormRegistrySettings -MountPath $Paths.MountInstall -Settings $settings -EditionFamily $State.edition.family -Source 'defaults' -Phase '06'
  Add-StormCustomizations -State $State -Results $results

  # Image identity for Storm apps, Storm Control ("Storm Version") and validation.
  $identity = [ordered]@{
    product      = 'STORM OS'
    statement    = 'STORM OS is a Storm customization and deployment environment built on Windows.'
    stormVersion = $State.buildVersion
    buildId      = $State.buildId
    builtAt      = (Get-Date).ToUniversalTime().ToString('o')
    windows      = [ordered]@{ edition = $State.edition.name; version = $State.edition.version; architecture = $State.edition.architecture }
    features     = [ordered]@{ gamingDefaults = [bool]$State.options.enableGamingDefaults; stormApps = [bool]$State.options.enableStormApps; overlay = [bool]$State.options.enableOverlay; benchmarks = [bool]$State.options.enableBenchmarks }
  }
  $target = Join-Path $Paths.MountInstall 'ProgramData\StormOS\Config\storm-image.json'
  New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
  [System.IO.File]::WriteAllText($target, ($identity | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))
  Write-StormLog -Phase '06' -Message 'Wrote ProgramData\StormOS\Config\storm-image.json'
  return @{ Status = 'Completed'; Message = "$(@($results | Where-Object { $_.status -eq 'Applied' }).Count) applied, $(@($results | Where-Object { $_.status -eq 'AlreadySet' }).Count) already set" }
}

# ---- 07 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseGaming {
  param($Paths, $State, $Options)
  $State.options.enableGamingDefaults = [bool](Get-StormOption $Options $State EnableGamingDefaults -Default $false)
  if (-not $State.options.enableGamingDefaults) { return @{ Status = 'Skipped'; Message = 'Gaming defaults not requested (-EnableGamingDefaults).' } }
  Assert-StormMounted -Paths $Paths -Phase '07'
  $settings = Import-StormRegistryConfig -Path (Join-Path $Paths.Config 'gaming\gaming-defaults.json')
  $results = Invoke-StormRegistrySettings -MountPath $Paths.MountInstall -Settings $settings -EditionFamily $State.edition.family -Source 'gaming' -Phase '07'
  Add-StormCustomizations -State $State -Results $results
  return @{ Status = 'Completed'; Message = "$(@($results | Where-Object { $_.status -eq 'Applied' }).Count) applied, $(@($results | Where-Object { $_.status -eq 'AlreadySet' }).Count) already set" }
}

# ---- 08 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseApps {
  param($Paths, $State, $Options)
  foreach ($name in 'EnableStormApps', 'EnableOverlay', 'EnableBenchmarks') {
    $State.options[$name.Substring(0, 1).ToLowerInvariant() + $name.Substring(1)] = [bool](Get-StormOption $Options $State $name -Default $false)
  }
  $State.options.stormAppsInstaller = Get-StormOption $Options $State StormAppsInstaller
  if (-not $State.options.enableStormApps) {
    if ($State.options.enableOverlay -or $State.options.enableBenchmarks) {
      Write-StormLog -Level Warn -Phase '08' -Message '-EnableOverlay/-EnableBenchmarks have no effect without -EnableStormApps.'
    }
    return @{ Status = 'Skipped'; Message = 'Storm apps not requested (-EnableStormApps).' }
  }
  Assert-StormMounted -Paths $Paths -Phase '08'
  $installer = $State.options.stormAppsInstaller
  if (-not $installer -or -not (Test-Path -LiteralPath $installer) -or [System.IO.Path]::GetExtension($installer).ToLowerInvariant() -ne '.msi') {
    throw (New-StormError -Code Customization -Message "-EnableStormApps needs -StormAppsInstaller pointing to the STORM OS MSI (got '$installer')." -Fix 'Build it with ../scripts/publish.ps1 -Runtime win-x64 (artifacts/installer/StormOS-<version>-x64.msi) and pass its path.')
  }
  $folder = Join-Path $Paths.MountInstall 'Windows\Setup\Scripts\StormOS'
  New-Item -ItemType Directory -Path $folder -Force | Out-Null
  Copy-Item -LiteralPath $installer -Destination (Join-Path $folder 'StormOS.msi') -Force
  $setupComplete = Join-Path $Paths.MountInstall 'Windows\Setup\Scripts\SetupComplete.cmd'
  $block = Get-Content -LiteralPath (Join-Path $Paths.Config 'provisioning\SetupComplete.storm.cmd') -Raw
  if (Test-Path -LiteralPath $setupComplete) {
    $existing = Get-Content -LiteralPath $setupComplete -Raw
    if ($existing -notmatch 'STORM OS provisioning') { $block = $existing.TrimEnd() + "`r`n`r`n" + $block }
    else { $block = $existing }
  }
  [System.IO.File]::WriteAllText($setupComplete, ($block -replace "`r?`n", "`r`n"), [System.Text.Encoding]::ASCII)
  $defaults = [ordered]@{ overlay = [ordered]@{ enabled = [bool]$State.options.enableOverlay }; benchmarks = [ordered]@{ enabled = [bool]$State.options.enableBenchmarks } }
  $defaultsPath = Join-Path $Paths.MountInstall 'ProgramData\StormOS\Config\storm-defaults.json'
  New-Item -ItemType Directory -Path (Split-Path -Parent $defaultsPath) -Force | Out-Null
  [System.IO.File]::WriteAllText($defaultsPath, ($defaults | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
  $State.applications = @([ordered]@{
      name        = 'STORM OS app, StormOSService and storm CLI'
      source      = (Resolve-Path -LiteralPath $installer).Path
      sha256      = Get-StormSha256 -Path $installer
      installedBy = 'Windows\Setup\Scripts\SetupComplete.cmd (runs once as SYSTEM after Windows Setup, before the first sign-in)'
    })
  return @{ Status = 'Completed'; Message = 'STORM OS MSI staged for first-boot installation' }
}

# ---- 09 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseOobe {
  param($Paths, $State, $Options)
  Assert-StormMounted -Paths $Paths -Phase '09'
  $branding = Get-StormBrandingConfig -Paths $Paths
  $oobeXml = New-StormOobeXml -Branding $branding
  $oobePath = Join-Path $Paths.MountInstall 'Windows\System32\oobe\info\oobe.xml'
  New-Item -ItemType Directory -Path (Split-Path -Parent $oobePath) -Force | Out-Null
  [System.IO.File]::WriteAllText($oobePath, $oobeXml, (New-Object System.Text.UTF8Encoding($false)))
  $unattend = New-StormUnattendXml -Branding $branding -Architecture $State.edition.architecture
  $violations = Test-StormUnattendSafety -XmlText $unattend
  if ($violations.Count) { throw (New-StormError -Code Customization -Message "Generated answer file is not allowed: $($violations -join '; ')" -Fix 'Fix New-StormUnattendXml; answer files may only contain appearance defaults.') }
  $unattendPath = Join-Path $Paths.MountInstall 'Windows\Panther\unattend.xml'
  New-Item -ItemType Directory -Path (Split-Path -Parent $unattendPath) -Force | Out-Null
  [System.IO.File]::WriteAllText($unattendPath, $unattend, (New-Object System.Text.UTF8Encoding($false)))
  Write-StormLog -Phase '09' -Message 'Wrote Windows\System32\oobe\info\oobe.xml and Windows\Panther\unattend.xml (appearance only)'
}

# ---- 10 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseWinPe {
  param($Paths, $State, $Options)
  return @{ Status = 'NotImplemented'; Message = 'Storm WinPE setup is development phase 6 (docs/STATUS.md). The ISO boots the unmodified Windows Setup from the source media.' }
}

# ---- 11 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseInstaller {
  param($Paths, $State, $Options)
  $log = Get-StormDismLog $Paths
  $working = $State.artifacts.workingWim
  if (-not $working) { throw (New-StormError -Code Installer -Message 'No working image recorded.' -Fix 'Run the build from phase 04.') }
  Dismount-StormImage -MountPath $Paths.MountInstall -Save -LogPath $log -Phase '11'
  $sources = Join-Path $Paths.Iso 'sources'
  foreach ($name in 'install.wim', 'install.esd') {
    $existing = Join-Path $sources $name
    if (Test-Path -LiteralPath $existing) { Remove-Item -LiteralPath $existing -Force }
  }
  $final = Join-Path $sources 'install.wim'
  Write-StormLog -Phase '11' -Message 'Exporting the committed image into the installer media (recompression)...'
  try {
    Export-WindowsImage -SourceImagePath $working -SourceIndex 1 -DestinationImagePath $final -CompressionType Max -CheckIntegrity -LogPath $log | Out-Null
  }
  catch {
    throw (New-StormError -Code Installer -Message "Exporting the final image failed: $($_.Exception.Message)" -Fix 'Check free space; the committed working image is kept in build/temp for inspection.' -InnerException $_.Exception)
  }
  $image = Get-WindowsImage -ImagePath $final -Index 1
  $media = [ordered]@{ product = 'STORM OS'; stormVersion = $State.buildVersion; buildId = $State.buildId; edition = $image.ImageName; architecture = ConvertTo-StormArchitecture $image.Architecture; windowsVersion = [string]$image.Version }
  $mediaInfo = Join-Path $Paths.Iso 'StormOS\storm-media.json'
  New-Item -ItemType Directory -Path (Split-Path -Parent $mediaInfo) -Force | Out-Null
  [System.IO.File]::WriteAllText($mediaInfo, ($media | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
  $State.artifacts.installWim = [ordered]@{ path = $final; sha256 = Get-StormSha256 -Path $final; sizeBytes = (Get-Item -LiteralPath $final).Length }
  Remove-Item -LiteralPath $working -Force
  $State.artifacts.workingWim = $null
  return @{ Status = 'Completed'; Message = "install.wim $(Format-StormBytes $State.artifacts.installWim.sizeBytes)" }
}

# ---- 12 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseIso {
  param($Paths, $State, $Options)
  $oscdimg = Find-StormOscdimg
  if (-not $oscdimg) { throw (New-StormError -Code Iso -Message 'oscdimg.exe was not found.' -Fix 'Install the Windows ADK "Deployment Tools" feature.') }
  if ($Paths.Iso -match '\s') { throw (New-StormError -Code Iso -Message "The media path contains spaces: $($Paths.Iso)" -Fix 'Move the storm-os folder to a path without spaces.') }
  if (-not (Test-Path $Paths.Output)) { New-Item -ItemType Directory -Path $Paths.Output -Force | Out-Null }
  $State.options.isoName = [string](Get-StormOption $Options $State IsoName -Default 'StormOS.iso')
  $State.options.noBootPrompt = [bool](Get-StormOption $Options $State NoBootPrompt -Default $false)
  $isoName = $State.options.isoName
  $isoPath = Join-Path $Paths.Output $isoName
  if (Test-Path -LiteralPath $isoPath) { Remove-Item -LiteralPath $isoPath -Force }
  $label = New-StormIsoLabel -BuildVersion $State.buildVersion
  $arguments = New-StormOscdimgArguments -MediaRoot $Paths.Iso -IsoPath $isoPath -Label $label -NoBootPrompt:([bool]$State.options.noBootPrompt)
  Write-StormLog -Phase '12' -Message "Building $isoName ($label)..."
  Invoke-StormOscdimg -Oscdimg $oscdimg -Arguments $arguments
  $hash = Write-StormHashFile -Path $isoPath
  $State.artifacts.iso = [ordered]@{ path = $isoPath; sha256 = $hash; sizeBytes = (Get-Item -LiteralPath $isoPath).Length; label = $label; bootPrompt = -not [bool]$State.options.noBootPrompt }
  Write-StormLog -Level Success -Phase '12' -Message "$isoPath ($(Format-StormBytes $State.artifacts.iso.sizeBytes)) SHA-256 $hash"
}

# ---- 13 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseValidate {
  param($Paths, $State, $Options)
  $branding = Get-StormBrandingConfig -Paths $Paths
  $isoPath = if ($State.artifacts.iso) { $State.artifacts.iso.path } else { Join-Path $Paths.Output 'StormOS.iso' }
  $checks = @()
  $checks += Test-StormIsoArtifacts -IsoPath $isoPath -ExpectedLabel (New-StormIsoLabel -BuildVersion $State.buildVersion)
  $checks += Test-StormMediaImages -MediaRoot $Paths.Iso -Edition $State.edition
  $expected = Get-StormExpectedImageFiles -Branding $branding -StormApps ([bool]$State.options.enableStormApps)
  $checks += Test-StormImageContents -WimPath (Join-Path $Paths.Iso 'sources\install.wim') -MountPath $Paths.MountVerify -ExpectedFiles $expected -LogPath (Get-StormDismLog $Paths)
  $customizationFailures = @($State.customizations | Where-Object { $_.status -notin 'Applied', 'AlreadySet', 'NotApplicable' })
  $checks += New-StormValidation -Name 'Customizations verified' -Passed ($customizationFailures.Count -eq 0) -Detail "$(@($State.customizations).Count) registry values written and read back"
  $State.validation = @($checks | ForEach-Object { ConvertTo-StormHashtable $_ })
  foreach ($check in $checks) {
    Write-StormLog -Phase '13' -Level $(if ($check.status -eq 'PASS') { 'Success' } elseif ($check.critical) { 'Error' } else { 'Warn' }) -Message ('{0,-5} {1,-28} {2}' -f $check.status, $check.name, $check.detail)
  }
  $summary = Get-StormValidationSummary -Checks $checks
  if ($summary -ne 'SUCCESS') {
    throw (New-StormError -Code Validation -Message 'Critical validation checks failed.' -Fix 'See the FAIL lines above; the report is in build/output/storm-build-report.json.')
  }
}

# ---- 14 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseVmTest {
  param($Paths, $State, $Options)
  $run = [bool](Get-StormOption $Options $State RunVmTest -Default $false)
  if (-not $run) { return @{ Status = 'Skipped'; Message = 'VM boot test not requested (-RunVmTest).' } }
  $iso = $State.artifacts.iso.path
  $hypervisor = [string](Get-StormOption $Options $State Hypervisor -Default 'Qemu')
  $minutes = [int](Get-StormOption $Options $State VmBootMinutes -Default 10)
  $out = Join-Path $Paths.Artifacts 'vm'
  $result = if ($hypervisor -eq 'HyperV') { Invoke-StormHyperVBootTest -IsoPath $iso -OutputDirectory $out -BootMinutes $minutes } else { Invoke-StormQemuBootTest -IsoPath $iso -OutputDirectory $out -BootMinutes $minutes }
  $State.vmTest = ConvertTo-StormHashtable $result
  if (-not $result.passed) { throw (New-StormError -Code VmTest -Message "VM boot test failed: $($result.detail)" -Fix "Inspect the screenshots in $out.") }
  return @{ Status = 'Completed'; Message = $result.detail }
}

# ---- 15 ------------------------------------------------------------------------------------------------------------
function Invoke-StormPhaseCleanup {
  param($Paths, $State, $Options)
  if (Test-StormIsWindows) {
    Dismount-StormAllHives -Phase '15'
    $mounted = @(Get-StormMountedImages | Where-Object { $_.Path -like "$($Paths.Mounts)*" })
    if ($mounted.Count) { throw (New-StormError -Code Rollback -Message 'Images are still mounted; cleanup will not delete mount folders.' -Fix 'Run scripts/rollback.ps1.') }
  }
  foreach ($item in @(Get-ChildItem -LiteralPath $Paths.Temp -Force -ErrorAction SilentlyContinue)) {
    if ($item.FullName -ne $Paths.State) { Remove-Item -LiteralPath $item.FullName -Recurse -Force }
  }
  if ([bool](Get-StormOption $Options $State RemoveMedia -Default $false) -and (Test-Path -LiteralPath $Paths.Iso)) {
    Get-ChildItem -LiteralPath $Paths.Iso -Force | Remove-Item -Recurse -Force
  }
  Write-StormLog -Phase '15' -Message 'Temporary files removed; logs, output and source are kept.'
}

# ---- rollback ------------------------------------------------------------------------------------------------------
function Invoke-StormRollback {
  <#
    Returns the machine to a clean state after an interrupted build: unloads offline hives, discards every image
    mounted under build/mounts, cleans corrupt mount points and dismounts the source ISO. Never deletes the source,
    the logs or the output.
  #>
  param([Parameter(Mandatory)] $Paths, $State)
  $actions = New-Object System.Collections.Generic.List[string]
  if (-not (Test-StormIsWindows)) { return @('Nothing to roll back on this platform.') }
  foreach ($hive in Get-StormLoadedHives) { $actions.Add("unload HKLM\$hive") }
  Dismount-StormAllHives -Phase 'rollback'
  $log = Get-StormDismLog $Paths
  foreach ($mount in @(Get-StormMountedImages | Where-Object { $_.Path -like "$($Paths.Mounts)*" })) {
    Write-StormLog -Level Warn -Phase 'rollback' -Message "Discarding mounted image at $($mount.Path) ($($mount.MountStatus))"
    try { Dismount-WindowsImage -Path $mount.Path -Discard -LogPath $log | Out-Null; $actions.Add("discard $($mount.Path)") }
    catch { Write-StormLog -Level Warn -Phase 'rollback' -Message "Discard failed: $($_.Exception.Message)" }
  }
  Clear-WindowsCorruptMountPoint -LogPath $log | Out-Null
  $actions.Add('clean corrupt mount points')
  if ($State -and $State.source -and $State.source.type -eq 'ISO' -and $State.source.path -and (Test-Path -LiteralPath $State.source.path)) {
    $disk = Get-DiskImage -ImagePath (Resolve-Path -LiteralPath $State.source.path).Path -ErrorAction SilentlyContinue
    if ($disk -and $disk.Attached) { Dismount-DiskImage -ImagePath $disk.ImagePath | Out-Null; $actions.Add('dismount source ISO') }
  }
  return $actions.ToArray()
}
