<#
.SYNOPSIS
  Publishes STORM OS (app, service, CLI) for one architecture and builds the MSI and the setup executable.

.EXAMPLE
  ./scripts/publish.ps1 -Runtime win-x64 -Version 1.2.0
  ./scripts/publish.ps1 -Runtime win-arm64 -Version 1.3.0-beta.1

.NOTES
  Output: artifacts/installer/StormOS-<version>-<platform>.msi and StormOS-Setup-<version>-<platform>.exe (+ .sha256).
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

  dotnet build (Join-Path $root 'installer/bundle/StormOS.Bundle.wixproj') -c $Configuration "-p:Platform=$($platform.ToLowerInvariant())" "-p:ProductVersion=$msiVersion" "-p:MsiPath=$($msi.FullName)" -o $installerOut
  if ($LASTEXITCODE -ne 0) { throw 'Setup executable build failed.' }
  $setup = Get-ChildItem $installerOut -Filter "StormOS-Setup-$msiVersion-*.exe" | Select-Object -First 1

  if ($env:SIGN_CERT_THUMBPRINT) {
    # A Burn bundle is signed in two steps: first the engine inside it, then the bundle itself.
    $tools = Join-Path $root "$Output/tools"
    $wix = Join-Path $tools 'wix.exe'
    if (-not (Test-Path $wix)) {
      dotnet tool install wix --version 5.0.2 --tool-path $tools
      if ($LASTEXITCODE -ne 0) { throw 'Could not install the WiX command-line tool.' }
    }
    $engine = Join-Path $installerOut 'engine.exe'
    $signed = Join-Path $installerOut 'setup-signed.exe'
    & $wix burn detach $setup.FullName -engine $engine
    if ($LASTEXITCODE -ne 0) { throw 'Could not detach the setup engine.' }
    & signtool sign /sha1 $env:SIGN_CERT_THUMBPRINT /fd sha256 /tr $timestamp /td sha256 $engine
    if ($LASTEXITCODE -ne 0) { throw 'Setup engine signing failed.' }
    & $wix burn reattach $setup.FullName -engine $engine -o $signed
    if ($LASTEXITCODE -ne 0) { throw 'Could not reattach the signed setup engine.' }
    Move-Item $signed $setup.FullName -Force
    Remove-Item $engine
    & signtool sign /sha1 $env:SIGN_CERT_THUMBPRINT /fd sha256 /tr $timestamp /td sha256 $setup.FullName
    if ($LASTEXITCODE -ne 0) { throw 'Setup signing failed.' }
  }

  foreach ($file in @($msi, (Get-Item $setup.FullName))) {
    $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$($file.FullName).sha256" -Value "$hash  $($file.Name)" -NoNewline
    Write-Host "Built $($file.Name) ($([math]::Round($file.Length / 1MB, 1)) MB) SHA-256 $hash" -ForegroundColor Green
  }
}
finally {
  Pop-Location
}
