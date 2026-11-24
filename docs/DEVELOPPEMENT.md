# Arcadia — guide de développement

> Présentation et téléchargement : voir le [README](../README.md).

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
- Steam (toutes les bibliothèques), Epic Games (ancien launcher : `ProgramDatapicpicGamesLauncherDataManifests` ; nouveau launcher 2026 : journal `%LOCALAPPDATA%\com.epicgames.eglv2\installations.jsonl` + titres de `cache\prod\catalog-cache.json` ; jeux laissés par l'ancien launcher : dossier `.egstore`), GOG Galaxy, Ubisoft Connect, application Xbox / Game Pass, EA app (`__Installer\installerdata.xml`, lancement `origin2://`), Battle.net (désinstalleur Blizzard `--uid`, lancement `Battle.net.exe --exec="launch <code>"`) et Riot Games (`RiotClientServices.exe --launch-product`).
- Lecture seule des manifestes et du registre ; fenêtre de choix avec cases à cocher ; les jeux déjà présents sont signalés.
- Au démarrage, Arcadia signale les **nouveaux jeux installés** (désactivable). Les jeux décochés ne sont plus reproposés.

**Import manuel** : `.exe`, `.lnk`, `.url` (`steam://`, `uplay://`…), plusieurs fichiers, glisser-déposer, raccourcis Xbox/Store « publiés », doublons ignorés.

**Recherche dans un dossier** (*Chercher dans un dossier…*, ou glisser un dossier dans la fenêtre) : `GameFolderScanner` parcourt les sous-dossiers (8 niveaux, en sautant redistribuables, anti-triche, `Engine`, `steamapps`…) et note chaque `.exe` : moteur reconnu (`<exe>_Data` Unity, `-Shipping` / `Engine` Unreal, `data.win` GameMaker, `<exe>.pck` Godot, `renpy`, RPG Maker, FNA/MonoGame, LÖVE), DLL Steam / GOG / Epic et de jeu (Bink, FMOD, PhysX, DLSS…), archives de jeu, DLL DirectX / OpenGL / Vulkan importées (lues dans l'en-tête PE, sans lancer le fichier), nom du dossier ; en négatif : programme console, éditeur de logiciels, Electron, Qt, description de logiciel. Installeurs, outils, launchers et émulateurs sont exclus par leur nom ou leur description. Un seul `.exe` est gardé par jeu (le 64 bits, le plus haut placé). Score ≥ 5 : proposé coché ; 3 à 5 : « Peut-être des jeux », décoché. Les raccourcis du dossier sont proposés aussi (sauf ceux qui mènent à un logiciel). Un jeu déjà présent sous le même nom (bibliothèque ou autre dossier) est proposé décoché, avec « En double ? ».

**Jeux non installés** (`OwnedGames`, réglage *Ajouter aussi les jeux non installés*, synchronisé au démarrage) :
- Steam : bibliothèque du dernier compte connecté (`userdata\<id>\config\librarycache\<appid>.json`, famille comprise, et `achievement_progress.json`) et jeux déjà joués (`localconfig.vdf`, avec les heures). Noms et types lus dans `appcache\appinfo.vdf` (`SteamAppInfo`, formats 0x07564428 et 0x07564429 avec table de chaînes) ; seuls les `type = game` sont gardés.
- Epic : `cache\prod\account\<id>\library-cache.json` (extensions écartées grâce à `catalog-cache.json`) et `playtime-cache.json`.
- Ces jeux ont `IsNotInstalled` : absents de l'accueil, des thèmes, des statistiques et des trophées (`Game.IsShown`), visibles dans *Tous les jeux → Non installés*. « Installer » ouvre le launcher.
- À chaque synchronisation, un jeu installé depuis repasse avec les autres, et un jeu Steam / Epic désinstallé (toujours dans la bibliothèque du launcher) passe dans « Non installés ». Un jeu non installé retiré n'est plus ajouté (`IgnoredDetections`).
- Xbox, EA, Ubisoft, Battle.net et Riot ne gardent la liste des jeux possédés qu'en ligne.

**Doublons** (`Duplicates`) : même exe, même lien, même jeu Steam, même identifiant de boutique, même dossier de jeu, exe dans le dossier d'un autre jeu, ou même nom (sauf deux jeux émulés). Fenêtre *Jeux en double* (au démarrage, après un import, ou *Paramètres → Jeux en double*) : l'entrée gardée reçoit le temps de jeu (sessions des deux, sans compter deux fois la même), favoris, collections, notes, progression, note, succès et les images qui lui manquent. « Ce ne sont pas des doublons » est retenu (`NotDuplicates`).

**Reconnaissance des noms** : `GTA5.exe - Raccourci` → *Grand Theft Auto V*, nettoyage des versions / builds / tags (`v1.7`, `-P2P`, `[game3rb]`, `Win64-Shipping`…), dossier du jeu, nom de produit, ROM des émulateurs. Un raccourci renommé par vous fait foi.

**Images** : Steam Store (sans clé : jaquette, illustration, logo, captures), Wikipédia (y compris l'image de l'infobox — logos SVG — et Wikipédia en français), SteamGridDB (clé gratuite facultative). Les démos sans page Steam utilisent les images du jeu complet. Recherche manuelle ou image personnelle. Cache local : fonctionne **hors ligne**.

**Jaquettes générées** : un jeu sans aucune image en ligne (fangame, émulateur…) reçoit une vraie jaquette 600×900 créée par Arcadia — couleurs tirées de son icône, halo, icône / logo / monogramme, titre et plateforme. Elle est refaite si vous renommez le jeu, et remplacée automatiquement dès qu'une image officielle est trouvée.

**Temps de jeu Steam et Epic** (`PlatformPlaytime`, réglage *Temps de jeu Steam et Epic Games synchronisé*) : pour les jeux de ces plateformes, le temps de jeu est celui de la plateforme, copié tel quel (même s'il est plus bas) au démarrage, toutes les 5 minutes, 30 s et 2 min après la fermeture d'un de leurs jeux, et avec *Synchroniser maintenant*. Arcadia n'y ajoute jamais ses propres mesures : `Game.PlaytimeSource` (Steam / Epic) le marque, `GameLauncher` et `BackgroundPlaytime` enregistrent alors la session (statistiques par période) sans l'ajouter au total. Steam : `localconfig.vdf` du compte connecté en dernier, pour les jeux de la boutique Steam, ceux d'un dossier `steamapps\common` ou lancés par `steam://rungameid/<id>` (une autre copie d'un jeu vendu sur Steam reste mesurée par Arcadia). Epic : `playtime-cache.json`, par l'identifiant Epic ou le dossier `.egstore`. Les autres plateformes ne gardent rien sur le PC : Arcadia mesure leurs jeux.

**Bibliothèque d'émulation** (*Paramètres → Steam et émulation → Configurer*) : Ryujinx, Yuzu/Eden/Citron, Cemu, Dolphin, PCSX2, DuckStation, PPSSPP, Citra/Azahar, melonDS, mGBA, Project64, Snes9x, xemu. Les émulateurs déjà présents dans la bibliothèque sont détectés, ainsi que leur dossier de jeux (d'après les ROM de vos raccourcis). Chaque ROM devient une fiche (nom nettoyé, console, jaquette), lancée avec les bons arguments ; les nouveaux jeux du dossier sont ajoutés au démarrage.

**Progression** : statut par jeu (À faire, En cours, Terminé, Abandonné), note sur 5 étoiles et notes personnelles sur la fiche ; section *Progression* dans la barre latérale, tri « Mieux notés », badge sur les jaquettes.

**Bandes-annonces** : la bande-annonce Steam se lance en sourdine derrière la fiche du jeu (bouton pause) et en mode TV après quelques secondes sur un jeu. Steam ne diffusant qu'en streaming DASH, Arcadia télécharge ~60 s de la piste vidéo 480p (≈ 6 Mo), la met en cache (30 vidéos max) et la lit en boucle — sans le son (piste audio séparée).

**« Je ne sais pas à quoi jouer »** (bouton *Au hasard*, ou X / R en mode TV) : roulette animée qui ralentit sur le jeu tiré au sort, avec filtres (tous, jamais lancés, à faire, favoris) ; les jeux « À faire » et favoris ont plus de chances.

**Options de lancement par jeu** (*Modifier → Options de lancement*) : exécuter en administrateur, fenêtre d'Arcadia (réglage général / réduire / laisser ouverte / fermer Arcadia), commande avant le lancement et après la fermeture du jeu.

**Lancement et temps de jeu**
- Lancement identique à l'Explorateur Windows (arguments, dossier de travail, UAC).
- **Succès des jeux Steam** : la liste officielle (voir ci-dessous) + ce qui est débloqué, lu dans le cache du client Steam sur ce PC (`Steam\appcache\stats\UserGameStatsSchema_<app>.bin` et `UserGameStats_<compte>_<app>.bin`, KeyValues binaires : un bit par succès, avec sa date). Hors ligne, sans clé ni profil public ; relu au démarrage, à l'ouverture de la fiche et après chaque session. Lecture seule (les plus récents en premier).
- **Succès des jeux hors plateforme** (`.exe`, raccourcis : plateforme « Jeux installés ») : dès qu'un jeu est ajouté et identifié, Arcadia récupère la liste officielle de ses succès sur Steam (API publique `IPlayerService/GetGameAchievements`, sans clé : noms et descriptions en français, icônes, % de joueurs). Les jeux identifiés ailleurs (Wikipédia…) sont cherchés sur Steam par leur nom, avec une correspondance quasi exacte. Dans la fiche, chaque succès se coche d'un clic (date enregistrée, succès secrets masqués tant qu'ils ne sont pas obtenus) ; *Actualiser* recharge la liste sans perdre ce qui est coché, et le menu du jeu propose *Rechercher les succès*. Les jeux Epic / GOG / Ubisoft / Xbox et les jeux émulés ne sont pas concernés.
- **Temps de jeu mesuré y compris pour les jeux Steam / Epic / Ubisoft** : Arcadia suit les processus lancés depuis le dossier d'installation du jeu. Historique des sessions → page Statistiques (activité sur 12 semaines, jeux les plus joués, sessions, répartition par plateforme).
- **Jeux lancés hors d'Arcadia** (`BackgroundPlaytime`, réglage *Mesurer le temps de jeu des jeux lancés hors d'Arcadia*) : toutes les 20 s, les programmes en cours sont comparés aux dossiers / exécutables de la bibliothèque (un dossier partagé par plusieurs jeux ne compte que par l'exe exact) ; la session est enregistrée à la fermeture du jeu ou d'Arcadia. Xbox, EA, Ubisoft, Battle.net et Riot ne gardent pas le temps de jeu sur le PC ; le nouveau launcher Epic (2026) le garde dans `%LOCALAPPDATA%\com.epicgames.eglv2\cache\prod\account\<id>\playtime-cache.json`, repris comme celui de Steam (voir *Temps de jeu Steam et Epic*).
- **Actualités** (fiche du jeu) : les 4 dernières annonces officielles du jeu sur Steam (`ISteamNews/GetNewsForApp`, flux `steam_community_announcements` uniquement, sans clé), avec image, date et extrait ; un clic ouvre l'annonce dans le navigateur. Chargées à l'ouverture de la fiche, gardées une heure en mémoire, jamais enregistrées.
- **Trophées Arcadia** (page Statistiques, `trophies.json`) : 21 jalons bronze / argent / or / platine mesurés sur toute la bibliothèque (temps de jeu, jeux terminés, séries de jours, sessions de nuit, succès, thème essayé…), avec leur progression. Les trophées déjà mérités au premier contrôle sont accordés en silence (un seul message) ; ensuite chaque nouveau trophée descend en bannière au-dessus de toutes les interfaces, avec son son.
- **« Ton année de jeu »** (bannière de la page Statistiques, et message une fois par an entre le 15 décembre et le 15 janvier) : temps de jeu, jeux joués, sessions, succès, jeu de l'année, top 5, mois par mois, plus longue partie, jour et moment préférés, nouveaux jeux, trophées. D'après les sessions enregistrées par Arcadia (le temps importé de Steam n'a pas de date) ; flèches pour les années précédentes.
- **Assistant de premier lancement** (installation neuve uniquement, ou *Réglages → Bibliothèque → Relancer l'assistant*) : bienvenue, jeux trouvés dans les launchers (ajout en un clic), interface, couleur et fond d'écran. Les bibliothèques créées avant cette version ne le voient pas.
- Arcadia se réduit au lancement et revient quand vous quittez le jeu.

**Thèmes d'interface** (barre latérale → *Thèmes*, ou *Réglages → Apparence*) : l'interface Arcadia d'origine, ou une interface plein écran dans l'esprit d'un menu de console. Les dessins, animations et sons sont propres à Arcadia (aucun logo, police, musique ni son officiel).
- **Wii** : pages de 12 chaînes sur fond rayé, horloge et date dans la barre du bas, boutons ronds ; une chaîne ouverte affiche son écran avec *Menu* / *Démarrer*.
- **Wii U** : boutons ronds (réglages, thèmes, ajouter, au hasard), grande bannière du jeu sélectionné et pages d'icônes carrées à contour lumineux.
- **Switch** (sombre ou clair) : rangée de grandes vignettes, titre en couleur, boutons ronds, et la grille *Tous les logiciels*.
- **PS3** : barre de catégories horizontale, jeux en colonne, vague animée dont la couleur change avec le mois, panneau d'options (Y / F).
- **PS4** : icônes de fonctions en haut, rangée de vignettes dont la sélection s'agrandit, bouton *Commencer* et informations.
- **PS5** : petites vignettes en haut, jeu en plein écran avec son logo, bande-annonce, bouton *Jouer* et cartes d'activité.

**Vignettes des thèmes** : chaque jeu reçoit automatiquement une vignette carrée et une vignette large adaptées aux cases des thèmes (au lieu d'une jaquette portrait recadrée). Avec une illustration et un logo, l'illustration remplit la case et le logo est posé dessus ; sinon la jaquette entière est centrée sur un fond flou tiré d'elle-même. Les vignettes sont créées en arrière-plan, pour les nouveaux jeux aussi, et refaites dès qu'une image du jeu change.

Clavier, souris et manette fonctionnent dans tous les thèmes (flèches, Entrée / A, F / Y favori, R / X au hasard, PgPréc / PgSuiv ou LB / RB pour les catégories). À la souris : la molette fait défiler (pages, rangées, listes), le bouton « précédent » de la souris revient en arrière, les indications à l'écran (Ⓐ Lancer, Ⓑ Retour, ◀ ▶…) sont cliquables, et clic droit sur un jeu ouvre son menu. Dans les rangées qui défilent (Switch, PS4, PS5), le survol met une vignette en évidence et le clic la sélectionne, pour que la rangée ne bouge pas sous le curseur. Les réglages s'ouvrent dans l'interface Arcadia, avec un bouton *Retour à l'interface…* (ou Échap).

**Dans le style de chaque famille de thèmes** (PlayStation : PS3 / PS4 / PS5 ; Switch sombre et clair ; Wii : Wii / Wii U) :
- **Fiche du jeu** (Start / O, bouton ⋯) ouverte dans le thème : PlayStation = jeu en plein écran, bouton *Jouer* et cartes *Activités* (temps de jeu, succès avec les icônes des derniers obtenus, actualités) ; Switch = vignette carrée, boutons arrondis, panneaux d'infos et actualités ; Wii = écran de chaîne, boutons *Démarrer*, bandeau d'actualités qui défile. Manette : ◀ ▶ choisir, A valider, B retour, Y favori, Start fiche complète (interface Arcadia). Souris : le survol sélectionne un bouton, le clic le lance, le bouton « précédent » de la souris ou l'indication Ⓑ referme la fiche, les annonces et les indications à l'écran sont cliquables.
- **Succès** du jeu sélectionné dans chaque thème (carte PS5, colonne PS4, ligne d'infos PS3 / Wii / Wii U, à côté du titre sur Switch).
- **Notifications** (messages et bannière de trophée) : sombres et carrées en haut sur PlayStation, pastilles en haut à droite sur Switch, blanches à contour bleu sur Wii.
- **Écran de démarrage** quand Arcadia s'ouvre sur un thème (une touche, un clic ou un bouton le passe) : trait de lumière qui s'élargit (PlayStation), les deux jambes du « A » d'Arcadia qui s'emboîtent avec un déclic (Switch), écran « Appuie sur Ⓐ » (Wii), chacun avec son son. Option développeur `--boot`.

**Mode TV (manette)** — `F11`, bouton *Mode TV*, ou **Start + Back** sur la manette
- Plein écran façon « Big Picture » : grand carrousel, arrière-plan du jeu sélectionné, catégories (Récents, Tous, Favoris, vos collections).
- Manette Xbox / XInput : A jouer, B quitter, Y favori, LB/RB catégorie, stick ou croix pour naviguer. Clavier : ← → Entrée F PgUp/PgDn Échap.
- La manette n'est jamais lue quand un jeu est au premier plan.

**Apparence et sons**
- 6 couleurs d'accent (Nébuleuse, Océan, Émeraude, Crépuscule, Sakura, Or).
- **Couleurs dynamiques** : l'arrière-plan s'illumine avec la couleur dominante de la jaquette affichée.
- **Fond d'écran** (*Réglages → Apparence*, interface Arcadia uniquement) : l'image est copiée dans `images\wallpaper_*`, assombrie (curseur 0-90 %, 70 % par défaut) et éventuellement floutée ; le flou est calculé une seule fois en image (`wallpaper_blur_*.jpg`), jamais en effet temps réel.
- Animations : apparition des pages, entrée échelonnée de la fiche, survol des cartes (zoom, reflet, anneau lumineux), fondus enchaînés.
- **Effets sonores** discrets (démarrage, navigation, ouverture, lancement, favoris, mode TV), générés par l'application (`sounds\v3`, à renommer en v4… dès que la synthèse change), volume réglable ou désactivables. Ils passent par le mixeur d'Arcadia (`AudioMixer`, winmm waveOut 44,1 kHz) : un son relancé s'éteint en fondu de 4 ms au lieu d'être coupé (plus de clics), les sons superposés passent par un limiteur doux, chaque son finit par un fondu en cosinus, et le flux se ferme après 6 s de silence.

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
dotnet test tests\Arcadia.Tests\Arcadia.Tests.csproj         # 99 tests
powershell -ExecutionPolicy Bypass -File tools\publish.ps1   # publish\Arcadia.exe autonome
```

> Sur ce PC, le SDK .NET 10 est installé dans `%LOCALAPPDATA%\Microsoft\dotnet` ; `tools\build.ps1` et `tools\publish.ps1` l'utilisent automatiquement.

**Succès en direct hors launcher** (`EmulatorAchievements`, `MainViewModel.LiveAchievements`, réglage *Afficher les succès débloqués pendant le jeu*) : pour les jeux lancés sans leur launcher, l'identifiant Steam est lu dans le dossier du jeu (`steam_appid.txt`, `OnlineFix.ini` RealAppId, `steam_emu.ini` / `SmartSteamEmu.ini` AppId, `cream_api.ini`; 480 ignoré) et prime sur la recherche par nom pour la liste des succès. Les déblocages sont lus dans les fichiers des émulateurs de l'API Steam : JSON Goldberg / GSE (`%APPDATA%/Goldberg SteamEmu Saves`, `GSE Saves`, EMPRESS), INI une section par succès (CODEX, RUNE dans `%PUBLIC%/Documents/Steam`, OnlineFix, CreamAPI), liste RLD! (`ProgramData/Steam/RLD!`), TENOKE (`SteamData/user_stats.ini` à côté du jeu, lignes `"clé" = {unlocked = true, time = …}`, identifiant dans `tenoke.ini`), et sauvegarde locale Goldberg à côté du jeu (`local_save.txt`). Au démarrage, les succès obtenus depuis sont ajoutés ; ensuite `AchievementFileWatcher` (FileSystemWatcher, 0,7 s de regroupement) déclenche la synchronisation et la fenêtre `AchievementPopup` (au premier plan, sans focus, clics traversants, en bas à droite). Un succès coché à la main n'est jamais décoché. Non lus : SmartSteamEmu (`stats.bin` binaire). `--dialog achievement` montre la fenêtre.
**Statistiques par période** (`PeriodStats`, `PeriodStatsViewModel`, carte « Ton activité ») : Jour / Semaine / Mois / Année / Tout, navigation ‹ › et « Revenir à aujourd'hui ». Les sessions mesurées par Arcadia sont découpées heure par heure (une session qui passe minuit compte sur les deux jours) : temps de jeu et comparaison avec la période précédente, sessions, jeux lancés (découverts, ajoutés), moyenne par jour (à partir de la première session mesurée) ou plus longue session, meilleur jour, graphique (heures, jours, mois ou années ; un clic sur une barre ouvre la période), jeux de la période, jours de la semaine et heures de la journée. Les heures importées de Steam / Epic n'ont pas de date : elles ne comptent que dans les totaux. `--stats-period day|week|month|year|all`.
**Grandes bibliothèques** (testé avec 1 400 jeux) : clés de noms mémorisées (`NameMatcher`), grille qui ne remplace que les rangées modifiées (`GameGrid`), index « déjà dans la bibliothèque ? » (`ImportService.LibraryChecker`), doublons sans comparaison jeu × jeu. `UiLagMonitor` écrit dans `logsrcadia.log` les gels de l'interface (« UI blocked … ms ») et les tâches lentes (« Slow UI task … ») : c'est la première chose à regarder si quelqu'un trouve Arcadia lent. `--perf` mesure le défilement, l'ouverture de chaque page et la recherche.
Options développeur : `--data <dossier>` (bibliothèque séparée / portable), `--import <chemin>`, `--tray` (démarrer dans la zone de notification), `--tv`, `--snapshot <png>` (+ `--section`, `--detail n` ou `--detail-name <nom>`, `--wallpaper <image>` (applique un fond d'écran comme le bouton des réglages), `--dialog edit|cover|confirm|scan|collections|emulators|roulette|themes|onboarding|recap|whatsnew|duplicates`, `--select <n>` (sélectionne n jeux), `--theme <clé>` (wii, wiiu, switch, switch-light, ps3, ps4, ps5), `--keys Right,Enter…` (touches envoyées au thème avant la capture), `--mouse "move 300 400; click; wheel 2; back"` (pilote la vraie souris avant la capture), `--wait-metadata`, `--size LxH`, `--perf`, `--play <nom>`, `--hover` : affiche toutes les jaquettes dans leur état « survolé », `--scroll <px>` : fait défiler la liste avant la capture). `tools\snapshot.ps1` automatise les captures.

## Partager et mettre à jour (GitHub)

Un seul dépôt public, **`dorian86/arcadia`** : le code, la page d'accueil (`README.md`, captures dans `docs/screenshots/`) et les versions publiées (Releases avec `Arcadia.exe`). Vos amis y téléchargent Arcadia, et Arcadia y vérifie les mises à jour.

L'ancien nom `dorian86/arcadia-releases` (versions 1.0.0 et 1.0.1) est redirigé par GitHub vers ce dépôt : les Arcadia installés avant la fusion continuent de recevoir les mises à jour. **Ne recréez jamais de dépôt nommé `arcadia-releases`**, cela casserait cette redirection.

Le dépôt des versions est indiqué dans `src/Arcadia/Arcadia.csproj` : `<UpdateRepository>dorian86/arcadia</UpdateRepository>`.

Les captures se refont avec une bibliothèque de démonstration : `Arcadia.exe --data <dossier> --make-demo <dossier>` (jeux fictifs, illustrations dessinées par Arcadia), puis `tools\snapshot.ps1` avec `ARCADIA_DISPLAY_NAME=Joueur`.

**Publier une nouvelle version** (après avoir enregistré vos modifications : `git add -A` puis `git commit -m "..."`) :

```powershell
powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Version 1.1.0 -Notes "- Nouveau thème`n- Corrections"
```

Le script change le numéro de version, lance les tests, produit `Arcadia.exe`, crée le commit et le tag `v1.1.0`, les envoie sur GitHub, puis publie la Release avec l'exe (avec GitHub CLI `gh` ; sans lui, il affiche les 3 clics à faire sur le site).

**Côté amis** : au démarrage, Arcadia vérifie s'il existe une version plus récente et propose « Mettre à jour » (nouveautés affichées). L'exe est téléchargé, remplacé et Arcadia redémarre ; la bibliothèque et les réglages (`%APPDATA%\Arcadia`) ne sont pas touchés. Désactivable dans *Réglages → Mises à jour*, qui permet aussi de vérifier à la main.

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
tests/Arcadia.Tests   99 tests
```

## Limites connues

- Applications EA app et Battle.net non détectées automatiquement (importez leurs raccourcis).
- Le temps de jeu des jeux Xbox / Game Pass n'est mesuré que s'ils ont été détectés par le scan (dossier d'installation connu).
- Jeux absents de Steam et de Wikipédia (fangames, émulateurs) : jaquette par défaut → *Changer la jaquette…* ou clé SteamGridDB.
