# Arcadia — lanceur de jeux PC

Arcadia rassemble tous vos jeux PC dans une seule bibliothèque visuelle, dans l'esprit de Steam, Playnite ou GOG Galaxy.
Il détecte vos jeux Steam, Epic, GOG, Ubisoft et Xbox, importe vos raccourcis (`.lnk`, `.url`) et exécutables (`.exe`), retrouve le vrai nom de chaque jeu, télécharge sa jaquette officielle et le lance en un clic — à la souris ou à la manette.

Application Windows native : **C# / .NET 10 / WPF**, thème sombre, interface entièrement personnalisée.

---

## Installation

1. Lancez `publish\Arcadia.exe` (autonome : **aucune installation de .NET nécessaire**).
2. *Paramètres → Système → Installer* : Arcadia se copie dans `%LOCALAPPDATA%\Programs\Arcadia`, crée ses raccourcis (menu Démarrer + bureau) et apparaît dans *Applications installées* de Windows (désinstallation propre, sans droits administrateur).
3. Premier lancement : **Ajouter des jeux → Détecter les jeux installés**, puis importez votre dossier « Jeux » du bureau (ou glissez-le dans la fenêtre).

Désinstaller : *Paramètres Windows → Applications → Arcadia → Désinstaller* (Arcadia demande s'il faut garder votre bibliothèque).

---

## Fonctionnalités

**Bibliothèque**
- Grille de jaquettes virtualisée et fluide (plusieurs centaines de jeux), taille réglable.
- Accueil avec **carrousel de jeux mis en avant** (illustration, logo officiel, défilement automatique, points et flèches), rangées *Reprendre la partie*, *Favoris*, *Ajoutés récemment*.
- Sections *Tous les jeux*, *Favoris*, *Récemment joués*, **Statistiques**, **Plateformes** (Steam, Epic, GOG, Ubisoft, Xbox, Émulation, Jeux installés), *Introuvables*, *Masqués*.
- **Collections personnalisées** (VR, Multijoueur, Émulation…) : création depuis la barre latérale, renommage / suppression par clic droit.
- **Sélection multiple** : Ctrl+clic, Maj+clic, Ctrl+A → barre d'actions (favoris, collections, masquer, retirer).
- **Masquer** un jeu (outils, émulateurs…) sans le retirer.
- Recherche instantanée (nom, développeur, genre, plateforme, collection), tri par nom, date d'ajout, dernière partie ou temps de jeu.
- Jeux portant le même nom : la carte indique leur origine (« Steam », dossier local…).
- Fiche détaillée : grande jaquette, **JOUER**, statistiques, description (en français si Steam la fournit), **captures d'écran** avec visionneuse, plateforme, collections.

**Détection des jeux installés**
- Steam (toutes les bibliothèques), Epic Games, GOG Galaxy, Ubisoft Connect et application Xbox / Game Pass.
- Lecture seule des manifestes et du registre ; fenêtre de choix avec cases à cocher ; les jeux déjà présents sont signalés.
- Au démarrage, Arcadia signale les **nouveaux jeux installés** (désactivable). Les jeux décochés ne sont plus reproposés.

**Import manuel** : `.exe`, `.lnk`, `.url` (`steam://`, `uplay://`…), plusieurs fichiers, dossiers entiers, glisser-déposer, raccourcis Xbox/Store « publiés », doublons ignorés.

**Reconnaissance des noms** : `GTA5.exe - Raccourci` → *Grand Theft Auto V*, nettoyage des versions / builds / tags (`v1.7`, `-P2P`, `[game3rb]`, `Win64-Shipping`…), dossier du jeu, nom de produit, ROM des émulateurs. Un raccourci renommé par vous fait foi.

**Images** : Steam Store (sans clé : jaquette, illustration, logo, captures), Wikipédia (y compris l'image de l'infobox — logos SVG — et Wikipédia en français), SteamGridDB (clé gratuite facultative). Les démos sans page Steam utilisent les images du jeu complet. Recherche manuelle ou image personnelle. Cache local : fonctionne **hors ligne**.

**Jaquettes générées** : un jeu sans aucune image en ligne (fangame, émulateur…) reçoit une vraie jaquette 600×900 créée par Arcadia — couleurs tirées de son icône, halo, icône / logo / monogramme, titre et plateforme. Elle est refaite si vous renommez le jeu, et remplacée automatiquement dès qu'une image officielle est trouvée.

**Temps de jeu Steam** : Arcadia lit le temps de jeu et la date de dernière partie que Steam enregistre déjà sur le PC (compte connecté en dernier) et les reprend — sans jamais additionner deux fois une même session.

**Bibliothèque d'émulation** (*Paramètres → Steam et émulation → Configurer*) : Ryujinx, Yuzu/Eden/Citron, Cemu, Dolphin, PCSX2, DuckStation, PPSSPP, Citra/Azahar, melonDS, mGBA, Project64, Snes9x, xemu. Les émulateurs déjà présents dans la bibliothèque sont détectés, ainsi que leur dossier de jeux (d'après les ROM de vos raccourcis). Chaque ROM devient une fiche (nom nettoyé, console, jaquette), lancée avec les bons arguments ; les nouveaux jeux du dossier sont ajoutés au démarrage.

