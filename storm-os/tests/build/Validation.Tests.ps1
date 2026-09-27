BeforeAll {
  Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\StormBuild.psm1') -Force
  $script:Branding = Read-StormJson -Path (Join-Path $PSScriptRoot '..\..\config\branding\branding.json')
}

Describe 'Validation summary' {
  It 'never reports success without checks' { Get-StormValidationSummary -Checks @() | Should -Be 'FAILED' }
  It 'fails on any critical failure' {
    Get-StormValidationSummary -Checks @((New-StormValidation -Name a -Passed $true -Detail d), (New-StormValidation -Name b -Passed $false -Detail d)) | Should -Be 'FAILED'
  }
  It 'tolerates non-critical failures' {
    Get-StormValidationSummary -Checks @((New-StormValidation -Name a -Passed $true -Detail d), (New-StormValidation -Name b -Passed $false -Detail d -Critical $false)) | Should -Be 'SUCCESS'
  }
  It 'expects Storm files, and app provisioning only when enabled' {
    $without = Get-StormExpectedImageFiles -Branding $Branding -StormApps $false
    $with = Get-StormExpectedImageFiles -Branding $Branding -StormApps $true
    $without | Should -Contain 'Windows\Panther\unattend.xml'
    $without | Should -Contain 'Windows\Web\Wallpaper\StormOS\storm-dark.jpg'
    $without | Should -Not -Contain 'Windows\Setup\Scripts\SetupComplete.cmd'
    $with | Should -Contain 'Windows\Setup\Scripts\StormOS\StormOS.msi'
  }
}

Describe 'Build report' {
  BeforeEach {
    $script:State = New-StormBuildState -BuildVersion '1.0.0'
    $State.source = [ordered]@{ type = 'ISO'; path = 'C:\w.iso'; sha256 = 'abc' }
    $State.edition = [ordered]@{ index = 6; name = 'Windows 11 Pro'; architecture = 'x64'; version = '10.0.26100.1'; languages = @('en-US') }
    $State.options = [ordered]@{ enableGamingDefaults = $true; enableOverlay = $false; enableBenchmarks = $false }
    $State.customizations = @(@{ id = 'a'; status = 'Applied' }, @{ id = 'b'; status = 'AlreadySet' })
  }
  It 'reports SUCCESS only with passing validation' {
    $State.validation = @((New-StormValidation -Name iso -Passed $true -Detail ok))
    $report = New-StormBuildReport -State $State
    $report.result | Should -Be 'SUCCESS'
    $report.customizations.applied | Should -Be 1
    $report.windowsSource.edition | Should -Be 'Windows 11 Pro'
    $report.statement | Should -Match 'built on Windows'
  }
  It 'reports FAILED with phase, message and fix' {
    $report = New-StormBuildReport -State $State -FailedPhase '04 Export and mount edition' -FailureMessage 'mount failed' -FailureFix 'run rollback'
    $report.result | Should -Be 'FAILED'
    $report.failure.fix | Should -Be 'run rollback'
  }
  It 'reports INCOMPLETE while validation has not run' {
    (New-StormBuildReport -State $State).result | Should -Be 'INCOMPLETE'
  }
  It 'reports FAILED when a phase failed even without validation' {
    Set-StormPhaseResult -State $State -Phase '04' -Status Failed -Message 'mount failed'
    (New-StormBuildReport -State $State).result | Should -Be 'FAILED'
  }
  It 'reports FAILED when a critical validation check failed' {
    $State.validation = @((New-StormValidation -Name iso -Passed $false -Detail missing))
    (New-StormBuildReport -State $State).result | Should -Be 'FAILED'
  }
  It 'serializes to JSON' {
    $State.validation = @((New-StormValidation -Name iso -Passed $true -Detail ok))
    { New-StormBuildReport -State $State | ConvertTo-Json -Depth 12 | ConvertFrom-Json } | Should -Not -Throw
  }
}
