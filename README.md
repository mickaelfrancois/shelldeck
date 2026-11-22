# ShellDeck

Terminal à onglets et panneaux pour Windows (PowerShell, WSL, SSH), basé sur WPF et xterm.js.

## Les trois lignes d'un onglet

Chaque onglet de la liste des sessions fait trois lignes, toujours de la même hauteur :

1. le titre (titre du terminal, ou nom du profil) ;
2. le dossier courant en gras, précédé de son dossier parent en grisé (`projects/`**`shell-deck`**) ;
3. l'état git : `⎇ branche ● ↑n ↓m` (`●` modifications non commitées, `↑n` commits à pousser, `↓m` à tirer). La ligne reste vide hors d'un repo.

Quand la place manque, le dossier parent se raccourcit en premier, puis la branche ; le dossier courant et les marqueurs restent visibles. Une notification en attente de ta réponse ou un shell terminé remplace la deuxième ligne. En SSH, la deuxième ligne montre la connexion et il n'y a jamais d'info git.

### Choisir ce que montrent les lignes

Dans `~/.shelldeck/settings.json`, `tabLines` donne un gabarit par ligne (trois au plus, les lignes manquantes sont vides) :

```json
{
  "tabLines": ["{title}", "{parent}{folder}", "{git}"]
}
```

Le fichier est rechargé à chaud. Un champ inconnu donne du vide et une accolade non fermée reste du texte. Une ligne dont tous les champs sont vides est vide (`⎇ {git.branch}` n'affiche rien hors d'un repo). `{{` et `}}` donnent une accolade. `{>}` pousse ce qui suit au bord droit de la ligne (`"{git}{>}{ctx}"`) ; sans place, la fin ne chevauche jamais le début.

| Champ | Contenu |
| --- | --- |
| `{title}` | titre du terminal, ou nom du profil |
| `{profile}` | nom du profil |
| `{folder}` | dossier courant ; avant le premier rapport du shell, `Profil · emplacement` ; en SSH, la connexion |
| `{parent}` | dossier parent avec son séparateur (`projects/`), vide en SSH |
| `{path}` | chemin complet annoncé par le shell |
| `{git}` | `⎇ branche ● ↑n ↓m` |
| `{git.repo}`, `{git.branch}` | nom du repo, branche |
| `{git.dirty}`, `{git.ahead}`, `{git.behind}` | marqueurs, avec leur espace de tête (` ●`, ` ↑2`, ` ↓1`) |

### Champs fournis par une commande

Un guide pas à pas, pensé pour être suivi aussi par un agent, est dans [`docs/configurer-les-onglets.md`](docs/configurer-les-onglets.md) ; le schéma JSON de `settings.json` est dans [`docs/settings.schema.json`](docs/settings.schema.json). En cas de doute sur la commande : clic droit sur un onglet, **Diagnostic de la commande…**.

