[CmdletBinding()]
param([Parameter(Mandatory)][string]$Source, [string]$TestRoot = (Join-Path $env:TEMP 'OrchDesk-installer-tests'))
$ErrorActionPreference = 'Stop'
foreach ($name in @('Install-OrchDesk.ps1', 'Uninstall-OrchDesk.ps1')) {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name), [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count) { throw "$name failed PowerShell parsing: $errors" }
}
$root = [IO.Path]::GetFullPath($TestRoot)
$target = [IO.Path]::GetFullPath((Join-Path $root ([Guid]::NewGuid().ToString('N'))))
if (!$target.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test path.' }
& (Join-Path $PSScriptRoot 'Install-OrchDesk.ps1') -Source $Source -InstallDir $target -NoShortcuts -TestMode
$installed = Join-Path $target 'OrchDesk.exe'
if ((Get-FileHash $Source).Hash -ne (Get-FileHash $installed).Hash) { throw 'Installed executable differs.' }
if (!(Test-Path -LiteralPath (Join-Path $target 'installer/Uninstall-OrchDesk.ps1'))) { throw 'Uninstaller missing.' }
& (Join-Path $PSScriptRoot 'Install-OrchDesk.ps1') -Source $Source -InstallDir $target -NoShortcuts -TestMode -Update
if ((Get-FileHash $Source).Hash -ne (Get-FileHash "$installed.bak").Hash) { throw 'Backup differs.' }
& (Join-Path $PSScriptRoot 'Uninstall-OrchDesk.ps1') -InstallDir $target -TestMode
if (Test-Path -LiteralPath $target) { throw 'Uninstall did not remove the test installation.' }
Write-Output 'PASS: parser, install, executable hash, installer copies, replacement backup, uninstall. No user shortcuts, registration, settings, or running app changed.'
