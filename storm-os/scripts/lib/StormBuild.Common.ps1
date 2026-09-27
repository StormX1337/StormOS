# Common infrastructure: exit codes, errors with fixes, logging, paths, build state, native process helpers.

$script:StormExitCodes = [ordered]@{
  Success          = 0
  Unexpected       = 1
  Prerequisites    = 10
  Workspace        = 15
  Source           = 20
  EditionSelection = 21
  Mount            = 30
  Customization    = 40
  Installer        = 45
  Iso              = 50
  Validation       = 60
  VmTest           = 70
  Rollback         = 80
}

$script:StormLog = @{ TextPath = $null; JsonPath = $null; Quiet = $false; BuildId = $null }

function Get-StormExitCode {
  <# Returns the numeric exit code for a named failure class (see docs/BUILD.md). #>
  param([Parameter(Mandatory)] [ValidateScript({ $script:StormExitCodes.Contains($_) })] [string] $Name)
  return [int]$script:StormExitCodes[$Name]
}

function Get-StormExitCodeTable {
  return $script:StormExitCodes
}

function New-StormError {
  <#
    Creates an exception that carries an exit code and a suggested fix. Phase scripts throw these; build-all and
    the phase wrappers print the fix and exit with the code.
  #>
  param(
    [Parameter(Mandatory)] [string] $Code,
    [Parameter(Mandatory)] [string] $Message,
    [Parameter(Mandatory)] [string] $Fix,
    [System.Exception] $InnerException
  )
  $exception = if ($InnerException) { New-Object System.InvalidOperationException($Message, $InnerException) } else { New-Object System.InvalidOperationException($Message) }
  $exception.Data['StormExitCode'] = Get-StormExitCode -Name $Code
  $exception.Data['StormFix'] = $Fix
  return $exception
}

function Get-StormErrorInfo {
  <# Extracts code, message and fix from any error record; unknown errors map to "Unexpected". #>
  param([Parameter(Mandatory)] $ErrorRecord)
  $exception = if ($ErrorRecord -is [System.Management.Automation.ErrorRecord]) { $ErrorRecord.Exception } else { $ErrorRecord }
  $current = $exception
  while ($null -ne $current) {
    if ($current.Data -and $current.Data.Contains('StormExitCode')) {
      return [pscustomobject]@{ Code = [int]$current.Data['StormExitCode']; Message = $current.Message; Fix = [string]$current.Data['StormFix'] }
    }
    $current = $current.InnerException
  }
  return [pscustomobject]@{
    Code    = Get-StormExitCode -Name Unexpected
    Message = $exception.Message
    Fix     = 'Check the build log in build/logs for details, run scripts/rollback.ps1 to clean up, then retry.'
  }
}

