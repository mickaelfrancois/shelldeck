# Outils

Scripts PowerShell à lancer sous Windows, depuis la racine du dépôt.

Windows bloque par défaut l'exécution des scripts non signés. `-ExecutionPolicy Bypass` lève ce blocage pour la seule commande lancée, sans changer la stratégie d'exécution de la machine.

## Install-Pupitre.ps1

Publie Pupitre en Release et l'installe pour l'utilisateur courant, avec un raccourci dans le menu Démarrer. Relancez-le pour mettre à jour (Pupitre doit être fermé).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Install-Pupitre.ps1
```

Prérequis : SDK .NET 10 pour compiler, runtime .NET 10 Desktop et WebView2 pour exécuter.

Dossier d'installation par défaut : `%LOCALAPPDATA%\Programs\Pupitre`. Pour en choisir un autre :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Install-Pupitre.ps1 -InstallDirectory 'D:\Apps\Pupitre'
```

Désinstallation : supprimez le dossier d'installation et le raccourci `Pupitre.lnk` du menu Démarrer.

## New-AppIcon.ps1

Régénère `src/Pupitre/Assets/pupitre.ico` à partir du dessin de `pupitre.svg`. À relancer après toute modification du SVG (en reportant les formes dans le script).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\New-AppIcon.ps1
```

Ce script nécessite Windows PowerShell (`powershell.exe`, pour `System.Drawing`) : ne le lancez pas avec `pwsh`.

## Depuis une session PowerShell ouverte, dépôt dans WSL

Les fichiers WSL sont accessibles depuis Windows sous `\\wsl.localhost\<distribution>\...`. Débloquez les scripts pour la session en cours seulement, puis appelez-les par leur chemin complet :

```powershell
Set-ExecutionPolicy -Scope Process Bypass
& '\\wsl.localhost\Ubuntu-26.04\home\mf\projects\pupitre\tools\Install-Pupitre.ps1'
& '\\wsl.localhost\Ubuntu-26.04\home\mf\projects\pupitre\tools\New-AppIcon.ps1'
```

Adaptez le nom de la distribution (`wsl -l` les liste) et le chemin du dépôt (`wslpath -w .` dans WSL l'affiche).
