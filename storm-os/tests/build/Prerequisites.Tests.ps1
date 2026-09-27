BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\StormBuild.psm1') -Force }

Describe 'Prerequisite evaluation' {
  It 'rejects hosts older than Windows 10 2004' {
    (Test-StormHostVersion -Version '10.0.18363').Status | Should -Be 'FAIL'
    (Test-StormHostVersion -Version '10.0.22631').Status | Should -Be 'PASS'
  }

  It 'grades free space' {
    (Test-StormFreeSpace -FreeBytes 20GB).Status | Should -Be 'FAIL'
    (Test-StormFreeSpace -FreeBytes 45GB).Status | Should -Be 'WARN'
    (Test-StormFreeSpace -FreeBytes 100GB).Status | Should -Be 'PASS'
  }

  It 'requires NTFS' {
    (Test-StormFileSystem -FileSystem 'NTFS').Status | Should -Be 'PASS'
    (Test-StormFileSystem -FileSystem 'exFAT').Status | Should -Be 'FAIL'
    (Test-StormFileSystem -FileSystem '').Status | Should -Be 'FAIL'
  }

  It 'fails missing mandatory tools and warns for optional ones, always with a fix' {
    $mandatory = Test-StormToolPresence -Name 'oscdimg' -Path '' -Fix 'install ADK'
    $optional = Test-StormToolPresence -Name 'WinPE' -Path '' -Fix 'install add-on' -Mandatory $false
    $mandatory.Status | Should -Be 'FAIL'
    $optional.Status | Should -Be 'WARN'
    $mandatory.Fix | Should -Not -BeNullOrEmpty
  }

  It 'validates the source type' {
    (Test-StormSourcePath -Path 'C:\x.iso' -Exists $true).Status | Should -Be 'PASS'
    (Test-StormSourcePath -Path 'C:\x.zip' -Exists $true).Status | Should -Be 'FAIL'
    (Test-StormSourcePath -Path 'C:\x.iso' -Exists $false).Status | Should -Be 'FAIL'
    (Test-StormSourcePath -Path '' -Exists $false).Status | Should -Be 'WARN'
  }

  It 'summarizes FAIL over WARN over PASS' {
    $pass = New-StormCheck -Name a -Status PASS -Detail d
    $warn = New-StormCheck -Name b -Status WARN -Detail d -Mandatory $false
    $fail = New-StormCheck -Name c -Status FAIL -Detail d
    Get-StormPrerequisiteSummary -Checks @($pass) | Should -Be 'PASS'
    Get-StormPrerequisiteSummary -Checks @($pass, $warn) | Should -Be 'WARN'
    Get-StormPrerequisiteSummary -Checks @($pass, $warn, $fail) | Should -Be 'FAIL'
  }

  It 'reports a non-Windows host as a failure instead of crashing' -Skip:([System.Environment]::OSVersion.Platform -eq 'Win32NT') {
    $checks = Get-StormPrerequisiteReport -Workspace $TestDrive
    Get-StormPrerequisiteSummary -Checks $checks | Should -Be 'FAIL'
  }
}