**Progression** : statut par jeu (À faire, En cours, Terminé, Abandonné), note sur 5 étoiles et notes personnelles sur la fiche ; section *Progression* dans la barre latérale, tri « Mieux notés », badge sur les jaquettes.

**Bandes-annonces** : la bande-annonce Steam se lance en sourdine derrière la fiche du jeu (bouton pause) et en mode TV après quelques secondes sur un jeu. Steam ne diffusant qu'en streaming DASH, Arcadia télécharge ~60 s de la piste vidéo 480p (≈ 6 Mo), la met en cache (30 vidéos max) et la lit en boucle — sans le son (piste audio séparée).

**« Je ne sais pas à quoi jouer »** (bouton *Au hasard*, ou X / R en mode TV) : roulette animée qui ralentit sur le jeu tiré au sort, avec filtres (tous, jamais lancés, à faire, favoris) ; les jeux « À faire » et favoris ont plus de chances.

**Options de lancement par jeu** (*Modifier → Options de lancement*) : exécuter en administrateur, fenêtre d'Arcadia (réglage général / réduire / laisser ouverte / fermer Arcadia), commande avant le lancement et après la fermeture du jeu.

**Lancement et temps de jeu**
- Lancement identique à l'Explorateur Windows (arguments, dossier de travail, UAC).
- **Temps de jeu mesuré y compris pour les jeux Steam / Epic / Ubisoft** : Arcadia suit les processus lancés depuis le dossier d'installation du jeu. Historique des sessions → page Statistiques (activité sur 12 semaines, jeux les plus joués, sessions, répartition par plateforme).
- Arcadia se réduit au lancement et revient quand vous quittez le jeu.

**Thèmes d'interface** (barre latérale → *Thèmes*, ou *Réglages → Apparence*) : l'interface Arcadia d'origine, ou une interface plein écran dans l'esprit d'un menu de console. Les dessins, animations et sons sont propres à Arcadia (aucun logo, police, musique ni son officiel).
- **Wii** : pages de 12 chaînes sur fond rayé, horloge et date dans la barre du bas, boutons ronds ; une chaîne ouverte affiche son écran avec *Menu* / *Démarrer*.
- **Wii U** : boutons ronds (réglages, thèmes, ajouter, au hasard), grande bannière du jeu sélectionné et pages d'icônes carrées à contour lumineux.
- **Switch** (sombre ou clair) : rangée de grandes vignettes, titre en couleur, boutons ronds, et la grille *Tous les logiciels*.
- **PS3** : barre de catégories horizontale, jeux en colonne, vague animée dont la couleur change avec le mois, panneau d'options (Y / F).
- **PS4** : icônes de fonctions en haut, rangée de vignettes dont la sélection s'agrandit, bouton *Commencer* et informations.
- **PS5** : petites vignettes en haut, jeu en plein écran avec son logo, bande-annonce, bouton *Jouer* et cartes d'activité.

