<#
.SYNOPSIS
  Publishes STORM OS (app, service, CLI) for one architecture and builds the MSI.

.EXAMPLE
  ./scripts/publish.ps1 -Runtime win-x64 -Version 1.2.0
  ./scripts/publish.ps1 -Runtime win-arm64 -Version 1.3.0-beta.1

.NOTES
  Code signing is optional and uses signtool with a certificate from the machine store:
  set SIGN_CERT_THUMBPRINT (and optionally SIGN_TIMESTAMP_URL) before running.
  Unsigned builds are fine for development; release builds should be signed because the
  service trusts clients by install location and, when the service is signed, by signer.
#>
[CmdletBinding()]
param(
  [ValidateSet('win-x64', 'win-arm64')] [string] $Runtime = 'win-x64',
  [string] $Version = '1.0.0',
  [string] $Configuration = 'Release',
  [string] $Output = 'artifacts',
  [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
  if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$') { throw "Version must be semantic (1.2.3 or 1.2.3-beta.1): $Version" }
  $msiVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3])"
  $platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }
  $publish = Join-Path $root "$Output/publish/$Runtime"
  if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

  $projects = @(
    @{ Path = 'src/StormOS.App/StormOS.App.csproj'; Extra = @("-p:Platform=$platform") },
    @{ Path = 'src/StormOS.Service/StormOS.Service.csproj'; Extra = @() },
    @{ Path = 'src/StormOS.Cli/StormOS.Cli.csproj'; Extra = @() }
  )
  foreach ($project in $projects) {
    Write-Host "Publishing $($project.Path) ($Runtime)" -ForegroundColor Cyan
    dotnet publish $project.Path -c $Configuration -r $Runtime --self-contained true "-p:Version=$Version" -o $publish @($project.Extra)
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($project.Path)" }
  }

  # One shared configuration file for app, service and CLI (they read different sections).
  Copy-Item (Join-Path $root 'installer/appsettings.json') (Join-Path $publish 'appsettings.json') -Force
  Get-ChildItem $publish -Filter 'appsettings.Development.json' | Remove-Item -Force
  Get-ChildItem $publish -Recurse -Filter '*.pdb' | Remove-Item -Force

  if ($env:SIGN_CERT_THUMBPRINT) {
    $timestamp = if ($env:SIGN_TIMESTAMP_URL) { $env:SIGN_TIMESTAMP_URL } else { 'http://timestamp.digicert.com' }
    $targets = Get-ChildItem $publish -Include 'StormOS.exe', 'StormOS.Service.exe', 'storm.exe', 'StormOS*.dll' -Recurse
    & signtool sign /sha1 $env:SIGN_CERT_THUMBPRINT /fd sha256 /tr $timestamp /td sha256 @($targets.FullName)
    if ($LASTEXITCODE -ne 0) { throw 'Code signing failed.' }
  }

  if ($SkipInstaller) { return }

  $installerOut = Join-Path $root "$Output/installer"
  dotnet build (Join-Path $root 'installer/StormOS.Installer.wixproj') -c $Configuration "-p:Platform=$($platform.ToLowerInvariant())" "-p:ProductVersion=$msiVersion" "-p:PublishDir=$publish\" -o $installerOut
  if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

  $msi = Get-ChildItem $installerOut -Filter "StormOS-$msiVersion-*.msi" | Select-Object -First 1
  if ($env:SIGN_CERT_THUMBPRINT) {
    & signtool sign /sha1 $env:SIGN_CERT_THUMBPRINT /fd sha256 /tr $timestamp /td sha256 $msi.FullName
    if ($LASTEXITCODE -ne 0) { throw 'MSI signing failed.' }
  }

  $hash = (Get-FileHash $msi.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  Set-Content -Path "$($msi.FullName).sha256" -Value "$hash  $($msi.Name)" -NoNewline
  Write-Host "Built $($msi.Name) ($([math]::Round($msi.Length / 1MB, 1)) MB) SHA-256 $hash" -ForegroundColor Green
}
finally {
  Pop-Location
}
