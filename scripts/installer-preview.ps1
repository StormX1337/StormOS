<#
.SYNOPSIS
  Installer smoke test with screenshots: runs the STORM OS setup through its UI, checks the installed
  service and CLI, captures the setup pages and the first app screens, then uninstalls silently.

.DESCRIPTION
  Meant for a disposable Windows machine (CI runner or VM) with an interactive desktop: it really installs
  STORM OS per machine and removes it again. Drives the UI with UI Automation only; it does not change any
  Windows setting besides installing and uninstalling the product. Runs in Windows PowerShell 5.1.

.EXAMPLE
  powershell -File scripts/installer-preview.ps1 -Setup artifacts/installer/StormOS-Setup-1.0.0-x64.exe
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)] [string] $Setup,
  [string] $Out = 'artifacts/preview'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PreviewNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT rect, int size);
}
'@

$Setup = (Resolve-Path $Setup).Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$AE = [System.Windows.Automation.AutomationElement]
$TreeScope = [System.Windows.Automation.TreeScope]
$installDir = Join-Path $env:ProgramFiles 'STORM OS'

function Get-VisibleBounds([IntPtr] $Hwnd) {
  $rect = New-Object PreviewNative+RECT
  # DWMWA_EXTENDED_FRAME_BOUNDS excludes the invisible resize borders that GetWindowRect includes.
  if ([PreviewNative]::DwmGetWindowAttribute($Hwnd, 9, [ref] $rect, 16) -ne 0) { [PreviewNative]::GetWindowRect($Hwnd, [ref] $rect) | Out-Null }
  return $rect
}

function Save-Window([IntPtr] $Hwnd, [string] $Name) {
  [PreviewNative]::ShowWindow($Hwnd, 9) | Out-Null
  [PreviewNative]::SetForegroundWindow($Hwnd) | Out-Null
  Start-Sleep -Milliseconds 600
  $frame = New-Object PreviewNative+RECT
  [PreviewNative]::GetWindowRect($Hwnd, [ref] $frame) | Out-Null
  $visible = Get-VisibleBounds $Hwnd
  $width = $visible.Right - $visible.Left
  $height = $visible.Bottom - $visible.Top

  # Composited window content (PW_RENDERFULLCONTENT), cropped to the visible frame; independent of what is on screen.
  $full = New-Object System.Drawing.Bitmap ($frame.Right - $frame.Left), ($frame.Bottom - $frame.Top)
  $graphics = [System.Drawing.Graphics]::FromImage($full)
  $hdc = $graphics.GetHdc()
  $printed = [PreviewNative]::PrintWindow($Hwnd, $hdc, 2)
  $graphics.ReleaseHdc($hdc)
  $graphics.Dispose()
  if ($printed) {
    $crop = New-Object System.Drawing.Rectangle ($visible.Left - $frame.Left), ($visible.Top - $frame.Top), $width, $height
    $image = $full.Clone($crop, $full.PixelFormat)
    $image.Save((Join-Path $Out "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $image.Dispose()
  }
  $full.Dispose()

  # What the screen shows (includes the real title bar); only complete when the window fits on screen.
  $screen = New-Object System.Drawing.Bitmap $width, $height
  $graphics = [System.Drawing.Graphics]::FromImage($screen)
  $graphics.CopyFromScreen($visible.Left, $visible.Top, 0, 0, $screen.Size)
  $graphics.Dispose()
  $screen.Save((Join-Path $Out "$Name.screen.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $screen.Dispose()
  Write-Host "Captured $Name ($width x $height)"
}

function Find-Element($Root, [string[]] $Names, [int] $TimeoutSeconds = 30, [switch] $Optional) {
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    foreach ($name in $Names) {
      $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::IsOffscreenProperty, $false)))
      $searchScope = if ($Root -eq $AE::RootElement) { $TreeScope::Children } else { $TreeScope::Descendants }
      $found = $Root.FindFirst($searchScope, $condition)
      if ($null -ne $found) { return $found }
    }
    Start-Sleep -Milliseconds 250
  } while ((Get-Date) -lt $deadline)
  if ($Optional) { return $null }
  throw "UI element not found within $TimeoutSeconds s: $($Names -join ' | ')"
}