function Test-StormIsWindows {
  return [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
}

function Assert-StormWindows {
  param([string] $Operation = 'This operation')
  if (-not (Test-StormIsWindows)) {
    throw (New-StormError -Code Prerequisites -Message "$Operation requires Windows (DISM, reg.exe and oscdimg are Windows tools)." -Fix 'Run the STORM OS build on a Windows 10 2004+ or Windows 11 machine.')
  }
}

function Test-StormAdministrator {
  if (-not (Test-StormIsWindows)) { return $false }
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-StormRoot {
  return $script:StormRoot
}

function Join-StormPath {
  <# Path.Combine: pure string joining (Join-Path fails for drives that do not exist on the current machine). #>
  param([Parameter(Mandatory)] [string] $Base, [Parameter(Mandatory)] [string] $Child)
  return [System.IO.Path]::Combine($Base, $Child)
}

function Get-StormPaths {
  <# Returns the workspace layout. OutputDirectory defaults to build/output. #>
  param([string] $Root = $script:StormRoot, [string] $OutputDirectory)
  $build = Join-StormPath $Root 'build'
  $output = if ($OutputDirectory) { $OutputDirectory } else { Join-StormPath $build 'output' }
  return [ordered]@{
    Root         = $Root
    Build        = $build
    Input        = Join-StormPath $build 'input'
    Output       = $output
    Iso          = Join-StormPath $build 'iso'
    Mounts       = Join-StormPath $build 'mounts'
    MountInstall = Join-StormPath (Join-StormPath $build 'mounts') 'install'
    MountBoot    = Join-StormPath (Join-StormPath $build 'mounts') 'boot'
    MountVerify  = Join-StormPath (Join-StormPath $build 'mounts') 'verify'
    Temp         = Join-StormPath $build 'temp'
    Logs         = Join-StormPath $build 'logs'
    Artifacts    = Join-StormPath $build 'artifacts'
    State        = Join-StormPath (Join-StormPath $build 'temp') 'build-state.json'
    Config       = Join-StormPath $Root 'config'
    Branding     = Join-StormPath $Root 'branding'
  }
}

function Initialize-StormLog {
  <# Starts (or continues) the build log: a readable text log plus a JSON-lines log for tooling. #>
  param([Parameter(Mandatory)] [string] $LogDirectory, [Parameter(Mandatory)] [string] $BuildId, [switch] $Quiet)
  if (-not (Test-Path $LogDirectory)) { New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null }
  $script:StormLog.TextPath = Join-Path $LogDirectory "build-$BuildId.log"
  $script:StormLog.JsonPath = Join-Path $LogDirectory "build-$BuildId.jsonl"
  $script:StormLog.BuildId = $BuildId
  $script:StormLog.Quiet = [bool]$Quiet
}

function Get-StormLogPath {
  return $script:StormLog.TextPath
}

function Write-StormLog {
  <#
    Writes one structured log entry. Messages and data never contain secrets: the build handles no credentials,
    and values from configuration are registry defaults and file paths only.
  #>
  param(
    [ValidateSet('Info', 'Warn', 'Error', 'Success', 'Detail')] [string] $Level = 'Info',
    [string] $Phase = '',
    [Parameter(Mandatory)] [string] $Message,
    [System.Collections.IDictionary] $Data
  )
  $timestamp = (Get-Date).ToUniversalTime().ToString('o')
  if (-not $script:StormLog.Quiet) {
    $color = switch ($Level) { 'Warn' { 'Yellow' } 'Error' { 'Red' } 'Success' { 'Green' } 'Detail' { 'DarkGray' } default { 'Gray' } }
    $prefix = if ($Phase) { "[$Phase] " } else { '' }
    Write-Host "$prefix$Message" -ForegroundColor $color
  }
  if ($script:StormLog.TextPath) {
    $line = '{0} {1,-7} {2}{3}' -f $timestamp, $Level.ToUpperInvariant(), $(if ($Phase) { "[$Phase] " } else { '' }), $Message
    Add-Content -Path $script:StormLog.TextPath -Value $line -Encoding UTF8
    $entry = [ordered]@{ time = $timestamp; level = $Level; phase = $Phase; message = $Message; build = $script:StormLog.BuildId }
    if ($Data) { $entry['data'] = $Data }
    Add-Content -Path $script:StormLog.JsonPath -Value ($entry | ConvertTo-Json -Compress -Depth 8) -Encoding UTF8
  }
}

function Write-StormPhaseHeader {
  param([Parameter(Mandatory)] [string] $Phase, [Parameter(Mandatory)] [string] $Title)
  if (-not $script:StormLog.Quiet) {
    Write-Host ''
    Write-Host ('=' * 72) -ForegroundColor DarkCyan
    Write-Host " STORM OS BUILD  |  $Phase  |  $Title" -ForegroundColor Cyan
    Write-Host ('=' * 72) -ForegroundColor DarkCyan
  }
  Write-StormLog -Phase $Phase -Message "Phase started: $Title"
}

function ConvertTo-StormHashtable {
  <# Converts ConvertFrom-Json output (PSCustomObject graphs) into hashtables/arrays; PS 5.1 has no -AsHashtable. #>
  param($InputObject)
  if ($null -eq $InputObject) { return $null }
  if ($InputObject -is [System.Collections.IDictionary]) {
    $table = [ordered]@{}
    foreach ($key in $InputObject.Keys) { $table[$key] = ConvertTo-StormHashtable $InputObject[$key] }
    return $table
  }
  if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
    $table = [ordered]@{}
    foreach ($property in $InputObject.PSObject.Properties) { $table[$property.Name] = ConvertTo-StormHashtable $property.Value }
    return $table
  }
  if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
    $list = New-Object System.Collections.ArrayList
    foreach ($item in $InputObject) { [void]$list.Add((ConvertTo-StormHashtable $item)) }
    return , $list.ToArray()
  }
  return $InputObject
}

function Read-StormJson {
  param([Parameter(Mandatory)] [string] $Path)
  if (-not (Test-Path -LiteralPath $Path)) {
    throw (New-StormError -Code Customization -Message "Configuration file not found: $Path" -Fix 'Restore the file from the repository (git checkout -- <path>).')
  }
  try {
    return ConvertTo-StormHashtable (Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json)
  }
  catch {
    throw (New-StormError -Code Customization -Message "Configuration file is not valid JSON: $Path ($($_.Exception.Message))" -Fix 'Fix the JSON syntax; tests/build validates every configuration file.')
  }
}