**Vignettes des thèmes** : chaque jeu reçoit automatiquement une vignette carrée et une vignette large adaptées aux cases des thèmes (au lieu d'une jaquette portrait recadrée). Avec une illustration et un logo, l'illustration remplit la case et le logo est posé dessus ; sinon la jaquette entière est centrée sur un fond flou tiré d'elle-même. Les vignettes sont créées en arrière-plan, pour les nouveaux jeux aussi, et refaites dès qu'une image du jeu change.

Clavier, souris et manette fonctionnent dans tous les thèmes (flèches, Entrée / A, F / Y favori, R / X au hasard, PgPréc / PgSuiv ou LB / RB pour les catégories). À la souris : la molette fait défiler (pages, rangées, listes), le bouton « précédent » de la souris revient en arrière, les indications à l'écran (Ⓐ Lancer, Ⓑ Retour, ◀ ▶…) sont cliquables, et clic droit sur un jeu ouvre son menu. Dans les rangées qui défilent (Switch, PS4, PS5), le survol met une vignette en évidence et le clic la sélectionne, pour que la rangée ne bouge pas sous le curseur. La fiche d'un jeu et les réglages s'ouvrent dans l'interface Arcadia, avec un bouton *Retour à l'interface…* (ou Échap).

**Mode TV (manette)** — `F11`, bouton *Mode TV*, ou **Start + Back** sur la manette
- Plein écran façon « Big Picture » : grand carrousel, arrière-plan du jeu sélectionné, catégories (Récents, Tous, Favoris, vos collections).
- Manette Xbox / XInput : A jouer, B quitter, Y favori, LB/RB catégorie, stick ou croix pour naviguer. Clavier : ← → Entrée F PgUp/PgDn Échap.
- La manette n'est jamais lue quand un jeu est au premier plan.

**Apparence et sons**
- 6 couleurs d'accent (Nébuleuse, Océan, Émeraude, Crépuscule, Sakura, Or).
- **Couleurs dynamiques** : l'arrière-plan s'illumine avec la couleur dominante de la jaquette affichée.
- Animations : apparition des pages, entrée échelonnée de la fiche, survol des cartes (zoom, reflet, anneau lumineux), fondus enchaînés.
- **Effets sonores** discrets (navigation, ouverture, lancement, favoris, mode TV), générés par l'application, volume réglable ou désactivables.

**Système** : icône dans la zone de notification (lancer un jeu récent, mode TV, quitter), option « garder ouvert à la fermeture », **lancement avec Windows** (démarre dans la zone de notification).

**Fiabilité** : sauvegarde automatique atomique avec copie de secours, détection des jeux introuvables + **Localiser…**, suivi automatique des raccourcis mis à jour par un launcher (ex. Roblox). **Arcadia ne modifie, ne déplace et ne supprime jamais les fichiers de vos jeux.**

### Raccourcis clavier
`Ctrl+F` rechercher · `Ctrl+O` ajouter · `Ctrl+clic` / `Maj+clic` sélection · `Ctrl+A` tout sélectionner · `F11` mode TV · `Échap` / bouton « précédent » de la souris : retour · `F5` vérifier les chemins · clic droit sur un jeu : menu complet.

---

## Performances

Les effets « live » de WPF (flous, ombres portées) recalculés à chaque image étaient la cause des ralentissements. Ils sont remplacés par des ombres pré-rendues, des images réduites étirées (flou gratuit) et des menus créés à la demande. Mesures en défilement continu sur ce PC (outil `--perf`) :

| Fenêtre | Avant | Après |
|---|---|---|
| 1600×1000 | 53 img/s (pics à 67 ms) | **113 img/s** |
| 2400×1300 | 18 img/s (pics à 133 ms) | **85 img/s** |

---

## Données

`%APPDATA%\Arcadia\` (*Paramètres → Bibliothèque → Ouvrir*) : `library.json` (+ `.bak`), `settings.json`, `images\` (jaquettes, illustrations, logos, icônes), `shortcuts\` (copies de vos raccourcis), `sounds\`, `logs\arcadia.log`. Copiez ce dossier pour sauvegarder ou transférer votre bibliothèque.

---

## Compiler

Prérequis : **SDK .NET 10**, Windows 10/11.

```powershell
dotnet build src\Arcadia\Arcadia.csproj                     # compiler
dotnet test tests\Arcadia.Tests\Arcadia.Tests.csproj         # 75 tests
powershell -ExecutionPolicy Bypass -File tools\publish.ps1   # publish\Arcadia.exe autonome
```

> Sur ce PC, le SDK .NET 10 est installé dans `%LOCALAPPDATA%\Microsoft\dotnet` ; `tools\build.ps1` et `tools\publish.ps1` l'utilisent automatiquement.

Options développeur : `--data <dossier>` (bibliothèque séparée / portable), `--import <chemin>`, `--tray` (démarrer dans la zone de notification), `--tv`, `--snapshot <png>` (+ `--section`, `--detail n`, `--dialog edit|cover|confirm|scan|collections|emulators|roulette|themes`, `--theme <clé>` (wii, wiiu, switch, switch-light, ps3, ps4, ps5), `--keys Right,Enter…` (touches envoyées au thème avant la capture), `--mouse "move 300 400; click; wheel 2; back"` (pilote la vraie souris avant la capture), `--wait-metadata`, `--size LxH`, `--perf`, `--play <nom>`, `--hover` : affiche toutes les jaquettes dans leur état « survolé », `--scroll <px>` : fait défiler la liste avant la capture). `tools\snapshot.ps1` automatise les captures.

## Partager et mettre à jour (GitHub)

Deux dépôts GitHub :

- **`arcadia`** (privé) : le code. Personne d'autre que vous ne le voit.
- **`arcadia-releases`** (public) : uniquement les versions publiées (Releases avec `Arcadia.exe`), sans code. Vos amis y téléchargent Arcadia, et Arcadia y vérifie les mises à jour (un dépôt privé exigerait un mot de passe dans l'exe, ce qui n'est pas sûr). Sa page d'accueil (README et captures) est dans `docs/releases-page/`. Les captures se refont avec une bibliothèque de démonstration : `Arcadia.exe --data <dossier> --make-demo <dossier>` (jeux fictifs, illustrations dessinées par Arcadia), puis `toolssnapshot.ps1` avec `ARCADIA_DISPLAY_NAME=Joueur`.

Le dépôt des versions est indiqué dans `src/Arcadia/Arcadia.csproj` : `<UpdateRepository>pseudo/arcadia-releases</UpdateRepository>`.

**Publier une nouvelle version** (après avoir enregistré vos modifications : `git add -A` puis `git commit -m "..."`) :

```powershell
powershell -ExecutionPolicy Bypass -File toolselease.ps1 -Version 1.1.0 -Notes "- Nouveau thème`n- Corrections"
```

Le script change le numéro de version, lance les tests, produit `Arcadia.exe`, crée le commit et le tag `v1.1.0`, les envoie sur le dépôt privé, puis publie la Release sur le dépôt public avec l'exe (avec GitHub CLI `gh` ; sans lui, il affiche les 3 clics à faire sur le site).

**Côté amis** : au démarrage, Arcadia vérifie s'il existe une version plus récente et propose « Mettre à jour » (nouveautés affichées). L'exe est téléchargé, remplacé et Arcadia redémarre ; la bibliothèque et les réglages (`%APPDATA%Arcadia`) ne sont pas touchés. Désactivable dans *Réglages → Mises à jour*, qui permet aussi de vérifier à la main.

## Architecture

```
src/Arcadia
├── Models/        Game (enregistrement + modèle observable), AppSettings, plateformes, sessions
├── Services/      Library, Import, StoreScanner (Steam/Epic/GOG/Ubisoft/Xbox), Metadata (+ Online/ Steam,
│                  Wikipédia, SteamGridDB), GameLauncher + ProcessWatcher (temps de jeu), Gamepad (XInput),
│                  Sound (synthèse), Visuals (ombres, grain, couleur dominante, thèmes), SystemIntegration
│                  (zone de notification, installation, démarrage Windows), NameCleaner / NameMatcher
├── ViewModels/    MainViewModel, dialogues, statistiques, mode TV
├── Views/         Accueil, Bibliothèque, Fiche, Statistiques, Paramètres, Mode TV, dialogues
│   └── Shells/    thèmes de console (Wii, Wii U, Switch, PS3, PS4, PS5) + fenêtre Thèmes
├── Controls/      GameGrid (virtualisée), ImageLoader (asynchrone, cache), SmoothScroll, GameMenu…
└── Themes/        palette, styles, modèles
tests/Arcadia.Tests   75 tests
```

## Limites connues

- Applications EA app et Battle.net non détectées automatiquement (importez leurs raccourcis).
- Le temps de jeu des jeux Xbox / Game Pass n'est mesuré que s'ils ont été détectés par le scan (dossier d'installation connu).
- Jeux absents de Steam et de Wikipédia (fangames, émulateurs) : jaquette par défaut → *Changer la jaquette…* ou clé SteamGridDB.
