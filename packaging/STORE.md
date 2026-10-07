# Publier sur le Microsoft Store

## Une seule fois

1. Créer un compte développeur sur l'[Espace partenaires](https://partner.microsoft.com/dashboard/registration) : gratuit pour un particulier.
2. *Apps et jeux* › *Nouveau produit* › *Application MSIX ou PWA*, réserver le nom **ShellDeck**. S'il était pris, un nom proche (« ShellDeck Terminal ») convient : il n'apparaît que dans le Store et le menu Démarrer.
3. Dans *Gestion des produits* › *Identité du produit*, relever l'identité et la déclarer, avec le nom réservé, comme variables du dépôt GitHub (*Settings* › *Secrets and variables* › *Actions* › *Variables*) :

   | Variable | Valeur de l'Espace partenaires |
   |---|---|
   | `MSIX_IDENTITY_NAME` | `Package/Identity/Name` |
   | `MSIX_PUBLISHER` | `Package/Identity/Publisher` (`CN=…`) |
   | `MSIX_PUBLISHER_DISPLAY_NAME` | `Package/Properties/PublisherDisplayName` |
   | `MSIX_DISPLAY_NAME` | le nom réservé, à l'identique |

   Dès lors, chaque tag `v*` joint aussi `ShellDeck-<version>-win-x64.msix` et `-win-arm64.msix` à la release.

4. Publier la politique de confidentialité ci-dessous à une URL **publique** : le Store la demande même pour une application qui ne collecte rien. Le dépôt étant privé, une page GitHub du dépôt ne suffit pas ; un gist public convient.

## À chaque version

1. `git tag v0.2.0 && git push origin v0.2.0`, attendre la release.
2. Dans l'Espace partenaires, créer une soumission, déposer les deux `.msix` dans *Packages*. Le Store les signe : pas de certificat à acheter.
3. Le numéro de version doit augmenter à chaque soumission.

## Tester le paquet avant de le soumettre

```powershell
.\tools\New-MsixPackage.ps1 -PublishDirectory publish\win-x64 -Version 0.2.0 -Architecture x64 `
    -IdentityName '<MSIX_IDENTITY_NAME>' -Publisher '<MSIX_PUBLISHER>' -PublisherDisplayName '<MSIX_PUBLISHER_DISPLAY_NAME>' -DisplayName '<MSIX_DISPLAY_NAME>' -TestCertificate
```

`-TestCertificate` signe le paquet avec un certificat de test et indique les deux commandes pour l'installer (la première, une fois, en administrateur).

## Fiche du Store

**Description courte** : Terminal à onglets pour PowerShell, WSL et SSH.

**Description** :

> ShellDeck réunit vos shells dans une seule fenêtre : PowerShell, Invite de commandes, distributions WSL et connexions de votre `~/.ssh/config`, détectés automatiquement.
>
> • Onglets et volets, avec des raccourcis clavier inspirés de Windows Terminal.
> • Lanceur organisable : masquez, ordonnez, ajoutez vos propres terminaux et choisissez leur répertoire de démarrage.
> • Signalement des onglets qui attendent une réponse, notamment les sessions Claude Code.
> • Réglages dans un simple fichier JSON, modifiable à la main et rechargé à chaud.

**Catégorie** : Outils de développement.

## Politique de confidentialité

> ShellDeck ne collecte, ne stocke à distance ni ne transmet aucune donnée personnelle. Les réglages (`%USERPROFILE%\.shelldeck\settings.json`) et le journal d'erreurs restent sur votre ordinateur. Les connexions SSH sont établies par le client OpenSSH de Windows, directement vers les serveurs que vous avez configurés.
