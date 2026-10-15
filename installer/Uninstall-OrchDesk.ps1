[CmdletBinding(SupportsShouldProcess)]
param([string]$InstallDir = "$env:LOCALAPPDATA\OrchDesk", [switch]$Purge, [switch]$TestMode)
$ErrorActionPreference = 'Stop'
$resolved = [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
if ($resolved -eq [IO.Path]::GetPathRoot($resolved).TrimEnd('\') -or !(Test-Path -LiteralPath (Join-Path $resolved 'OrchDesk.exe')) -or !(Test-Path -LiteralPath (Join-Path $resolved '.orchdesk-install'))) { throw 'Not an OrchDesk installation.' }
if ($PSCmdlet.ShouldProcess($resolved, 'Uninstall OrchDesk')) {
    if (!$TestMode) {
        Get-Process OrchDesk -ErrorAction SilentlyContinue | Stop-Process -Force
        foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) { Remove-Item -LiteralPath (Join-Path $folder 'OrchDesk.lnk') -Force -ErrorAction SilentlyContinue }
        Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OrchDesk' -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
    if ($Purge -and !$TestMode) { $settings = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'OrchDesk')); if ([IO.Path]::GetFileName($settings) -ne 'OrchDesk') { throw 'Invalid settings folder.' }; Remove-Item -LiteralPath $settings -Recurse -Force -ErrorAction SilentlyContinue }
}