Pour afficher autre chose (modèle et contexte de Claude Code, phase d'une bataille Legion, etc.), `tabCommand` déclare un programme et ses arguments, lancé pour chaque onglet :

```json
{
  "tabCommand": ["wsl.exe", "-d", "Ubuntu-26.04", "--exec", "python3", "/home/me/shell-deck/examples/claude-tab.py"],
  "tabLines": ["{title}", "{parent}{folder}", "{git}  {model} {ctx}"]
}
```

- **Entrée :** ShellDeck envoie sur l'entrée standard un objet JSON : `version`, `title`, `status` (`running`, `working`, `attention`, `exited`), `profile` (`id`, `name`, `kind`, `location`), `directory`, `claudeSessionId` et `git` (`repo`, `branch`, `detached`, `dirty`, `ahead`, `behind`, ou `null`). `directory` et `claudeSessionId` sont nuls en SSH ou tant que le shell n'a rien annoncé.
- **Sortie :** la commande écrit sur la sortie standard un objet JSON dont chaque clé devient un champ utilisable dans `tabLines`. Une valeur est un texte, un nombre, ou `{"text": "BUILD", "style": "attention"}`. Styles : `normal`, `muted` (défaut), `strong`, `attention`, `dirty`, `ahead`, `behind`.
- **Rafraîchissement :** au changement de dossier, à la fin d'une commande, à la fin d'un tour de Claude Code, toutes les 30 s et au retour de la fenêtre, jamais fenêtre inactive. Un onglet n'a qu'une exécution à la fois, trois commandes au plus tournent ensemble.
- **Échec :** une commande qui échoue, dépasse 2 s, affiche plus de 64 Ko ou ne renvoie pas un objet JSON garde les valeurs précédentes de l'onglet. Les champs intégrés (`{title}`, `{git}`…) l'emportent sur un champ de même nom.
- **Sécurité :** la commande n'est lue que dans `settings.json`. Elle est lancée directement, sans shell, et les données du terminal (dossier, titre, session) n'arrivent que par l'entrée standard, jamais dans la ligne de commande : un script doit les traiter comme du texte non fiable. Sa sortie est nettoyée (caractères de contrôle remplacés par des espaces, 200 caractères et 32 champs au plus, noms limités à lettres, chiffres, `_`, `.` et `-`).

`examples/claude-tab.py` donne `{model}`, `{ctx}` (tokens du contexte) et `{ctxpct}` en lisant le transcript de la session Claude Code (`~/.claude/projects`), à lancer dans la distribution WSL. Le transcript n'indique pas la taille de la fenêtre de contexte : 200 000 tokens par défaut, `SHELLDECK_CTX_WINDOW` pour une autre (par exemple `--exec env SHELLDECK_CTX_WINDOW=1000000 python3 …`).

## Dossier courant et git

ShellDeck n'injecte rien dans le shell : **c'est le shell qui doit annoncer son dossier courant** à chaque prompt, par une séquence OSC 7 (`file://hôte/chemin`) ou OSC 9;9 (ConEmu). Sans cela, la deuxième ligne n'affiche que `Profil · emplacement` (le dossier d'ouverture) et il n'y a pas d'info git.

Le dossier annoncé sert aussi à rouvrir un panneau au bon endroit (restauration de session) et à démarrer un nouveau panneau dans le même dossier lors d'un split.

Les lignes sont rafraîchies à chaque changement de dossier, à la fin d'une commande (si le shell émet OSC 133), toutes les 30 s et au retour de la fenêtre. ShellDeck n'exécute jamais `git fetch` : `↓m` reflète ce que git connaît déjà.

### PowerShell (Windows)

Dans `$PROFILE` :

```powershell
function prompt {
    $location = $executionContext.SessionState.Path.CurrentLocation
    $osc = if ($location.Provider.Name -eq 'FileSystem') {
        "$([char]27)]9;9;`"$($location.ProviderPath)`"$([char]27)\"
    } else { '' }
    "PS $location> $osc"
}
```

### WSL : bash

Dans `~/.bashrc` de la distribution :

```bash
__shelldeck_cwd() { printf '\e]7;file://%s%s\e\\' "$HOSTNAME" "$PWD"; }
PROMPT_COMMAND="__shelldeck_cwd${PROMPT_COMMAND:+;$PROMPT_COMMAND}"
```

### WSL : zsh

Dans `~/.zshrc` de la distribution :

```zsh
__shelldeck_cwd() { printf '\e]7;file://%s%s\e\\' "$HOST" "$PWD" }
precmd_functions+=(__shelldeck_cwd)
```

### oh-my-posh

Garder oh-my-posh pour le prompt et ajouter le snippet du shell ci-dessus (bash, zsh ou PowerShell) pour annoncer le dossier.

N'utilise **pas** `"pwd": "osc7"` sous WSL : oh-my-posh y réécrit le chemin en `file://hôte///wsl.localhost/<distro>/home/me`, soit un chemin réseau (UNC) que ShellDeck refuse (voir les limites).

L'option `"shell_integration": true`, à la racine du thème, est en revanche utile : elle émet les marques OSC 133, qui rafraîchissent le git dès la fin d'une commande.

```json
{
  "version": 3,
  "shell_integration": true,
  "blocks": []
}
```

### Limites

- Les dossiers réseau et de périphérique (`\\hôte\partage`, `//hôte/…`, `\\?\…`) sont refusés : ils ne sont ni affichés ni utilisés pour rouvrir un panneau.
- Un chemin contenant `?`, `#` ou un guillemet est ignoré en OSC 7. Utiliser OSC 9;9 sous PowerShell pour ces cas.
- Le guillemet et les caractères de contrôle sont refusés dans tous les cas.
