# Declarative registry defaults for the offline image.
#
# Every value STORM OS writes into the image is declared in config/*/*.json with a reason, risk, category,
# applicable editions and how the user changes it back. The engine refuses anything that touches security,
# activation, update or setup-bypass locations (see $script:StormRegistryDenyList), so "random registry tweaks"
# cannot slip in through configuration.

$script:StormRegistryCategories = @('POWER', 'GRAPHICS', 'GAMING', 'BACKGROUND', 'STARTUP', 'NETWORK', 'STORAGE', 'SYSTEM', 'BRANDING')
$script:StormRegistryRisks = @('None', 'Low')          # image defaults are conservative: Medium/High are rejected
$script:StormRegistryHives = @('Software', 'DefaultUser')
$script:StormRegistryTypes = @('REG_SZ', 'REG_EXPAND_SZ', 'REG_DWORD')
$script:StormEditionFamilies = @('*', 'Home', 'Pro', 'Education', 'Enterprise', 'Other')

# Key path fragments (regex, case-insensitive) STORM OS must never write: security products, UAC, update,
# activation, Secure Boot / code integrity, firewall, SmartScreen, setup hardware-requirement bypasses,
# OOBE network bypass, automatic logon, credentials and service start types.
$script:StormRegistryDenyList = @(
  'Windows Defender', 'Microsoft Antimalware', 'Advanced Threat Protection', 'Windows Security',
  'CurrentVersion\\Policies\\System', 'WindowsUpdate', 'UpdateOrchestrator', 'WaaSMedic',
  'SoftwareProtectionPlatform', 'SecureBoot', 'DeviceGuard', 'HypervisorEnforcedCodeIntegrity', 'CI\\Policy',
  'FirewallPolicy', 'WindowsFirewall', 'SmartScreen', 'Setup\\LabConfig', 'Setup\\MoSetup',
  'CurrentVersion\\OOBE', 'Winlogon', 'Control\\Lsa', '\\SAM\\', 'CurrentControlSet\\Services', 'ControlSet\d+\\Services'
)
$script:StormRegistryDenyValues = @(
  'EnableLUA', 'ConsentPromptBehaviorAdmin', 'ConsentPromptBehaviorUser', 'PromptOnSecureDesktop', 'DisableAntiSpyware',
  'DisableRealtimeMonitoring', 'BypassTPMCheck', 'BypassSecureBootCheck', 'BypassRAMCheck', 'BypassStorageCheck',
  'BypassCPUCheck', 'BypassNRO', 'AllowUpgradesWithUnsupportedTPMOrCPU', 'AutoAdminLogon', 'DefaultPassword', 'NoAutoUpdate',
  'EnableSmartScreen', 'ShellSmartScreenLevel', 'SmartScreenEnabled', 'EnableFirewall', 'DisableAntiVirus'
)

function Test-StormRegistryPathAllowed {
  <# Returns $null when allowed, otherwise the reason it is blocked. #>
  param([Parameter(Mandatory)] [string] $Key, [string] $ValueName = '')
  foreach ($pattern in $script:StormRegistryDenyList) {
    if ($Key -match $pattern) { return "Key '$Key' is in a protected area ($pattern). STORM OS never changes security, update, activation or setup-requirement settings." }
  }
  if ($ValueName -and ($script:StormRegistryDenyValues -contains $ValueName)) {
    return "Value '$ValueName' controls a protected security or setup behavior."
  }
  return $null
}

