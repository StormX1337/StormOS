<#
.SYNOPSIS
  TEST ONLY: runs at the first sign-in of the STORM OS test VM and opens screens for screenshots.

.DESCRIPTION
  Started by the test answer file (autounattend.xml). For each screen it writes "STORM-TOUR:<name>" to COM1; the host
  (Invoke-StormVmInstallTest.ps1) reads the VM's serial output and captures the display. The script also writes what
  it finds (theme, OEM information, Storm files) to COM1 so the host log documents the installed state.
#>
$ErrorActionPreference = 'Continue'

function Send-Serial([string] $Line) {
  try {
    $port = New-Object System.IO.Ports.SerialPort 'COM1', 115200
    $port.Open()
    $port.Write("`r`n$Line`r`n")   # leading newline: firmware output on the same port may not end with one
    $port.Close()
  }
  catch { }
}

function Show-Screen([string] $Name, [scriptblock] $Open, [int] $Settle = 25) {
  & $Open
  Start-Sleep -Seconds $Settle
  Send-Serial "STORM-TOUR:$Name"
  Start-Sleep -Seconds 20   # the host captures the display
}

function Close-Settings { Get-Process SystemSettings -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Seconds 3 }

Send-Serial 'STORM-TOUR-STATUS:signed-in'
Start-Sleep -Seconds 75   # first-sign-in animation, taskbar and Start settle

$oem = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation' -ErrorAction SilentlyContinue
$theme = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes' -ErrorAction SilentlyContinue
$personalize = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -ErrorAction SilentlyContinue
$wallpaper = (Get-ItemProperty 'HKCU:\Control Panel\Desktop' -ErrorAction SilentlyContinue).WallPaper
Send-Serial "STORM-TOUR-STATUS:oem=$($oem.Manufacturer)|$($oem.Model)"
Send-Serial "STORM-TOUR-STATUS:theme=$($theme.CurrentTheme)"
Send-Serial "STORM-TOUR-STATUS:wallpaper=$wallpaper"
Send-Serial "STORM-TOUR-STATUS:apps-light=$($personalize.AppsUseLightTheme) system-light=$($personalize.SystemUsesLightTheme)"
Send-Serial "STORM-TOUR-STATUS:game-mode=$((Get-ItemProperty 'HKCU:\Software\Microsoft\GameBar' -ErrorAction SilentlyContinue).AutoGameModeEnabled)"
Send-Serial "STORM-TOUR-STATUS:storm-image=$(Test-Path 'C:\ProgramData\StormOS\Config\storm-image.json')"

Send-Serial 'STORM-TOUR:desktop'
Start-Sleep -Seconds 45   # run 6: the host captured the desktop late (Start was already open); give it more time

$shell = New-Object -ComObject WScript.Shell
Show-Screen 'start-menu' { $shell.SendKeys('^{ESC}') } 8
$shell.SendKeys('{ESC}')
Start-Sleep -Seconds 3

Show-Screen 'settings-about' { Start-Process 'ms-settings:about' }
Close-Settings
Show-Screen 'settings-personalization' { Start-Process 'ms-settings:personalization' }
Close-Settings
Show-Screen 'settings-themes' { Start-Process 'ms-settings:themes' }
Close-Settings
Show-Screen 'settings-gaming' { Start-Process 'ms-settings:gaming-gamemode' }
Close-Settings
Show-Screen 'file-explorer' { Start-Process explorer.exe 'C:\ProgramData\StormOS' }
(New-Object -ComObject Shell.Application).Windows() | ForEach-Object { $_.Quit() }
Start-Sleep -Seconds 3

Send-Serial 'STORM-TOUR:done'
