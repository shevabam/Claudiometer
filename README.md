# Claudiometer

Petit widget Windows 11 qui affiche, dans la taskbar à côté de l'horloge, le quota **Claude Code** :
session 5 h et semaine, en pourcentage. Au survol : date de reset dans le fuseau local. Au clic : panneau détaillé.

- Un `.exe` natif .NET Framework, compilé avec le `csc.exe` déjà présent dans Windows : **rien à installer**.
- Léger : ~3-5 Mo de RAM (working set), ~0 % CPU.
- Choix de l'écran (principal / secondaire), position auto à gauche de l'horloge.

## Compiler et lancer

Prérequis : Windows 10/11 (le `csc.exe` de .NET Framework 4 est déjà présent).

```powershell
.\build.ps1          # produit Claudiometer.exe
.\Claudiometer.exe
```

Au premier lancement, un `config.ini` est créé à côté de l'exe.

### Commandes (Makefile)

Un `Makefile` regroupe les raccourcis (GNU make : `winget install ezwinports.make`). Chaque target s'appuie sur
un script `.ps1` qu'on peut aussi lancer directement, sans make.

| Commande | Équivalent sans make | Effet |
|---|---|---|
| `make build` | `.\build.ps1` | Compile `Claudiometer.exe` |
| `make run` | `.\build.ps1` puis lancer l'exe | Compile, ferme l'instance en cours et relance |
| `make version` | `.\version.ps1` | Affiche la version courante |
| `make version V=1.2.0` | `.\version.ps1 1.2.0` | Change la version dans `Claudiometer.cs` (`Program.Version`, `AssemblyVersion`, `AssemblyFileVersion`) |
| `make release` | `.\release.ps1` | Compile et produit `dist\Claudiometer-<version>.zip` + `.sha256` |
| `make tag` | `git tag v<version>` | Crée le tag git de la version courante (local, rien n'est poussé) |
| `make clean` | | Supprime l'exe et `dist\` |
| `make help` | | Liste les commandes |

### Publier une version

Première fois : initialiser le dépôt et le relier à GitHub.

```powershell
git init
git add .
git commit -m "Premier commit"
git branch -M main
git remote add origin https://github.com/<vous>/Claudiometer.git
git push -u origin main
```

Ensuite, pour chaque nouvelle version (exemple avec la 1.2.0) :

```powershell
make version V=1.2.0          # 1. change le numéro dans Claudiometer.cs
make release                  # 2. (facultatif) vérifie que le zip se construit bien en local
git commit -am "Version 1.2.0"   # 3. commit du changement de version
git push
make tag                      # 4. crée le tag v1.2.0
git push origin v1.2.0        # 5. pousse le tag : GitHub compile et publie la release
```

À l'étape 5, le workflow `.github/workflows/release.yml` compile l'exe sur GitHub, puis attache le zip et le
`.sha256` à une release du même nom. Suivre l'avancement dans l'onglet « Actions » du dépôt.

Sans GitHub Actions, on peut aussi déposer à la main le zip de `dist\` dans « Releases > Draft a new release ».

Pour signer l'exe (évite l'avertissement SmartScreen), définir `CLAUDIOMETER_CERT` avec l'empreinte d'un
certificat de signature de code avant `make release`. Sans certificat, l'exe est non signé.

## Utilisation

| Action | Effet |
|---|---|
| Survol | Infobulle : % restant / utilisé, reset (date + « dans X h Y min »), heure de mise à jour |
| Clic gauche | Panneau : barres, resets des deux fenêtres, intervalle d'actualisation, lien « Actualiser » |
| Clic droit | Actualiser maintenant, intervalle (1 à 30 min), lancer avec Windows, ouvrir la config, quitter |

## Configuration (`config.ini`, relu automatiquement)

| Clé | Défaut | Rôle |
|---|---|---|
| `Monitor` | `secondary` | `primary`, `secondary` ou numéro d'écran (1, 2, …) |
| `RefreshMinutes` | `5` | Intervalle d'actualisation (min 1) |
| `ShowRemaining` | `true` | `true` = quota restant, `false` = quota utilisé |
| `Layout` | `stack` | `stack` (2 lignes) ou `line` (1 ligne) |
| `LabelSession` / `LabelWeek` | `5h` / `7j` | Libellés |
| `LabelOpacity` | `65` | Opacité des libellés (`5h` / `W`) en % : 100 = pleine, plus bas = plus discret |
| `FontName` / `FontSize` | `Segoe UI` / `9` | Police |
| `RightOffset` | `auto` | `auto` = collé à gauche de l'horloge ; ou un nombre de px depuis le bord droit |
| `WarnPercent` / `CritPercent` | `30` / `10` | Seuils (sur le quota **restant**) : orange / rouge |
| `TextColor` | `auto` | Suit le thème Windows, ou `#RRGGBB` |
| `TrayIcon` | `false` | Icône dans la zone de notification (pastille avec le % de la session) |

## Comment ça marche

- Lit le token OAuth de Claude Code dans `~\.claude\.credentials.json` (ou `$CLAUDE_CONFIG_DIR`),
  **en lecture seule** : il n'est jamais rafraîchi (ça déconnecterait Claude Code), ni écrit, ni journalisé.
- Interroge `GET https://api.anthropic.com/api/oauth/usage` (en-tête `anthropic-beta: oauth-2025-04-20`) :
  champs `five_hour` et `seven_day` (`utilization`, `resets_at`).
  **Cet endpoint n'est pas documenté officiellement** et peut changer ou limiter (HTTP 429).
- En cas d'échec (token expiré, 429, réseau), les dernières valeurs restent affichées en gris, avec la raison
  et l'heure du prochain essai dans le panneau. Le détail de la dernière erreur est dans `last-error.txt`.
- **Backoff** : après un échec réseau ou un 429, l'intervalle est doublé à chaque échec (plafond 30 min, ou
  `Retry-After` si le serveur demande plus). Il revient à la normale dès qu'une actualisation réussit.
- **Token expiré** : le widget ne le renouvelle pas. Dès que Claude Code réécrit `.credentials.json`, il
  réessaie aussitôt ; sinon le panneau indique de relancer Claude Code.
- Si l'API répond dans un format inattendu (endpoint non documenté), le widget l'indique au lieu de rester muet.
- Le widget est une fenêtre `layered` sans bordure posée sur la taskbar. La position de l'horloge est détectée
  par UI Automation (en tâche de fond, toutes les 5 min ou si la taskbar change).
- Masqué quand une application est en plein écran (détection via `SHQueryUserNotificationState`).

## Limites connues

- Taskbar chargée (écran principal) : le widget peut recouvrir les derniers boutons d'application.
- Pas de bouton de repli quand il n'y a pas de place ; l'icône de notification (`TrayIcon=true`) peut servir de secours.
- Après plusieurs redémarrages rapprochés, l'API peut répondre 429 : les valeurs reviennent au rafraîchissement suivant.
