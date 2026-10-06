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

# Refuse a folder that holds something else: it is wiped on update.
if ((Test-Path $InstallDirectory) -and -not (Test-Path (Join-Path $InstallDirectory 'Pupitre.exe')) -and
    (Get-ChildItem -Force $InstallDirectory | Select-Object -First 1)) {
    throw "$InstallDirectory existe et ne contient pas Pupitre : choisissez un autre dossier avec -InstallDirectory."
}

# Publish to a staging folder first, so a failed build leaves the installed version untouched.
# Next to the install folder, so the final move stays on the same volume (Move-Item cannot move a folder across volumes).
$installParent = Split-Path $InstallDirectory
New-Item -ItemType Directory -Force $installParent | Out-Null
$staging = Join-Path $installParent "pupitre-publish-$([guid]::NewGuid())"
try {
    dotnet publish $project --configuration Release --runtime win-x64 --self-contained false --output $staging
    if ($LASTEXITCODE -ne 0) { throw "La publication a échoué (code $LASTEXITCODE)." }

    if (Test-Path $InstallDirectory) {
        Remove-Item -Recurse -Force $InstallDirectory
    }
    Move-Item $staging $InstallDirectory
}
finally {
    if (Test-Path $staging) {
        Remove-Item -Recurse -Force $staging
    }
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $InstallDirectory 'Pupitre.exe'
$shortcut.WorkingDirectory = $env:USERPROFILE
$shortcut.Description = 'Terminaux PowerShell, WSL, cmd et SSH'
$shortcut.Save()

Write-Output "Pupitre installé dans $InstallDirectory"
Write-Output "Raccourci : $shortcutPath"