function New-StormBuildState {
  param([Parameter(Mandatory)] [string] $BuildVersion)
  $id = '{0}-{1}' -f (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 6))
  return [ordered]@{
    schema        = 1
    buildId       = $id
    buildVersion  = $BuildVersion
    startedAt     = (Get-Date).ToUniversalTime().ToString('o')
    source        = [ordered]@{}
    edition       = [ordered]@{}
    options       = [ordered]@{}
    phases        = [ordered]@{}
    customizations = @()
    applications  = @()
    artifacts     = [ordered]@{}
    validation    = @()
    vmTest        = $null
  }
}

function Read-StormState {
  <# Loads build/temp/build-state.json; phase scripts run standalone read the previous phases' results from it. #>
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $Paths, [switch] $AllowMissing)
  if (-not (Test-Path -LiteralPath $Paths.State)) {
    if ($AllowMissing) { return $null }
    throw (New-StormError -Code Workspace -Message 'No build state found. The workspace has not been prepared.' -Fix 'Run scripts/01-prepare-workspace.ps1 (or scripts/build-all.ps1) first.')
  }
  return Read-StormJson -Path $Paths.State
}

function Save-StormState {
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $Paths, [Parameter(Mandatory)] [System.Collections.IDictionary] $State)
  $directory = Split-Path -Parent $Paths.State
  if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
  $json = $State | ConvertTo-Json -Depth 12
  [System.IO.File]::WriteAllText($Paths.State, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Set-StormPhaseResult {
  <# Records a phase outcome in the build state: Completed, Skipped, NotImplemented or Failed. #>
  param(
    [Parameter(Mandatory)] [System.Collections.IDictionary] $State,
    [Parameter(Mandatory)] [string] $Phase,
    [Parameter(Mandatory)] [ValidateSet('Completed', 'Skipped', 'NotImplemented', 'Failed')] [string] $Status,
    [string] $Message = '',
    [double] $DurationSeconds = 0
  )
  $State.phases[$Phase] = [ordered]@{
    status          = $Status
    message         = $Message
    durationSeconds = [math]::Round($DurationSeconds, 1)
    finishedAt      = (Get-Date).ToUniversalTime().ToString('o')
  }
}

function Invoke-StormNative {
  <#
    Runs a native tool with an argument array (never a concatenated command line from user input), logs the call
    and throws a Storm error on unexpected exit codes. Returns the combined output lines.
  #>
  param(
    [Parameter(Mandatory)] [string] $FilePath,
    [string[]] $Arguments = @(),
    [int[]] $SuccessCodes = @(0),
    [Parameter(Mandatory)] [string] $ErrorCode,
    [Parameter(Mandatory)] [string] $Fix,
    [string] $Phase = ''
  )
  Write-StormLog -Level Detail -Phase $Phase -Message ("> {0} {1}" -f (Split-Path -Leaf $FilePath), ($Arguments -join ' '))
  $previous = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $FilePath @Arguments 2>&1 | ForEach-Object { "$_" }
    $exitCode = $LASTEXITCODE
  }
  finally {
    $ErrorActionPreference = $previous
  }
  if ($SuccessCodes -notcontains $exitCode) {
    $tail = ($output | Select-Object -Last 15) -join [Environment]::NewLine
    Write-StormLog -Level Error -Phase $Phase -Message "$(Split-Path -Leaf $FilePath) failed with exit code $exitCode" -Data @{ output = $tail }
    throw (New-StormError -Code $ErrorCode -Message "$(Split-Path -Leaf $FilePath) failed with exit code ${exitCode}: $tail" -Fix $Fix)
  }
  return $output
}

function Format-StormBytes {
  param([double] $Bytes)
  if ($Bytes -ge 1TB) { return '{0:N1} TB' -f ($Bytes / 1TB) }
  if ($Bytes -ge 1GB) { return '{0:N1} GB' -f ($Bytes / 1GB) }
  if ($Bytes -ge 1MB) { return '{0:N1} MB' -f ($Bytes / 1MB) }
  return '{0:N0} bytes' -f $Bytes
}

