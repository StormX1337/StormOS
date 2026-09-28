<#
.SYNOPSIS
  Downloads official Windows 11 Enterprise evaluation media from Microsoft for CI pipeline validation.

.DESCRIPTION
  CI needs a real, legitimate Windows 11 image to exercise the STORM OS build pipeline end to end. This script
  downloads the Windows 11 Enterprise *evaluation* ISO from Microsoft's Evaluation Center (or a URL passed with
  -Url / the STORM_SOURCE_ISO_URL variable), verifies it looks like an ISO and writes its SHA-256.

  The evaluation media is used only to validate the pipeline. Neither it nor the StormOS.iso built from it is
  published: CI uploads reports, logs and screenshots only (docs/LICENSING.md).

.EXAMPLE
  .\tests\ci\Get-StormEvaluationIso.ps1 -Destination D:\storm-src\source.iso
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $Destination,
  [string] $Url,
  [string] $EvaluationPage = 'https://www.microsoft.com/en-us/evalcenter/download-windows-11-enterprise'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Resolve-FinalUrl([string] $Candidate) {
  $request = [System.Net.HttpWebRequest]::Create($Candidate)
  $request.Method = 'HEAD'
  $request.AllowAutoRedirect = $true
  $request.UserAgent = 'Mozilla/5.0 (StormOS CI)'
  $response = $request.GetResponse()
  try { return [pscustomobject]@{ Url = $response.ResponseUri.AbsoluteUri; Length = $response.ContentLength } }
  finally { $response.Dispose() }
}

$candidates = @()
if ($Url) { $candidates += $Url }
else {
  # The Evaluation Center sometimes answers "Service unavailable / The request is blocked" for a few minutes.
  $page = $null
  foreach ($wait in 0, 30, 60, 120, 180, 300) {
    if ($wait) { Write-Host "  retrying in $wait s"; Start-Sleep -Seconds $wait }
    Write-Host "Reading $EvaluationPage"
    try { $page = Invoke-WebRequest -Uri $EvaluationPage -UseBasicParsing -UserAgent 'Mozilla/5.0 (StormOS CI)'; break }
    catch { Write-Host "  $($_.Exception.Message)" }
  }
  if (-not $page) { throw "The Evaluation Center page could not be read. Retry later or set the STORM_SOURCE_ISO_URL repository variable to an official Microsoft download URL." }
  $links = [regex]::Matches($page.Content, 'https://go\.microsoft\.com/fwlink/p?/?\?[^"''<>\s]*') | ForEach-Object { $_.Value.Replace('&amp;', '&') } | Select-Object -Unique
  $candidates = @($links | Where-Object { $_ -match 'culture=en-us' -and $_ -match 'country=us' })
  Write-Host "Found $($candidates.Count) en-US download links"
}

$selected = $null
foreach ($candidate in $candidates) {
  try {
    $final = Resolve-FinalUrl $candidate
    Write-Host "  $candidate -> $($final.Url) ($([math]::Round($final.Length / 1GB, 2)) GB)"
    if ($final.Url -match '\.iso$' -and $final.Length -gt 3GB -and ($Url -or ($final.Url -match 'ENTERPRISEEVAL' -and $final.Url -match 'x64' -and $final.Url -notmatch 'LTSC'))) { $selected = $final; break }
  }
  catch { Write-Host "  $candidate -> $($_.Exception.Message)" }
}
if (-not $selected) { throw 'No Windows 11 Enterprise evaluation ISO (x64, en-US) link was found. Set the STORM_SOURCE_ISO_URL repository variable to an official Microsoft download URL.' }

New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
Write-Host "Downloading $($selected.Url)"
$client = New-Object System.Net.WebClient
$client.Headers.Add('User-Agent', 'Mozilla/5.0 (StormOS CI)')
$client.DownloadFile($selected.Url, $Destination)

$stream = [System.IO.File]::OpenRead($Destination)
try {
  $buffer = New-Object byte[] 5
  [void]$stream.Seek(0x8001, 'Begin')
  [void]$stream.Read($buffer, 0, 5)
  if ([System.Text.Encoding]::ASCII.GetString($buffer) -ne 'CD001') { throw 'The download is not an ISO 9660 image.' }
}
finally { $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Downloaded $Destination ($([math]::Round((Get-Item $Destination).Length / 1GB, 2)) GB) SHA-256 $hash"
"$hash  $(Split-Path -Leaf $selected.Url)" | Set-Content -Path "$Destination.sha256" -Encoding ASCII