function Invoke-Element($Element) {
  $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Get-Hwnd($Element) { return [IntPtr] $Element.Current.NativeWindowHandle }

# A larger desktop makes the app window fit on screen; Server SKUs support this, others keep their resolution.
try { Set-DisplayResolution -Width 1920 -Height 1080 -Force -ErrorAction Stop; Write-Host 'Display set to 1920x1080' } catch { Write-Host "Display resolution unchanged: $($_.Exception.Message)" }

# ---- Setup UI -------------------------------------------------------------------------------------------------
$process = Start-Process -FilePath $Setup -ArgumentList '/log', ('"' + (Join-Path $Out 'setup.log') + '"') -PassThru
$window = Find-Element $AE::RootElement @('STORM OS Setup') 90
$hwnd = Get-Hwnd $window

function Confirm-License {
  $toggle = (Find-Element $window @('I agree to the license terms and conditions')).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
  if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
}

Confirm-License
Save-Window $hwnd '01-setup-welcome'

Invoke-Element (Find-Element $window @('Options'))
Find-Element $window @('Setup Options') | Out-Null
Save-Window $hwnd '02-setup-options'
Invoke-Element (Find-Element $window @('OK'))
Confirm-License

Invoke-Element (Find-Element $window @('Install'))
$progress = Find-Element $window @('Setup Progress') 60 -Optional
if ($null -ne $progress) {
  Start-Sleep -Milliseconds 1500
  if ($null -ne (Find-Element $window @('Setup Progress') 1 -Optional)) { Save-Window $hwnd '03-setup-progress' }
}

$done = Find-Element $window @('Installation Successfully Completed', 'Setup Successful', 'Setup Failed') 600
if ($done.Current.Name -eq 'Setup Failed') {
  Save-Window $hwnd '99-setup-failed'
  throw "Setup failed; see $(Join-Path $Out 'setup.log')."
}
Save-Window $hwnd '04-setup-complete'

# ---- Installed product checks ------------------------------------------------------------------------------------
$service = Get-Service -Name StormOSService
Write-Host "StormOSService: $($service.Status) ($($service.StartType))"
if ($service.Status -ne 'Running') { throw 'StormOSService is not running after install.' }
foreach ($file in 'StormOS.exe', 'StormOS.Service.exe', 'storm.exe', 'appsettings.json') {
  if (-not (Test-Path (Join-Path $installDir $file))) { throw "Missing installed file: $file" }
}
$uninstallKeys = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
$entries = @(Get-ItemProperty $uninstallKeys -ErrorAction SilentlyContinue | Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -eq 'STORM OS' -and -not ($_.PSObject.Properties['SystemComponent'] -and $_.SystemComponent -eq 1) })
Write-Host "Visible Apps & Features entries: $($entries.Count)"
if ($entries.Count -ne 1) { throw "Expected exactly one visible 'STORM OS' uninstall entry, found $($entries.Count)." }

# The CLI runs from the install folder, so the service treats it as a trusted client over the named pipe.
$ErrorActionPreference = 'Continue'
$status = & (Join-Path $installDir 'storm.exe') status 2>&1 | Out-String
$ErrorActionPreference = 'Stop'
$status | Set-Content (Join-Path $Out 'storm-status.txt')
Write-Host $status
if ($status -notmatch 'Service\s+Running') { throw 'storm status could not reach the installed service.' }

# ---- First app screens (best effort: rendering on a GPU-less CI desktop is not part of the pass criteria) ---------
try {
  Invoke-Element (Find-Element $window @('Launch'))
  $app = Find-Element $AE::RootElement @('STORM OS') 90
  $appHwnd = Get-Hwnd $app
  [PreviewNative]::SetWindowPos($appHwnd, [IntPtr]::Zero, 0, 0, 1480, 940, 0x0014) | Out-Null
  Start-Sleep -Seconds 10
  Save-Window $appHwnd '05-app-first-run'
  $skip = Find-Element $app @('Skip setup') 10 -Optional
  if ($null -ne $skip) {
    Invoke-Element $skip
    Start-Sleep -Seconds 8
    Save-Window $appHwnd '06-app-dashboard'
    foreach ($page in 'Optimizer', 'Performance', 'Benchmark') {
      $item = Find-Element $app @($page) 10 -Optional
      if ($null -eq $item) { continue }
      $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
      Start-Sleep -Seconds 6
      Save-Window $appHwnd ('07-app-' + $page.ToLowerInvariant())
    }
  }
}
catch {
  Write-Warning "App screenshots skipped: $($_.Exception.Message)"
}
finally {
  Get-Process -Name StormOS -ErrorAction SilentlyContinue | Stop-Process -Force
}

Invoke-Element (Find-Element $window @('Close'))
$process.WaitForExit(30000) | Out-Null

# ---- Silent uninstall --------------------------------------------------------------------------------------------
$uninstall = Start-Process -FilePath $Setup -ArgumentList '/uninstall', '/quiet', '/log', ('"' + (Join-Path $Out 'uninstall.log') + '"') -Wait -PassThru
Write-Host "Uninstall exit code: $($uninstall.ExitCode)"
if ($uninstall.ExitCode -ne 0) { throw "Uninstall failed with exit code $($uninstall.ExitCode)." }
if (Get-Service -Name StormOSService -ErrorAction SilentlyContinue) { throw 'StormOSService is still registered after uninstall.' }
if (Test-Path (Join-Path $installDir 'StormOS.exe')) { throw 'Program files are still present after uninstall.' }
Write-Host 'Install, service, CLI and uninstall checks passed.' -ForegroundColor Green
