# Caméra First-Person — diagnostic du 13 octobre 2026

## État initial et portée

Le checkout était sur `master` à `fd44b9d367a0a43180465151aa8cd21a621d47aa`.
Seul `AGENTS.md` était non suivi ; aucune source locale modifiée. Après fetch,
la base la plus récente est `origin/fix/diagnostics-20261012`,
`32e5f105effcd476c5b9a3bc82f63d944bdb1471`. Les branches
`fix/reliability-audit-20261011` (`6feaf7d`) et
`fix/lua-xml-reliability-20261012` (`c348ea6`) sont ses ancêtres. La
[PR 2](https://github.com/matla91/S6Patcher/pull/2) reste en brouillon ; la
PR 3 documentaire est intégrée à la base et la PR 1 intermédiaire est fermée.
La mission crée `fix/first-person-camera-engine-20261013` depuis cette base,
sans modifier les PR ni publier de release. `Source/Archive` reste à
`ec1424232f56a7b5c44d5c5acb4a200925573c45`.

Les sources locales/globales, les deux bancs Lua, le déploiement C# et les
documents d'architecture/audit/tests ont été examinés. Le SDK 10.0.100 exigé
par `global.json` a été ajouté au toolchain local (10.0.401 déjà présent) ;
Lua 5.1.5 officiel a été compilé avec GCC existant, hors Git. Le jeu installé
n'a pas été patché, lancé ou modifié.

## Ce qui était déjà corrigé

La base contient les protections C01–C09 de `FIXES.md` : traduction par clé,
prévalidation marché/coordonnées/touches, entrée et frame protégées,
restauration de chaque étape, `CleanupPending`, restauration de widgets et
bindings, retrait unique de la page FPS, délégation des callbacks originaux,
sortie avant restrictions d'entrée, directions tenues, durée bornée, vitesse
de simulation conservée, interruption caméra et récupération au rechargement.
Le marché existe dès le début d'une partie normale ; aucune API d'habitant
n'intervient dans l'activation. Cette logique est conservée.

L'incident historique pouvait appartenir à une ancienne version. Les défauts
suivants sont reproduits sur la base actuelle avec doubles ; ils ne sont pas
une attribution certaine de l'incident observé en jeu.

## Défauts reproduits et corrections

| Défaut dans la base | Conséquence / changement ciblé |
| --- | --- |
| Un diagnostic non protégé précède la récupération après erreur d'entrée/frame | Si le logger échoue, `Disable` n'est jamais appelé. Un helper protège log et conversion d'erreur. |
| `Disable` marque pending seulement à la fin et journalise chaque erreur sans protection | Double faute de restauration/journal : sortie partielle, `Enabled=false` et `CleanupPending=false`, donc Z/Échap perdent le chemin de récupération. Pending est posé dès le début ; toutes les étapes continuent. |
| `GUI.ClearSelection` appelle un callback qui peut quitter pendant `Enable`, puis l'entrée poursuit ses mutations | La caméra peut être inactive avec commandes interdites et sons/alertes désactivés. Une demande d'annulation est consommée après le bloc d'entrée protégé. |
| Un callback de récupération peut réentrer via Échap | Le callback original peut être invoqué ou le nettoyage répété. Le garde de désactivation et pending rendent cette sortie idempotente. |
| Entrée à terrain +500, déplacement à terrain +200 ; look-at calculé avant déplacement | Chute de 300 unités au premier mouvement et cible décalée lors du déplacement. Hauteur initiale alignée sur +200 et cible calculée depuis la pose finale. |
| Aucun marqueur de chargement du candidat | Écriture des fichiers ne prouve pas leur exécution. Marqueur `S6Patcher FPS script loaded: camera-engine-20261013-v1`, plus transitions rares, sans logs par frame. |

Ces changements sont dans `9c25ef3`, avec neuf régressions ajoutées au banc
existant. Ils conservent `FPSMode`, ses méthodes, le marché, l'opt-out
`FirstPersonCamera=0`, les callbacks sauvegardés et la récupération pending.

`5e73c71` ajoute neuf scénarios guidés par les
[preuves natives](FIRST_PERSON_NATIVE.md) : perte/sélection de mode, switch
ignoré et reprise, FOV live initial/stationnaire, occlusion persistée 0/2 et
snapshots défaillants. Il conserve la caméra externe et sa cible, applique
le FOV via position et restaure l'occlusion capturée sans conversion booléenne.

## Cycle Lua après correction

1. Z traite d'abord sortie/récupération ; sinon vérifie restrictions et opt-out.
2. `Enable` résout joueur, marché, approche, hauteur finie et touches distinctes,
   puis mémorise cible RTS et origine souris. Aucun habitant n'est requis.
3. Pending et activation sont posés avant les mutations : rendu close-up,
   comportement 5, pose/FOV/distance, widgets/page, bindings et commandes GUI.
4. Échec ou annulation synchrones déclenchent `Disable` après l'entrée protégée.
5. Les widgets ThroneRoom appellent le hook : restrictions/interruption, puis
   yaw/pitch, delta de temps borné à 0,1 s, déplacement 600 unités/s,
   contrôle de secteur, suivi du terrain et pose/cible cohérentes.
6. Z/Échap ou erreur mettent pending avant nettoyage, retirent l'activité et
   directions, tentent chaque restauration. Une autre caméra détectée est
   conservée. Une faute conserve pending pour une nouvelle tentative.

Les gardes ne remplacent pas l'architecture par une nouvelle machine à états.
Un crash natif et un setter qui ignore silencieusement son appel ne sont pas
détectés par la réussite de `pcall`.

## Sources livrées avec History Edition

Références relatives à `Data/base/shr`, lues dans l'installation, sans copier
leur code dans Git. Leurs empreintes et les preuves natives détaillées sont
conservées dans `../analysis/first-person-20261013/`.

- `menu/Projects/ThroneRoom.xml:11959,21068,21158` route
  `ThroneRoomCameraControl()` depuis Campaign/updater/Briefing ; le callback
  peut donc recevoir plusieurs appels. La cadence native n'est pas observée.
- `Script/Local/KeyBindings/KeyBindings.lua:181,943` : Échap appelle
  `Game.Escape`, puis le callback annule cutscene/animation.
  `CameraAnimation.Abort:425` appelle `ShowCloseUpView(0)` ; ce dernier
  ramène un mode non nul à 0 en reprenant une position CloseUp. Le hook FPS
  absorbe déjà le callback actif ; les effets natifs antérieurs restent à
  distinguer du chemin Lua.
- `CameraAnimation.lua:306,351` modifie mouvement clavier RTS, bords, widgets
  et sélection lors des cinématiques ; `IsRunning` expose son état.
- `Selection.lua:33` teste `Camera.GetCameraBehaviour()` ; modes 0 RTS, 1
  CloseUp et 2 FreeView sont étayés par leurs appels dans les scripts.
- `MenuPage.lua:22,34` mémorise `GetTopPage` puis le redonne à `PushPage`.
  Cela ne prouve pas son type ni un identifiant unique de propriétaire.
- `VideoOptions.lua:33,282,442,458` gère `Display/Occlusion` ;
  `DeviceOptions.lua:7` présente des valeurs 0, 1, 2. Les setters user/program
  d'occlusion sont distincts dans `SharedPresentationTools.lua:22,103`.

Aucune définition du contrôleur ThroneRoom n'a été trouvée dans les Lua
libres de `Data` ; cette recherche bornée ne prouve pas son absence des
archives ou du natif. Aucune correction spéculative de pile/page n'est faite.

## Tests et limites

| Preuve exécutée sur Windows | Référence / résultat après `9c25ef3` |
| --- | --- |
| Tests simulés caméra existants | 22/22 avant |
| Banc final contre base `32e5f10` | 23/40, 17 échecs attendus ; 22 anciens passent |
| Banc final contre premier correctif `9c25ef3` | 31/40, 9 échecs attendus |
| Banc final contre `5e73c71` | 40/40 |
| Autres hooks Lua | 34/34 avant et après |
| C# avec fixtures, incluant kill/recovery d'un processus synthétique | 121/121 avant et après |
| Build SDK 10.0.100, Debug, analyseurs, warnings en erreurs | 0 warning / 0 erreur avant et après |
| Syntaxe Lua 5.1.5 | Trois scripts et deux bancs acceptés |
| Publish autonome Windows de développement sans trimming | Réussite ; ressources du candidat embarquées |
| Provenance/déploiement des trois ressources du Release réel dans un profil temporaire | 9 comparaisons SHA-256, 0 échec |
| Moteur, UI interactive, nouvelle partie / RTX 3060 | Non exécuté |

Les mocks vérifient rendu **par défaut**, target RTS, contrôles et équilibre
du nombre de pages ; ils ne prouvent pas les réglages arbitraires, l'identité
des pages superposées, le scheduling GUI ou un effet natif différé. Les
touches sont les mappings localisés hérités, pas une nouvelle capture des
bindings tiers personnalisés. Confronter ces points au jeu témoin.

La procédure [Windows](FIRST_PERSON_WINDOWS_VALIDATION.md) fixe commit/hashes,
profil et installation isolés, sauvegarde complète, ressources embarquées,
preuve de chargement, marché sans habitants, Z/Échap, vingt cycles,
cinématiques/rechargements et retour au checkpoint. Le lot téléchargé est
accepté seulement s'il correspond aux trois ressources de cette compilation.

Les conclusions natives, modes 5/0 et hypothèses H1–H9 sont consignés dans
[FIRST_PERSON_NATIVE.md](FIRST_PERSON_NATIVE.md). Aucun patch x86 n'est requis
pour les défauts étayés. La propriété réelle des pages pendant une cinématique
reste inconnue ; FOV ThroneRoom persistant et overrides runtime restent à
comparer au témoin. Le premier appel REA a attendu l'import complet par défaut ;
aucune couverture du binaire packed n'est assimilée à l'analyse décompressée.

Le package trimmed standard se construit avec IL2026 mais son journal JSON
hérité refuse Begin/Recover lorsque la réflexion est désactivée. Le probe
sur fixtures le confirme, sans jeu. Pour rendre cet essai concret, le package
est publié avec `-p:PublishTrimmed=false` ; aucune refonte P10 n'est faite.

Le package de test est conservé hors Git dans
`../analysis/first-person-20261013/packages/development-build-camera-untrimmed/`.
SHA-256 de `S6Patcher.exe` :
`c5280391fe21ef4f958d71d396b49ccc7612f86e27b59b4c8f46a925554ae599`.
Le probe Release compare les trois ressources réellement embarquées et leur
déploiement par le handler réel au source : neuf comparaisons réussies, zéro
échec. Le script local a pour SHA-256
`829c9594a9be0d04a80035ef9385eb214d3ecee8860f6034c1b9d972ba2ad498`.
Les binaires installé/original/décompressé du jeu ont les mêmes empreintes
avant et après mission. Les packages, probes, bundles et snapshots ne sont
pas committés.
