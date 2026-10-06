<#
.SYNOPSIS
    Publishes Pupitre and installs it for the current user, with a Start menu shortcut.
.DESCRIPTION
    Requires the .NET 10 SDK to build, and the .NET 10 Desktop Runtime plus the WebView2 runtime to run
    (both ship with an up-to-date Windows 11 developer machine). Run it again to update.
    Uninstall: delete the install folder and the Start menu shortcut.
#>
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\Pupitre')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\Pupitre\Pupitre.csproj'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Pupitre.lnk'

if (Get-Process -Name Pupitre -ErrorAction SilentlyContinue) {
    throw 'Pupitre est ouvert : fermez-le avant de lancer l''installation.'
}

# Publish to a staging folder first, so a failed build leaves the installed version untouched.
$staging = Join-Path ([IO.Path]::GetTempPath()) "pupitre-publish-$([guid]::NewGuid())"
dotnet publish $project --configuration Release --runtime win-x64 --self-contained false --output $staging
if ($LASTEXITCODE -ne 0) { throw "La publication a échoué (code $LASTEXITCODE)." }

# Only ever wipe a folder that holds a previous Pupitre install.
if (Test-Path (Join-Path $InstallDirectory 'Pupitre.exe')) {
    Remove-Item -Recurse -Force $InstallDirectory
}
New-Item -ItemType Directory -Force (Split-Path $InstallDirectory) | Out-Null
Move-Item $staging $InstallDirectory

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $InstallDirectory 'Pupitre.exe'
$shortcut.WorkingDirectory = $env:USERPROFILE
$shortcut.Description = 'Terminaux PowerShell, WSL, cmd et SSH'
$shortcut.Save()

Write-Output "Pupitre installé dans $InstallDirectory"
Write-Output "Raccourci : $shortcutPath"
