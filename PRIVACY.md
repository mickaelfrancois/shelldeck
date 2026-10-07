# Politique de confidentialité de ShellDeck

*Dernière mise à jour : 7 octobre 2026*

ShellDeck est un terminal pour Windows. Il ne collecte, ne transmet ni ne vend aucune donnée personnelle, et ne contient ni publicité, ni mesure d'audience, ni télémétrie.

## Données enregistrées sur votre ordinateur

ShellDeck n'enregistre que ce qui lui sert à fonctionner, et uniquement sur votre ordinateur :

- **Vos réglages**, dans `%USERPROFILE%\.shelldeck\settings.json` : côté du panneau, position de la fenêtre, terminal ouvert au démarrage, terminaux ajoutés, masqués ou réordonnés.
- **Un journal d'erreurs**, dans le dossier de données de l'application, si une erreur inattendue se produit. Il contient la date, le message technique de l'erreur et l'endroit du code de ShellDeck où elle s'est produite.
- **Le cache du composant d'affichage** (Microsoft Edge WebView2), dans le même dossier.

Rien de tout cela n'est envoyé où que ce soit. Désinstaller la version du Microsoft Store supprime le dossier de données ; le fichier de réglages peut être supprimé à la main.

## Connexions réseau

ShellDeck n'établit lui-même aucune connexion réseau. Les programmes que vous lancez dans ses onglets (PowerShell, WSL, le client OpenSSH de Windows…) se connectent là où vous le leur demandez, selon leurs propres règles. ShellDeck lit votre fichier `~/.ssh/config` pour lister vos connexions, sans le modifier ni le transmettre.

Ouvrir un lien affiché dans le terminal le transmet à votre navigateur par défaut.

## Composants tiers

L'affichage du terminal repose sur Microsoft Edge WebView2, un composant de Windows dont l'usage est régi par la [déclaration de confidentialité de Microsoft](https://privacy.microsoft.com/privacystatement).

## Contact

Pour toute question sur cette politique, utilisez les coordonnées de support indiquées sur la fiche Microsoft Store de ShellDeck.

---

# ShellDeck privacy policy

*Last updated: October 7, 2026*

ShellDeck is a terminal for Windows. It does not collect, transmit or sell any personal data, and contains no advertising, analytics or telemetry.

**Data stored on your computer only:** your settings (`%USERPROFILE%\.shelldeck\settings.json`), an error log written if an unexpected error occurs (date, technical message and where in ShellDeck's code it happened), and the cache of the display component (Microsoft Edge WebView2). None of it is sent anywhere.

**Network:** ShellDeck makes no network connection itself. The programs you start in its tabs (PowerShell, WSL, the Windows OpenSSH client…) connect where you tell them to. ShellDeck reads `~/.ssh/config` to list your connections, without changing or sending it. Opening a link shown in the terminal hands it to your default browser.

**Third-party components:** the terminal display relies on Microsoft Edge WebView2, governed by the [Microsoft privacy statement](https://privacy.microsoft.com/privacystatement).

**Contact:** use the support contact shown on ShellDeck's Microsoft Store page.
