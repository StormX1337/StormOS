<#
.SYNOPSIS
  Builds StormOS.iso from a Windows 11 source in one run.

.DESCRIPTION
  Runs phases 00-15: prerequisites, workspace, source import, edition selection, export + mount, Storm branding,
  Storm defaults, optional gaming defaults and Storm apps, OOBE configuration, (Storm WinPE: not implemented),
  commit + installer media, ISO, validation, optional VM boot test and cleanup.

  On failure the build stops safely: it records the failing phase, discards mounted images, unloads offline hives,
  dismounts the source ISO, keeps all logs, writes build/output/storm-build-report.json with the error and the
  suggested fix, and exits with the phase's exit code (docs/BUILD.md). The source image is never modified.

  BUILD SUCCESS is only printed when every critical validation check passed.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\build-all.ps1 -SourceIso "C:\StormOS\source.iso" -EditionIndex 6

.EXAMPLE
  .\scripts\build-all.ps1 -SourceIso "C:\ISO\Windows11.iso" -EditionIndex 6 -BuildVersion "1.0.0" `
    -EnableGamingDefaults -EnableStormApps -StormAppsInstaller "..\artifacts\installer\StormOS-1.0.0-x64.msi" `
    -EnableOverlay -EnableBenchmarks -CleanBuild
#>
[CmdletBinding()]
param(
  [string] $SourceIso,
  [string] $SourceWim,
  [string] $SourceMediaDirectory,
  [int] $EditionIndex,
  [string] $OutputDirectory,
  [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')] [string] $BuildVersion = '1.0.0',
  [switch] $EnableGamingDefaults,
  [switch] $EnableStormApps,
  [string] $StormAppsInstaller,
  [switch] $EnableOverlay,
  [switch] $EnableBenchmarks,
  [switch] $CleanBuild,
  [string] $IsoName = 'StormOS.iso',
  [switch] $NoBootPrompt,
  [switch] $RunVmTest,
  [ValidateSet('Qemu', 'HyperV')] [string] $Hypervisor = 'Qemu',
  [int] $VmBootMinutes = 10,
  [switch] $KeepTemp
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\StormBuild.psm1') -Force

$options = ConvertTo-StormOptions -BoundParameters $PSBoundParameters
if (-not $options.ContainsKey('BuildVersion')) { $options['BuildVersion'] = $BuildVersion }
if (-not $options.ContainsKey('IsoName')) { $options['IsoName'] = $IsoName }
$paths = Get-StormPaths -OutputDirectory $OutputDirectory
$state = New-StormBuildState -BuildVersion $BuildVersion
Initialize-StormLog -LogDirectory $paths.Logs -BuildId $state.buildId
Write-StormLog -Message "STORM OS $BuildVersion build $($state.buildId) - STORM OS is a Storm customization and deployment environment built on Windows."

$failed = $null
$watchAll = [System.Diagnostics.Stopwatch]::StartNew()
foreach ($phase in Get-StormPhaseTable) {
  if ($phase.Id -eq '15' -and $KeepTemp) { Set-StormPhaseResult -State $state -Phase '15' -Status Skipped -Message '-KeepTemp'; continue }
  Write-StormPhaseHeader -Phase $phase.Id -Title $phase.Title
  $watch = [System.Diagnostics.Stopwatch]::StartNew()
  try {
    $result = & $phase.Function -Paths $paths -State $state -Options $options
    $status = 'Completed'; $message = ''
    if ($result -is [System.Collections.IDictionary] -and $result.Contains('Status')) { $status = $result.Status; $message = [string]$result.Message }
    Set-StormPhaseResult -State $state -Phase $phase.Id -Status $status -Message $message -DurationSeconds $watch.Elapsed.TotalSeconds
    if ($phase.Id -ne '00') { Save-StormState -Paths $paths -State $state }
    Write-StormLog -Level $(if ($status -eq 'Completed') { 'Success' } else { 'Warn' }) -Phase $phase.Id -Message "$status ($([math]::Round($watch.Elapsed.TotalSeconds, 1)) s)$(if ($message) { ": $message" })"
  }
  catch {
    $info = Get-StormErrorInfo $_
    Set-StormPhaseResult -State $state -Phase $phase.Id -Status Failed -Message $info.Message -DurationSeconds $watch.Elapsed.TotalSeconds
    $failed = [pscustomobject]@{ Phase = $phase.Id; Title = $phase.Title; Info = $info }
    Write-StormLog -Level Error -Phase $phase.Id -Message "PHASE FAILED: $($info.Message)"
    break
  }
}

if ($failed) {
  Write-StormLog -Level Warn -Phase 'rollback' -Message 'Stopping safely: discarding mounted images and unloading hives (source, logs and output are kept).'
  try {
    foreach ($action in (Invoke-StormRollback -Paths $paths -State $state)) { Write-StormLog -Phase 'rollback' -Message $action }
  }
  catch {
    Write-StormLog -Level Error -Phase 'rollback' -Message "Automatic cleanup incomplete: $($_.Exception.Message). Run scripts/rollback.ps1."
  }
}

if (Test-Path -LiteralPath $paths.Temp) { Save-StormState -Paths $paths -State $state }
$report = if ($failed) { New-StormBuildReport -State $state -FailedPhase "$($failed.Phase) $($failed.Title)" -FailureMessage $failed.Info.Message -FailureFix $failed.Info.Fix } else { New-StormBuildReport -State $state }
$reportPath = Save-StormBuildReport -Paths $paths -Report $report

Write-Host ''
if ($report.result -eq 'SUCCESS') {
  Write-StormLog -Level Success -Message ('=' * 72)
  Write-StormLog -Level Success -Message 'BUILD SUCCESS'
  Write-StormLog -Level Success -Message "ISO:     $($state.artifacts.iso.path)"
  Write-StormLog -Level Success -Message "SHA-256: $($state.artifacts.iso.sha256)"
  Write-StormLog -Level Success -Message "Report:  $reportPath"
  Write-StormLog -Level Success -Message "Time:    $([math]::Round($watchAll.Elapsed.TotalMinutes, 1)) min"
  Write-StormLog -Level Success -Message ('=' * 72)
  exit 0
}

$code = if ($failed) { $failed.Info.Code } else { Get-StormExitCode -Name Validation }
Write-StormLog -Level Error -Message ('=' * 72)
Write-StormLog -Level Error -Message 'BUILD FAILED'
if ($failed) {
  Write-StormLog -Level Error -Message "Phase:   $($failed.Phase) $($failed.Title)"
  Write-StormLog -Level Error -Message "Error:   $($failed.Info.Message)"
  Write-StormLog -Level Warn -Message "Fix:     $($failed.Info.Fix)"
}
Write-StormLog -Level Error -Message "Report:  $reportPath"
Write-StormLog -Level Error -Message "Log:     $(Get-StormLogPath)"
Write-StormLog -Level Error -Message ('=' * 72)
exit $code
