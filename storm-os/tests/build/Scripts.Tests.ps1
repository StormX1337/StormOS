BeforeAll { $script:Scripts = Join-Path $PSScriptRoot '..\..\scripts' }

Describe 'Build scripts' {
  It 'provides every pipeline script: <_>' -ForEach @(
    '00-check-prerequisites.ps1', '01-prepare-workspace.ps1', '02-import-source.ps1', '03-inspect-image.ps1',
    '04-mount-install.ps1', '05-apply-branding.ps1', '06-apply-defaults.ps1', '07-apply-gaming-config.ps1',
    '08-install-storm-apps.ps1', '09-configure-oobe.ps1', '10-build-winpe.ps1', '11-build-installer.ps1',
    '12-build-iso.ps1', '13-validate-image.ps1', '14-test-vm.ps1', '15-cleanup.ps1', 'build-all.ps1', 'rollback.ps1'
  ) {
    Test-Path -LiteralPath (Join-Path $Scripts $_) | Should -BeTrue
  }

  It 'parses without syntax errors: <Name>' -ForEach @(Get-ChildItem -Path (Join-Path $PSScriptRoot '..\..\scripts') -Recurse -Include *.ps1, *.psm1 | ForEach-Object { @{ Name = $_.Name; Path = $_.FullName } }) {
    $tokens = $null; $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors) | Out-Null
    $errors | Should -BeNullOrEmpty
  }

  It 'documents every script with a synopsis: <Name>' -ForEach @(Get-ChildItem -Path (Join-Path $PSScriptRoot '..\..\scripts') -Filter *.ps1 | ForEach-Object { @{ Name = $_.Name; Path = $_.FullName } }) {
    (Get-Content -LiteralPath $Path -Raw) | Should -Match '\.SYNOPSIS'
  }

  It 'build-all exposes the documented parameters' {
    $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $Scripts 'build-all.ps1'), [ref]$null, [ref]$null)
    $names = $ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath }
    foreach ($name in 'SourceIso', 'SourceWim', 'EditionIndex', 'OutputDirectory', 'BuildVersion', 'EnableGamingDefaults', 'EnableStormApps', 'EnableOverlay', 'EnableBenchmarks', 'CleanBuild') {
      $names | Should -Contain $name
    }
  }

  It 'never passes user input to Invoke-Expression' {
    foreach ($file in Get-ChildItem -Path $Scripts -Recurse -Include *.ps1, *.psm1) {
      (Get-Content -LiteralPath $file.FullName -Raw) | Should -Not -Match 'Invoke-Expression|iex\s'
    }
  }

  It 'has a phase function for every phase' {
    Import-Module (Join-Path $Scripts 'lib\StormBuild.psm1') -Force
    foreach ($phase in Get-StormPhaseTable) { Get-Command $phase.Function -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty }
  }
}
