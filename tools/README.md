# Outils

Scripts PowerShell à lancer sous Windows, depuis la racine du dépôt.

Windows bloque par défaut l'exécution des scripts non signés. `-ExecutionPolicy Bypass` lève ce blocage pour la seule commande lancée, sans changer la stratégie d'exécution de la machine.

## Install-ShellDeck.ps1

Publie ShellDeck en Release et l'installe pour l'utilisateur courant, avec un raccourci dans le menu Démarrer. Relancez-le pour mettre à jour (ShellDeck doit être fermé).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Install-ShellDeck.ps1
```

Prérequis : SDK .NET 10 pour compiler, runtime .NET 10 Desktop et WebView2 pour exécuter.

Dossier d'installation par défaut : `%LOCALAPPDATA%\Programs\ShellDeck`. Pour en choisir un autre :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Install-ShellDeck.ps1 -InstallDirectory 'D:\Apps\ShellDeck'
```

Désinstallation : supprimez le dossier d'installation et le raccourci `ShellDeck.lnk` du menu Démarrer.

## New-AppIcon.ps1

Régénère `src/ShellDeck/Assets/shelldeck.ico` et les logos du paquet MSIX (`packaging/Assets`) à partir du dessin de `shelldeck.svg`. À relancer après toute modification du SVG (en reportant les formes dans le script).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\New-AppIcon.ps1
```

Ce script nécessite Windows PowerShell (`powershell.exe`, pour `System.Drawing`) : ne le lancez pas avec `pwsh`.

## Depuis une session PowerShell ouverte, dépôt dans WSL

Les fichiers WSL sont accessibles depuis Windows sous `\\wsl.localhost\<distribution>\...`. Débloquez les scripts pour la session en cours seulement, puis appelez-les par leur chemin complet :

```powershell
Set-ExecutionPolicy -Scope Process Bypass
& '\\wsl.localhost\Ubuntu-26.04\home\mf\projects\shelldeck\tools\Install-ShellDeck.ps1'
& '\\wsl.localhost\Ubuntu-26.04\home\mf\projects\shelldeck\tools\New-AppIcon.ps1'
```

Adaptez le nom de la distribution (`wsl -l` les liste) et le chemin du dépôt (`wslpath -w .` dans WSL l'affiche).

## Publier une version

Le workflow `.github/workflows/release.yml` se déclenche sur un tag `v*`. Il lance les tests, publie ShellDeck en Release avec le runtime .NET inclus (win-x64 et win-arm64), puis crée une release GitHub avec un zip par architecture et des notes générées à partir des commits.

```bash
git tag v0.2.0
git push origin v0.2.0
```

Le numéro du tag devient la version de l'exécutable. Côté utilisateur, il suffit de décompresser le zip et de lancer `ShellDeck.exe` : ni SDK ni runtime .NET à installer, seulement WebView2, déjà présent sur Windows 11. L'exécutable n'est pas signé : à la première exécution, SmartScreen affiche « Windows a protégé votre ordinateur », puis « Informations complémentaires » › « Exécuter quand même ».

## New-MsixPackage.ps1

Empaquette une publication de ShellDeck en MSIX pour le Microsoft Store. Le workflow de release l'appelle dès que l'identité Store est déclarée ; la procédure complète est dans [`packaging/STORE.md`](../packaging/STORE.md). Nécessite le SDK Windows (`makeappx.exe`).
