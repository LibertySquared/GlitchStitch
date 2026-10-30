param([string]$DesktopFolder = [Environment]::GetFolderPath('Desktop'))
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$appPath = Join-Path $projectRoot 'portable-release\GlitchStich.exe'
$iconPath = Join-Path $projectRoot 'portable-release\Resources\reelgood.ico'
if (-not (Test-Path -LiteralPath $appPath) -or -not (Test-Path -LiteralPath $iconPath)) { throw 'Install the updated app and icon before creating its desktop shortcut.' }
$desktop = [IO.Path]::GetFullPath($DesktopFolder)
if (-not (Test-Path -LiteralPath $desktop -PathType Container)) { throw "The desktop folder does not exist: $desktop" }
$shortcutPath = [IO.Path]::GetFullPath((Join-Path $desktop 'GlitchStich.lnk'))
if ([IO.Path]::GetDirectoryName($shortcutPath) -ne $desktop) { throw 'Shortcut resolved outside the desktop folder.' }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $appPath
$shortcut.WorkingDirectory = [IO.Path]::GetDirectoryName($appPath)
$shortcut.IconLocation = "$iconPath,0"
$shortcut.Description = 'GlitchStich — glitches into stitches'
$shortcut.Save()
$verified = $shell.CreateShortcut($shortcutPath)
if ($verified.TargetPath -ne $appPath -or $verified.IconLocation -ne "$iconPath,0") { throw 'The desktop shortcut did not retain its target and icon.' }
$legacyShortcut = Join-Path $desktop 'ReelGood.lnk'
if (Test-Path -LiteralPath $legacyShortcut) {
    $previous = $shell.CreateShortcut($legacyShortcut)
    if ($previous.TargetPath -eq (Join-Path $projectRoot 'portable-release\ReelGood.exe') -or $previous.TargetPath -eq $appPath) {
        Remove-Item -LiteralPath $legacyShortcut -Force
    }
}
Write-Output "Desktop shortcut: $shortcutPath"