function Test-StormRegistrySetting {
  <# Validates one declared setting. Returns an array of error strings (empty = valid). #>
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $Setting)
  $errors = New-Object System.Collections.Generic.List[string]
  foreach ($field in 'id', 'name', 'description', 'reason', 'category', 'risk', 'hive', 'key', 'valueName', 'type', 'value', 'editions', 'userChange') {
    if (-not $Setting.Contains($field) -or $null -eq $Setting[$field] -or ([string]$Setting[$field]) -eq '') { $errors.Add("missing '$field'") }
  }
  if ($errors.Count) { return $errors.ToArray() }
  $id = [string]$Setting.id
  if ($id -notmatch '^[a-z0-9]+(\.[a-z0-9-]+)+$') { $errors.Add("id '$id' must be lowercase dotted (e.g. gaming.game-mode)") }
  if ($script:StormRegistryCategories -notcontains $Setting.category) { $errors.Add("$id category '$($Setting.category)' is not one of $($script:StormRegistryCategories -join ', ')") }
  if ($script:StormRegistryRisks -notcontains $Setting.risk) { $errors.Add("$id risk '$($Setting.risk)' is not allowed for image defaults (only None/Low)") }
  if ($script:StormRegistryHives -notcontains $Setting.hive) { $errors.Add("$id hive '$($Setting.hive)' must be Software or DefaultUser") }
  if ($script:StormRegistryTypes -notcontains $Setting.type) { $errors.Add("$id type '$($Setting.type)' must be REG_SZ, REG_EXPAND_SZ or REG_DWORD") }
  if ([string]$Setting.key -match '^(HKLM|HKCU|HKEY_)' -or [string]$Setting.key -match '^\\') { $errors.Add("$id key must be relative to the hive (no HKLM/HKCU prefix)") }
  if ($Setting.type -eq 'REG_DWORD') {
    $number = 0L
    if (-not [long]::TryParse([string]$Setting.value, [ref]$number) -or $number -lt 0 -or $number -gt 4294967295) { $errors.Add("$id REG_DWORD value '$($Setting.value)' must be 0..4294967295") }
  }
  foreach ($edition in @($Setting.editions)) {
    if ($script:StormEditionFamilies -notcontains $edition) { $errors.Add("$id edition '$edition' is not one of $($script:StormEditionFamilies -join ', ')") }
  }
  $blocked = Test-StormRegistryPathAllowed -Key ([string]$Setting.key) -ValueName ([string]$Setting.valueName)
  if ($blocked) { $errors.Add("$id $blocked") }
  return $errors.ToArray()
}

function Import-StormRegistryConfig {
  <# Loads and validates a registry defaults file; any invalid entry fails the whole file. #>
  param([Parameter(Mandatory)] [string] $Path)
  $config = Read-StormJson -Path $Path
  if (-not $config.Contains('settings')) {
    throw (New-StormError -Code Customization -Message "$Path has no 'settings' array." -Fix 'See config/registry/README.md for the format.')
  }
  $settings = @($config.settings)
  $problems = @()
  $ids = @{}
  foreach ($setting in $settings) {
    $problems += Test-StormRegistrySetting -Setting $setting
    if ($setting.Contains('id')) {
      if ($ids.ContainsKey($setting.id)) { $problems += "duplicate id '$($setting.id)'" }
      $ids[$setting.id] = $true
    }
  }
  if ($problems.Count) {
    throw (New-StormError -Code Customization -Message "Invalid registry defaults in ${Path}: $($problems -join '; ')" -Fix 'Fix the entries; tests/build/Registry.Tests.ps1 validates all configuration files.')
  }
  return $settings
}

function Test-StormSettingApplies {
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $Setting, [Parameter(Mandatory)] [string] $EditionFamily)
  $editions = @($Setting.editions)
  return ($editions -contains '*') -or ($editions -contains $EditionFamily)
}

function ConvertTo-StormRegData {
  <# Canonical data string for reg.exe and for before/after comparison (DWORDs as decimal). #>
  param([Parameter(Mandatory)] [string] $Type, [Parameter(Mandatory)] $Value)
  if ($Type -eq 'REG_DWORD') { return ([long]$Value).ToString([System.Globalization.CultureInfo]::InvariantCulture) }
  return [string]$Value
}

function New-StormRegAddArguments {
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $Setting, [Parameter(Mandatory)] [string] $HiveRoot)
  $key = '{0}\{1}' -f $HiveRoot, $Setting.key
  return @('add', $key, '/v', [string]$Setting.valueName, '/t', [string]$Setting.type, '/d', (ConvertTo-StormRegData -Type $Setting.type -Value $Setting.value), '/f')
}

function ConvertFrom-StormRegQuery {
  <# Parses "reg query <key> /v <name>" output. Returns @{ Type; Data } with DWORDs as decimal, or $null. #>
  param([string[]] $Lines, [Parameter(Mandatory)] [string] $ValueName)
  foreach ($line in $Lines) {
    if ($line -match '^\s+(?<name>.+?)\s{4}(?<type>REG_[A-Z_]+)(?:\s{4}(?<data>.*))?$' -and $Matches['name'] -eq $ValueName) {
      $type = $Matches['type']
      $data = if ($Matches.ContainsKey('data')) { $Matches['data'] } else { '' }
      if ($type -eq 'REG_DWORD' -and $data -match '^0x([0-9a-fA-F]+)$') {
        $data = [Convert]::ToUInt32($Matches[1], 16).ToString([System.Globalization.CultureInfo]::InvariantCulture)
      }
      return @{ Type = $type; Data = $data }
    }
  }
  return $null
}