function Get-StormSha256 {
  param([Parameter(Mandatory)] [string] $Path)
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-StormHashFile {
  <# Writes "<hash>  <file name>" next to the file, the format sha256sum -c understands. #>
  param([Parameter(Mandatory)] [string] $Path)
  $hash = Get-StormSha256 -Path $Path
  [System.IO.File]::WriteAllText("$Path.sha256", "$hash  $(Split-Path -Leaf $Path)`n", (New-Object System.Text.UTF8Encoding($false)))
  return $hash
}

function Test-StormDirectoryEmpty {
  param([Parameter(Mandatory)] [string] $Path)
  if (-not (Test-Path -LiteralPath $Path)) { return $true }
  return -not (Get-ChildItem -LiteralPath $Path -Force | Select-Object -First 1)
}

function ConvertTo-StormOptions {
  <# Converts a script's $PSBoundParameters into a plain hashtable (switches become booleans). #>
  param([Parameter(Mandatory)] [AllowEmptyCollection()] $BoundParameters)
  $options = @{}
  foreach ($key in @($BoundParameters.Keys)) {
    $value = $BoundParameters[$key]
    if ($value -is [System.Management.Automation.SwitchParameter]) { $value = $value.IsPresent }
    $options[$key] = $value
  }
  return $options
}

function Get-StormOption {
  <# Reads an option from the phase call, falling back to the options recorded in the build state. #>
  param([System.Collections.IDictionary] $Options, [System.Collections.IDictionary] $State, [Parameter(Mandatory)] [string] $Name, $Default = $null)
  if ($Options -and $Options.Contains($Name) -and $null -ne $Options[$Name] -and "$($Options[$Name])" -ne '') { return $Options[$Name] }
  $key = $Name.Substring(0, 1).ToLowerInvariant() + $Name.Substring(1)
  if ($State -and $State.Contains('options') -and $State.options -and $State.options.Contains($key) -and $null -ne $State.options[$key]) { return $State.options[$key] }
  return $Default
}

function Invoke-StormPhase {
  <#
    Runs one phase function for the standalone phase scripts: loads paths, build state and log, runs the phase,
    records the result in build/temp/build-state.json and converts failures into "PHASE FAILED" output with the
    suggested fix. Returns the process exit code for the script.
  #>
  param(
    [Parameter(Mandatory)] [string] $Phase,
    [Parameter(Mandatory)] [string] $Title,
    [Parameter(Mandatory)] [string] $Function,
    [System.Collections.IDictionary] $Options = @{},
    [string] $OutputDirectory,
    [switch] $NoState,
    [switch] $NewState
  )
  $paths = Get-StormPaths -OutputDirectory $OutputDirectory
  $state = $null
  try {
    if ($NewState) { $state = New-StormBuildState -BuildVersion ([string](Get-StormOption -Options $Options -Name BuildVersion -Default '1.0.0')) }
    elseif (-not $NoState) { $state = Read-StormState -Paths $paths }
    if (-not $OutputDirectory -and $state -and $state.options -and $state.options.outputDirectory) { $paths = Get-StormPaths -OutputDirectory $state.options.outputDirectory }
  }
  catch {
    $info = Get-StormErrorInfo $_
    Write-Host "PHASE FAILED: $($info.Message)" -ForegroundColor Red
    Write-Host "Suggested fix: $($info.Fix)" -ForegroundColor Yellow
    return $info.Code
  }
  $buildId = if ($state) { $state.buildId } else { 'standalone' }
  Initialize-StormLog -LogDirectory $paths.Logs -BuildId $buildId
  Write-StormPhaseHeader -Phase $Phase -Title $Title
  $watch = [System.Diagnostics.Stopwatch]::StartNew()
  try {
    $result = & $Function -Paths $paths -State $state -Options $Options
    $status = 'Completed'
    $message = ''
    if ($result -is [System.Collections.IDictionary] -and $result.Contains('Status')) { $status = $result.Status; $message = [string]$result.Message }
    if ($state) {
      Set-StormPhaseResult -State $state -Phase $Phase -Status $status -Message $message -DurationSeconds $watch.Elapsed.TotalSeconds
      Save-StormState -Paths $paths -State $state
      Save-StormBuildReport -Paths $paths -Report (New-StormBuildReport -State $state) | Out-Null
    }
    Write-StormLog -Level Success -Phase $Phase -Message "Phase $status ($([math]::Round($watch.Elapsed.TotalSeconds, 1)) s)$(if ($message) { ": $message" })"
    return 0
  }
  catch {
    $info = Get-StormErrorInfo $_
    Write-StormLog -Level Error -Phase $Phase -Message "PHASE FAILED: $($info.Message)"
    Write-StormLog -Level Warn -Phase $Phase -Message "Suggested fix: $($info.Fix)"
    if ($state) {
      Set-StormPhaseResult -State $state -Phase $Phase -Status Failed -Message $info.Message -DurationSeconds $watch.Elapsed.TotalSeconds
      Save-StormState -Paths $paths -State $state
      Save-StormBuildReport -Paths $paths -Report (New-StormBuildReport -State $state -FailedPhase "$Phase $Title" -FailureMessage $info.Message -FailureFix $info.Fix) | Out-Null
    }
    return $info.Code
  }
}
