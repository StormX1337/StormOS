# Build report: build/output/storm-build-report.json, written after every build attempt (success or failure).

function New-StormBuildReport {
  <#
    Builds the report from the build state. Result: SUCCESS only if validation passed and no phase failed; FAILED if a
    phase or validation failed; INCOMPLETE while phases are run one by one and validation has not run yet.
  #>
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $State, [string] $FailedPhase, [string] $FailureMessage, [string] $FailureFix)
  $started = [datetime]::Parse($State.startedAt, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
  $finished = (Get-Date).ToUniversalTime()
  $validation = @($State.validation | Where-Object { $null -ne $_ })
  $failedPhases = @($State.phases.Keys | Where-Object { $State.phases[$_].status -eq 'Failed' })
  $result = if ($FailedPhase -or $failedPhases.Count) { 'FAILED' } elseif ($validation.Count -eq 0) { 'INCOMPLETE' } else { Get-StormValidationSummary -Checks $validation }
  $customizations = @($State.customizations)
  return [ordered]@{
    schema          = 1
    product         = 'STORM OS'
    statement       = 'STORM OS is a Storm customization and deployment environment built on Windows.'
    stormVersion    = $State.buildVersion
    buildId         = $State.buildId
    result          = $result
    startedAt       = $State.startedAt
    finishedAt      = $finished.ToString('o')
    durationSeconds = [math]::Round(($finished - $started.ToUniversalTime()).TotalSeconds, 1)
    windowsSource   = [ordered]@{
      type         = $State.source.type
      path         = $State.source.path
      sha256       = $State.source.sha256
      editionIndex = $State.edition.index
      edition      = $State.edition.name
      editionId    = $State.edition.editionId
      architecture = $State.edition.architecture
      version      = $State.edition.version
      languages    = $State.edition.languages
    }
    options         = $State.options
    phases          = $State.phases
    applications    = @($State.applications)
    gamingFeatures  = [ordered]@{
      gamingDefaults = [bool]$State.options.enableGamingDefaults
      overlay        = [bool]$State.options.enableOverlay
      benchmarks     = [bool]$State.options.enableBenchmarks
      note           = 'Overlay and benchmark flags are defaults for the Storm apps; they have an effect only when Storm apps are installed.'
    }
    customizations  = [ordered]@{
      applied       = @($customizations | Where-Object { $_.status -eq 'Applied' }).Count
      alreadySet    = @($customizations | Where-Object { $_.status -eq 'AlreadySet' }).Count
      notApplicable = @($customizations | Where-Object { $_.status -eq 'NotApplicable' }).Count
      items         = $customizations
    }
    validation      = $validation
    vmTest          = $State.vmTest
    hashes          = $State.artifacts
    failure         = if ($FailedPhase) { [ordered]@{ phase = $FailedPhase; message = $FailureMessage; fix = $FailureFix } } else { $null }
  }
}

function Save-StormBuildReport {
  param([Parameter(Mandatory)] [System.Collections.IDictionary] $Paths, [Parameter(Mandatory)] [System.Collections.IDictionary] $Report)
  if (-not (Test-Path $Paths.Output)) { New-Item -ItemType Directory -Path $Paths.Output -Force | Out-Null }
  $path = Join-Path $Paths.Output 'storm-build-report.json'
  [System.IO.File]::WriteAllText($path, ($Report | ConvertTo-Json -Depth 12), (New-Object System.Text.UTF8Encoding($false)))
  return $path
}