function Read-StormRegistryValue {
  param([Parameter(Mandatory)] [string] $Key, [Parameter(Mandatory)] [string] $ValueName)
  $previous = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & reg.exe query $Key /v $ValueName 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ConvertFrom-StormRegQuery -Lines $output -ValueName $ValueName
  }
  finally {
    $ErrorActionPreference = $previous
  }
}

function Invoke-StormRegistrySettings {
  <#
    Applies declared settings to the mounted image and verifies each write by reading it back. Returns one result
    per setting (Applied, AlreadySet, NotApplicable) with the previous value, so the build report documents exactly
    what differs from the source image.
  #>
  param(
    [Parameter(Mandatory)] [string] $MountPath,
    [Parameter(Mandatory)] [object[]] $Settings,
    [Parameter(Mandatory)] [string] $EditionFamily,
    [Parameter(Mandatory)] [string] $Source,
    [string] $Phase = ''
  )
  Assert-StormWindows -Operation 'Editing offline registry hives'
  $results = New-Object System.Collections.Generic.List[object]
  $needed = @($Settings | Where-Object { Test-StormSettingApplies -Setting $_ -EditionFamily $EditionFamily } | ForEach-Object { $_.hive } | Select-Object -Unique)
  $roots = @{}
  try {
    foreach ($hive in $needed) { $roots[$hive] = Mount-StormHive -MountPath $MountPath -Hive $hive -Phase $Phase }
    foreach ($setting in $Settings) {
      $result = [ordered]@{ id = $setting.id; source = $Source; category = $setting.category; risk = $setting.risk; hive = $setting.hive; key = $setting.key; valueName = $setting.valueName; target = ConvertTo-StormRegData -Type $setting.type -Value $setting.value; before = $null; after = $null; status = '' }
      if (-not (Test-StormSettingApplies -Setting $setting -EditionFamily $EditionFamily)) {
        $result.status = 'NotApplicable'
        Write-StormLog -Level Detail -Phase $Phase -Message "skip  $($setting.id) (not applicable to $EditionFamily)"
        $results.Add([pscustomobject]$result)
        continue
      }
      $fullKey = '{0}\{1}' -f $roots[$setting.hive], $setting.key
      $before = Read-StormRegistryValue -Key $fullKey -ValueName $setting.valueName
      $result.before = if ($before) { $before.Data } else { $null }
      if ($before -and $before.Type -eq $setting.type -and $before.Data -eq $result.target) {
        $result.after = $before.Data
        $result.status = 'AlreadySet'
        Write-StormLog -Level Detail -Phase $Phase -Message "ok    $($setting.id) already $($result.target)"
      }
      else {
        Invoke-StormNative -Phase $Phase -FilePath 'reg.exe' -Arguments (New-StormRegAddArguments -Setting $setting -HiveRoot $roots[$setting.hive]) -ErrorCode Customization -Fix "Check config entry $($setting.id)." | Out-Null
        $after = Read-StormRegistryValue -Key $fullKey -ValueName $setting.valueName
        if (-not $after -or $after.Data -ne $result.target) {
          throw (New-StormError -Code Customization -Message "Verification failed for $($setting.id): expected '$($result.target)', read '$(if ($after) { $after.Data } else { '<missing>' })'." -Fix 'Run scripts/rollback.ps1 and retry; check for tools holding the offline hive open.')
        }
        $result.after = $after.Data
        $result.status = 'Applied'
        Write-StormLog -Phase $Phase -Message "set   $($setting.id): $(if ($null -eq $result.before) { '<unset>' } else { $result.before }) -> $($result.after)"
      }
      $results.Add([pscustomobject]$result)
    }
  }
  finally {
    foreach ($hive in $roots.Keys) { Dismount-StormHive -Hive $hive -Phase $Phase }
  }
  return $results.ToArray()
}
