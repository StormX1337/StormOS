BeforeAll { Import-Module (Join-Path $PSScriptRoot '..\..\scripts\lib\StormBuild.psm1') -Force }

Describe 'Exit codes and errors' {
  It 'has unique exit codes' {
    $codes = @((Get-StormExitCodeTable).Values)
    ($codes | Select-Object -Unique).Count | Should -Be $codes.Count
  }

  It 'carries exit code and fix on Storm errors' {
    $info = Get-StormErrorInfo (New-StormError -Code Mount -Message 'boom' -Fix 'do this')
    $info.Code | Should -Be 30
    $info.Fix | Should -Be 'do this'
  }

  It 'finds the Storm error inside wrapped exceptions' {
    $inner = New-StormError -Code Iso -Message 'inner' -Fix 'fix it'
    $info = Get-StormErrorInfo (New-Object System.Exception('outer', $inner))
    $info.Code | Should -Be 50
  }

  It 'maps unknown errors to Unexpected with a generic fix' {
    $info = Get-StormErrorInfo (New-Object System.Exception('surprise'))
    $info.Code | Should -Be 1
    $info.Fix | Should -Match 'rollback'
  }
}

Describe 'Options and state' {
  It 'prefers explicit options over recorded state options' {
    $state = @{ options = [ordered]@{ editionIndex = 6 } }
    Get-StormOption -Options @{ EditionIndex = 3 } -State $state -Name EditionIndex | Should -Be 3
    Get-StormOption -Options @{} -State $state -Name EditionIndex | Should -Be 6
    Get-StormOption -Options @{} -State $state -Name Missing -Default 'x' | Should -Be 'x'
  }

  It 'converts bound switches to booleans' {
    $options = ConvertTo-StormOptions -BoundParameters @{ CleanBuild = [switch]$true; EditionIndex = 6 }
    $options.CleanBuild | Should -BeOfType [bool]
    $options.CleanBuild | Should -BeTrue
  }

  It 'round-trips build state through JSON' {
    $state = New-StormBuildState -BuildVersion '1.0.0'
    $state.customizations = @(@{ id = 'a'; status = 'Applied' })
    $copy = ConvertTo-StormHashtable (($state | ConvertTo-Json -Depth 10) | ConvertFrom-Json)
    $copy.buildVersion | Should -Be '1.0.0'
    @($copy.customizations).Count | Should -Be 1
    $copy.customizations[0].id | Should -Be 'a'
  }

  It 'lays out the workspace under build/' {
    $paths = Get-StormPaths -Root 'X:\storm-os'
    $paths.MountInstall | Should -Match 'build.mounts.install$'
    $paths.State | Should -Match 'build.temp.build-state\.json$'
  }
}
