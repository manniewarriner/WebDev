[CmdletBinding()]
param([string]$Source, [string]$InstallDir = "$env:LOCALAPPDATA\OrchDesk", [switch]$Update, [switch]$NoShortcuts, [switch]$TestMode)
$ErrorActionPreference = 'Stop'
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
if ($InstallDir -eq [IO.Path]::GetPathRoot($InstallDir)) { throw 'Choose an application folder.' }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$target = Join-Path $InstallDir 'OrchDesk.exe'
$staged = Join-Path $InstallDir 'OrchDesk.download.exe'
$headers = @{ 'User-Agent' = 'OrchDesk-Installer' }
$version = '0.3.0'
if ($Source) { Copy-Item -LiteralPath $Source -Destination $staged -Force }
else {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $releases = Invoke-RestMethod 'https://api.github.com/repos/manniewarriner/WebDev/releases?per_page=100' -Headers $headers
    $release = $releases | Where-Object { $_.tag_name -like 'orchdesk-v*' -and !$_.prerelease -and !$_.draft } | Sort-Object published_at -Descending | Select-Object -First 1
    $asset = $release.assets | Where-Object name -eq 'OrchDesk.exe' | Select-Object -First 1
    if (!$asset) { throw 'No OrchDesk release executable found.' }
    Invoke-WebRequest $asset.browser_download_url -OutFile $staged -UseBasicParsing -Headers $headers
    $version = $release.tag_name -replace '^orchdesk-v',''
    $checksum = $release.assets | Where-Object name -eq 'SHA256SUMS.txt' | Select-Object -First 1
    if (!$checksum) { throw 'Release checksum is missing.' }
    $sums = (Invoke-WebRequest $checksum.browser_download_url -UseBasicParsing -Headers $headers).Content
    $expected = [regex]::Match($sums, '(?im)^([a-f0-9]{64})\s+\*?OrchDesk\.exe\s*$').Groups[1].Value
    if (!$expected -or (Get-FileHash $staged -Algorithm SHA256).Hash -ne $expected) { throw 'Release checksum failed.' }
}
if (!$TestMode) {
    Get-Process OrchDesk -ErrorAction SilentlyContinue | ForEach-Object { if (!$_.CloseMainWindow()) { Stop-Process -Id $_.Id -Force } elseif (!$_.WaitForExit(10000)) { Stop-Process -Id $_.Id -Force } }
}
if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination "$target.bak" -Force }
Move-Item -LiteralPath $staged -Destination $target -Force
Set-Content -LiteralPath (Join-Path $InstallDir '.orchdesk-install') -Value 'OrchDesk per-user installation'
$installerDir = Join-Path $InstallDir 'installer'
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
foreach ($scriptName in @('Install-OrchDesk.ps1','Uninstall-OrchDesk.ps1')) {
    $localScript = if ($PSScriptRoot) { Join-Path $PSScriptRoot $scriptName } else { $null }
    $destination = Join-Path $installerDir $scriptName
    if ($localScript -and (Test-Path -LiteralPath $localScript)) { if ([IO.Path]::GetFullPath($localScript) -ne $destination) { Copy-Item -LiteralPath $localScript -Destination $destination -Force } }
    else { Invoke-WebRequest "https://raw.githubusercontent.com/manniewarriner/WebDev/orchdesk/self-improve/installer/$scriptName" -OutFile $destination -UseBasicParsing }
}
if (!$NoShortcuts -and !$TestMode) {
    $shell = New-Object -ComObject WScript.Shell
    foreach ($folder in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
        $shortcut = $shell.CreateShortcut((Join-Path $folder 'OrchDesk.lnk')); $shortcut.TargetPath = $target; $shortcut.WorkingDirectory = $InstallDir; $shortcut.IconLocation = "$target,0"; $shortcut.Save()
    }
}
if (!$TestMode) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OrchDesk'
    New-Item $key -Force | Out-Null
    $uninstaller = Join-Path $installerDir 'Uninstall-OrchDesk.ps1'
    $values = @{ DisplayName='OrchDesk'; DisplayIcon=$target; UninstallString="powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`" -InstallDir `"$InstallDir`""; DisplayVersion=$version; Publisher='Mannie Warriner'; InstallLocation=$InstallDir }
    foreach ($name in $values.Keys) { New-ItemProperty $key -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null }
}
Write-Output "Installed OrchDesk $version to $InstallDir"
