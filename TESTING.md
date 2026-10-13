# Validation reproductible

## Caméra / moteur — 13 octobre 2026

La mission caméra reprend `32e5f105effcd476c5b9a3bc82f63d944bdb1471` de
`fix/diagnostics-20261012`, sur `fix/first-person-camera-engine-20261013`.
Sur Windows, avec le SDK **10.0.100** et Lua **5.1.5**, la référence donne
22/22 caméra, 34/34 hooks et 121/121 C# ; build Debug avec analyseurs et
warnings en erreurs : zéro warning/erreur. Le décompte 19 de la mission est
historique ; la base consolidée contient déjà 22 scénarios caméra.

Le commit `9c25ef3` ajoute neuf scénarios au banc caméra existant. Le même
banc sur le script de référence donne **23/31 réussites, 8 échecs** (les
22 tests historiques passent). Après correction : **31/31 caméra**, **34/34
hooks**, **121/121 C#**, même build et syntaxe des trois scripts et deux bancs
acceptés. Les doubles vérifient maintenant les commandes contextuelles,
feedback/alertes, paramètres de rendu par défaut et cible RTS, au lieu de
réduire tous ces setters à des fonctions vides. Ils ne certifient pas une
préférence graphique arbitraire ni la pile de pages native.

Le second commit `5e73c71`, guidé par
[REA/Ghidra](docs/FIRST_PERSON_NATIVE.md), porte la suite à **40/40 caméra**,
avec **34/34 hooks**, **121/121 C#**, syntaxe des cinq fichiers acceptée et
build Debug toujours sans warning/erreur. Le banc final donne **31/40** sur
`9c25ef3` (9 échecs attendus) et **23/40** sur `32e5f10` (17 échecs attendus,
les 22 historiques passent). Il vérifie setters silencieux hors contrôleur,
reprise native de mode/cible, FOV appliqué à l'arrêt et occlusion 0/2/reprise.

Le publish Release standard avec trimming réussit mais produit IL2026 sur
le journal JSON HE Steam ; son runtime désactive la réflexion JSON. Un probe
des méthodes **réelles** `PatchSessionRecovery.Begin/Recover` sur fixtures
reproduit `InvalidOperationException` avec ce switch, puis réussit avec
réflexion activée. Ce défaut préexistant ne touche pas au correctif Lua ;
le package de développement pour l'essai est construit avec
`-p:PublishTrimmed=false`. Aucune correction générale P10 n'est introduite.
Les preuves sont dans `../analysis/first-person-20261013/json-trimming-probe/`.
Le protocole Windows utilise explicitement ce package sans trimming.

Un probe séparé du **Release réel** compare les trois ressources via leur
API et streams embarqués, puis les déploie avec le vrai
`UserScriptHandler.CreateUserScriptFiles(false)` dans un profil temporaire
initialisé : **9 comparaisons SHA-256, 0 échec**. Sources, bytes embarqués et
fichiers écrits correspondent. Preuve dans
`../analysis/first-person-20261013/deployment-resource-probe/` ; cela atteste
la provenance du package, pas le chargement par le moteur.

Les sorties détaillées sont conservées hors Git dans
`../analysis/first-person-20261013/`. La procédure actuelle de déploiement,
preuve de chargement et restauration intégrale est
[FIRST_PERSON_WINDOWS_VALIDATION.md](docs/FIRST_PERSON_WINDOWS_VALIDATION.md).
Les sections datées ci-dessous sont des résultats historiques : les
limitations de récupération HE et de logger décrites avant les changements
consolidés doivent être lues avec [RECOVERY.md](RECOVERY.md) et
[DIAGNOSTICS.md](DIAGNOSTICS.md). Aucune validation en jeu n'est annoncée.

## Suite XML/Lua — 12 octobre 2026

Branche : `fix/lua-xml-reliability-20261012`. Départ vérifié après fetch :
`6feaf7d535bb35a2c8b9850e2ae9d7166237229b` de
`fix/reliability-audit-20261011`. Le dépôt local était propre. Les résultats
historiques de la première mission restent enregistrés plus bas.

| Exécution locale Linux | Résultat exact |
| --- | --- |
| Baseline, SDK 10.0.100, compilation Debug avec analyseurs et warnings en erreurs | 0 avertissement, 0 erreur ; exit 0 |
| Baseline C# / caméra Lua 5.1 | 69/69 C# et 22/22 caméra ; exits 0 |
| Banc C# final exécuté sur `GameplayModification.cs` de `6feaf7d` | 103 tests : 76 passed, 27 failed ; exit 1 attendu ; les 69 préexistants passent |
| Banc C# après D02 et D03 | 103 tests : 103 passed, 0 failed ; exit 0 ; 34 nouveaux cas XML |
| Nouveau banc hooks sur script global de `6feaf7d` | 34 tests : 7 passed, 27 failed ; exit 1 attendu |
| Nouveau banc hooks après D03 | 34 tests : 34 passed, 0 failed ; exit 0 |
| Caméra existante après D03 | 22 tests : 22 passed, 0 failed ; exit 0 ; script local inchangé |
| Syntaxe Lua 5.1.5 | 3 sources et 2 bancs acceptés par luac5.1 ; exit 0 |
| Compilation finale .NET 10.0.100 avec analyseurs | 0 avertissement, 0 erreur ; exit 0 |

La CI du commit de code `e2a86de6ab37ac207004439c3ea9f68d47bf185b`,
[run 38211518236](https://github.com/matla91/S6Patcher/actions/runs/38211518236),
a réussi sous Ubuntu et Windows : compilation/analyseurs, banc C# ; sous
Ubuntu syntaxe Lua/caméra/hooks, sous Windows package Release avec trimming.
Les étapes ignorées sont uniquement celles de l'autre plateforme. Les statuts
ont été vérifiés avec le SHA complet, les deux jobs et leurs étapes publiques.
Le commit `c994914` expose ensuite le décompte C# réellement mesuré dans le nom
d'une étape publique, sans déduire un résultat Windows du résultat Linux.

Les étapes publiques du
[run 38212080409](https://github.com/matla91/S6Patcher/actions/runs/38212080409),
commit `c994914b723da9ffeafae0372d7bfedfe3b3ca4b`, donnent les mesures exactes :

| Plateforme CI | Résultat C# réellement exécuté |
| --- | --- |
| Ubuntu | 103 tests, 103 passed, 0 failed |
| Windows | 104 tests, 104 passed, 0 failed |

Le test supplémentaire Windows est l'appel natif imagehlp sur le PE synthétique
déjà présent avant cette mission. La commande conserve son exit status grâce
à `set -euo pipefail`. Le nom de l'étape suivante contient uniquement la ligne
de bilan produite par le runner ; les journaux privés de GitHub ne sont pas
nécessaires pour vérifier ces décomptes. Aucune release n'est créée par la CI.
Les deux jobs de ce run sont terminés avec succès, y compris les tests Lua sur
Ubuntu et le package Windows avec trimming.

Reproduction depuis cette branche, sans jeu installé :

```sh
git submodule update --init --recursive
dotnet --version
dotnet build S6Patcher.csproj -c Debug -warnaserror -p:EnableNETAnalyzers=true
dotnet run --project Tests/S6Patcher.Tests.csproj -c Debug
luac5.1 -p Resources/Scripts/UserScriptLocal.lua
luac5.1 -p Resources/Scripts/UserScriptGlobal.lua
luac5.1 -p Resources/Scripts/MainMenuUserScript.lua
luac5.1 -p Tests/camera_spec.lua
luac5.1 -p Tests/hooks_spec.lua
lua5.1 Tests/camera_spec.lua
lua5.1 Tests/hooks_spec.lua
```

Le banc C# est toujours l'exécutable existant, pas `dotnet test` ; aucun framework
n'est ajouté. Les cas XML utilisent le vrai `GameplayModification` et le banc
des hooks charge le script global de production entier avec des API synthétiques.
La syntaxe de ce dernier reste celle de Lua 5.1.

Pour vérifier les régressions Lua sur la référence sous Bash :

```sh
git show 6feaf7d535bb35a2c8b9850e2ae9d7166237229b:Resources/Scripts/UserScriptGlobal.lua > /tmp/s6patcher-global-reference.lua
lua5.1 Tests/hooks_spec.lua /tmp/s6patcher-global-reference.lua
```

Résultat attendu : 7 réussites, 27 échecs, exit 1. La comparaison XML a utilisé
le même banc final avec uniquement le fichier C# de la référence remplacé
temporairement, sauvegarde et restauration du candidat dans `finally`. Ne pas
faire cette substitution dans un checkout contenant du travail utilisateur :
utiliser une copie de test séparée pour reproduire l'expérience.

Les scénarios couvrent les tags absents/surnuméraires/structurés, conteneurs
ambigus, canaux RGB décalés, namespaces, DTD, mappings incomplets ou d'un autre
fichier ; les hooks couvrent succès, erreur, imbrication, récursion bornée,
appels successifs, rechargement, changement de table, installation/restauration
défaillante ou ignorée, objets d'erreur et diagnostics défaillants.

Limites : aucun XML/archive/exécutable commercial utilisé, aucun test dans le
moteur réel. `SettlerLimit` et le premier `Worth` restent des contrats ordinaux
hérités. Un moteur qui refuse toute restauration ou plante n'est pas récupérable
par ce helper ; la faute n'est jamais présentée comme un succès. Les callbacks
doivent rester synchrones sous Lua 5.1. Caméra, ModLoader, patches binaires,
menu précompilé et transaction globale restent hors de cette correction.

## Référence et résultats enregistrés

Référence auditée : `fd44b9d367a0a43180465151aa8cd21a621d47aa`,
`matla91/S6Patcher/master`. Branche de développement :
`fix/reliability-audit-20261011`.

La validation du **dernier commit de code**
[`cfc8ee8ef1568ac36f35ad90f4aa196ab9ca18bc`](https://github.com/matla91/S6Patcher/commit/cfc8ee8ef1568ac36f35ad90f4aa196ab9ca18bc)
est visible dans [GitHub Actions, run 38176956069](https://github.com/matla91/S6Patcher/actions/runs/38176956069).
Les commits d'intégration et de documentation suivants ne changent ni sources
C#/Lua ni tests. Le workflow historique de release est aligné au même SDK,
mais n'a pas été déclenché (aucun tag/aucune release).

| Validation exécutée | Résultat exact | Environnement / portée |
| --- | --- | --- |
| Build Debug, `-warnaserror`, analyseurs .NET | Réussite ; 0 warning, 0 error | CI Windows et Ubuntu, SDK 10.0.100 |
| Banc C# | 70 tests, 70 passed, 0 failed ; exit 0 | CI Windows ; comprend un appel natif imagehlp.dll sur PE synthétique |
| Banc C# | 69 tests, 69 passed, 0 failed ; exit 0 | CI Ubuntu ; checksum natif Windows volontairement absent |
| Syntaxe des trois sources Lua | 3 fichiers acceptés ; exit 0 | `luac5.1 -p`, CI Ubuntu |
| Banc caméra Lua | 22 tests, 22 passed, 0 failed ; exit 0 | Lua 5.1, CI Ubuntu, API moteur simulées |
| Publish Release `win-x64`, autonome, fichier unique et trimming | Réussite ; exit 0 | CI Windows, configuration Release existante ; aucune release publiée |
| Build et bancs dans le conteneur | Build : 0 warning/0 error ; C# : 69/69 ; Lua : 22/22 | Linux, SDK 10.0.100 et Lua 5.1.5 |
| Même banc Lua livré sur le script du commit audité | 22 tests, 2 passed, 20 failed ; exit 1 attendu | Linux, comparaison de référence ; inclut exigences de récupération/opt-out |
| Cross-publish Release avec trimming dans le conteneur | Échec MSB4216/MSB4027 : task host MSBuild indisponible | Limitation locale ; la même configuration réussit en CI Windows |
| Jeux, cartes, sauvegardes, UI Avalonia interactive | **Non exécutés** | Aucun jeu installé ni EXE/archive propriétaire disponible |

Le runner signale également la dépréciation Node 20 de checkout v4, forcé en
Node 24. Cet avertissement d'action n'est pas un avertissement de compilation ;
le checkout et les deux jobs ont réussi. Les checks ne constituent pas une
validation des hooks x86 sur un EXE réel ou du rendu dans le jeu.

## Reproduire les validations automatiques

Pré-requis : Git, SDK .NET 10.0.100, sous-modules initialisés. Lua 5.1 est requis
pour les tests de scripts. Sous Windows, utiliser PowerShell ; sous Linux,
remplacer `lua5.1`/`luac5.1` par les chemins de Lua 5.1 si nécessaire.

```sh
git clone --recurse-submodules https://github.com/matla91/S6Patcher.git
cd S6Patcher
git checkout fix/reliability-audit-20261011
git submodule update --init --recursive
git rev-parse HEAD
dotnet --version
dotnet build S6Patcher.csproj -c Debug -warnaserror -p:EnableNETAnalyzers=true
dotnet run --project Tests/S6Patcher.Tests.csproj -c Debug
luac5.1 -p Resources/Scripts/UserScriptLocal.lua
luac5.1 -p Resources/Scripts/UserScriptGlobal.lua
luac5.1 -p Resources/Scripts/MainMenuUserScript.lua
lua5.1 Tests/camera_spec.lua
```

Le banc C# est un exécutable de tests, pas une suite découverte par `dotnet test`.
Chaque cas imprime PASS/FAIL et le processus retourne 1 si un cas échoue.
Il crée un dossier temporaire unique, des PE artificiels avec marqueurs connus,
des BBA de nos propres textes et des profils temporaires ; il ne recherche pas
une installation du jeu. Il nettoie ces fichiers après exécution.

Sous Windows, vérifier aussi la configuration distribuable, sans la publier :

```powershell
dotnet publish S6Patcher.csproj -c Release -r win-x64 -o development-build
Get-FileHash .\development-build\S6Patcher.exe -Algorithm SHA256
```

Lancer ensuite l'UI dans l'environnement de test isolé décrit plus bas. Le
simple démarrage du binaire ne valide pas les patches appliqués dans le jeu.

Comparaison du banc Lua avec la référence, sous Bash :

```sh
git show fd44b9d367a0a43180465151aa8cd21a621d47aa:Resources/Scripts/UserScriptLocal.lua > /tmp/s6patcher-reference.lua
lua5.1 Tests/camera_spec.lua /tmp/s6patcher-reference.lua
```

Résultat attendu de cette comparaison : 2 réussites/20 échecs, code de sortie 1.
Le banc charge le bloc de localisation/caméra du **vrai script**, pas une copie
réimplémentée. Les doubles décrivent le contrat attendu, sans prouver que toutes
les versions du moteur ont exactement ce contrat.

## Couverture et limites des fixtures

| Domaine | Vérifié automatiquement | Reste manuel ou non couvert |
| --- | --- | --- |
| EMX/gzip | Version, tailles, troncatures, limites, doublons, ressources embarquées | Corpus malformé étendu/fuzzing |
| Mappings | Éditions annoncées, marqueurs/257, génération répétable, bornes et paramètres invalides | Préimages, instructions x86 et combinaisons sur vrais EXE |
| PE et sauvegarde | Identification synthétique, abandon, commit, backup conservé, restore répété, édition incompatible | Identité cryptographique des builds ; panne matérielle/accès concurrents |
| INI/chemins | Section/clé exacte, commentaires, chemins Windows relatifs dangereux, profil Documents temporaire | UNC, jonctions et toutes les locales de Windows ; UI complète |
| Mod et BBA | Opérations synthétiques, source absente conservant ancien mod, round-trip, corruption/traversal, fermeture des streams | Archives commerciales, collisions hash, transaction d'extraction complète |
| Scripts réseau | Refus de lot différent et repli embarqué | Serveur lent, coupures à tous les instants, chaînes proxy |
| Caméra | 22 transitions/erreurs/contrôles avec mocks | Pile réelle des pages, rendu, touche maintenue, sauvegardes et cinématiques natives |
| XML, suite du 12 octobre | 34 cas : cibles complètes, absentes, multiples, structurées, namespaces et mappings invalides ; conservation en erreur | Corpus réel des éditions/mods ; identité sémantique des occurrences ordinales |
| Hooks, suite du 12 octobre | 34 scénarios : nettoyage, imbrication/récursion, erreurs originales et diagnostics protégés | API moteur réelle, callbacks synchrones et crash/refus de restauration natifs |
| Autres features | Compilation et syntaxe Lua | Graphismes, jour/nuit, campagnes, chevaliers, multijoueur et menus |

## Préparer un test Windows isolé

1. Utiliser une VM ou un compte Windows de test distinct, avec un dossier
   Documents de test et sans synchronisation des sauvegardes personnelles.
   Une copie de l'installation seule n'isole pas les profils Documents.
2. Avec une copie légalement détenue, conserver l'arborescence complète du jeu
   et la version officielle 1.71.4289. Ne pas déplacer seulement l'EXE.
   N'utiliser Steamless, lorsque l'édition l'exige selon le README original,
   que sur cette copie de test.
3. Démarrer le jeu de test une fois pour créer son profil/`Config/Options.ini`.
   Vérifier que tous les profils reconnus appartiennent à cet environnement.
   Un dossier Documents personnalisé doit contenir un profil initialisé.
4. Copier l'EXE, les INI, scripts et mods de test dans un checkpoint ; relever
   leurs tailles/SHA-256. Utiliser uniquement des sauvegardes créées pour ce test.
5. Compiler la branche, conserver le commit et le hash du patcher. Sélectionner
   explicitement l'EXE de test. Vérifier chemins/Documents/édition affichés.
   Éviter une sélection provenant d'une installation personnelle détectée.
6. Tester d'abord la configuration embarquée, puis le mode Download et des
   combinaisons d'options. Conserver un checkpoint entre les scénarios.

Cette procédure n'a pas été exécutée pendant la mission. Elle suppose un
environnement isolé autorisé ; aucun test ne doit viser une installation réelle
ou des sauvegardes personnelles sans cette préparation et autorisation.
Ne jamais committer EXE, BBA du jeu ou sauvegarde propriétaire.

## Matrice caméra — résultats à consigner

Pour chaque cas, noter édition/distribution, langue, carte, joueur, réglages,
présence du marché/habitants, autres mods et séquence exacte des touches.
Observer la caméra, les widgets, la sélection, la vitesse et les contrôles
pendant le cas et après retour au stratégique.

| Cas / actions | Résultat attendu |
| --- | --- |
| Début de carte, marché présent, aucun habitant ni bâtiment supplémentaire ; Z puis Échap | Entrée et sortie sans blocage ; caméra stratégique et contrôles utilisables |
| Carte/scénario sans marché valide ; Z | Entrée refusée avant changement caméra/UI ; diagnostic `entry rejected` ; stratégique intact |
| Français puis autres langues, dont une sans traduction FPS | Instructions complètes via repli ; aucun état partiel dû à un texte nil |
| 20 cycles Z/Z puis 20 cycles Z/Échap | Aucun empilement de pages/overlay ; widgets initialement cachés restent cachés ; stratégie utilisable à chaque sortie |
| Z maintenu 3 secondes puis relâché ; alternance rapide Z/Échap | Aucun état verrouillé ; vérifier le repeat natif et recueillir la vidéo si bascules multiples |
| Souris initialement au bord de l'écran ; entrée, sortie, nouvelle entrée | Aucun saut lié à une ancienne origine ; pas de déplacement résiduel |
| Avancer/reculer tenus ensemble puis relâcher une seule touche ; sortie en mouvement | La touche encore tenue reste prise en compte ; aucune commande FPS après sortie |
| BuildingStartStop, menu météo et commandes habituelles après sortie | Fonction originale retrouvée ; pas de mouvement FPS ; sauvegarde et Échap normaux |
| Personnage sélectionné avant Z ; sélectionner pendant FPS | Sélection retirée sans récursion ni crash ; nouvelle sélection normale après sortie |
| Pause puis vitesse accélérée avant Z | La caméra n'impose pas la vitesse 1 ; relever comportement natif si le jeu interdit l'entrée en pause |
| Cinématique/ThroneRoom démarrant pendant FPS ; animation caméra ; tentative Z/Échap | FPS libéré ; caméra cinématique préservée ; Back/Skip/clic natifs utilisables ; stratégie cohérente après fin |
| Back, Skip, clic ThroneRoom et Échap sans avoir activé FPS | Comportement identique au jeu témoin |
| Sauvegarde/chargement normal après sortie ; chargement possible pendant interruption | FPS ne reste pas actif dans la nouvelle session ; contrôles/UI corrects ; vérifier les restrictions de sauvegarde déjà présentes en FPS |
| Recommencer la carte, changer de carte/campagne, recharger plusieurs sauvegardes | Pas de bindings/callbacks hérités de la session précédente ; chaque nouvelle entrée/sortie fonctionne |
| Partie réseau ; Z | Pas d'activation FPS ; autres contrôles conservés |
| `FirstPersonCamera=0`, relancer puis Z ; réappliquer le patcher | Caméra inactive ; préférence conservée ; jour/nuit et autres features sélectionnées restent indépendants |
| Erreur de frame/activation provoquée uniquement par un script de test isolé | Diagnostic et restauration de tous les éléments possibles ; Z/Échap réessaient le nettoyage ; retirer le script de faute ensuite |

Les cases cinématiques/chargements restent particulièrement critiques : les
mocks ne connaissent pas la pile de pages ni l'ordre réel des callbacks.
Comparer systématiquement au jeu témoin dans le même environnement.

## Matrice patcher, ModLoader et fonctionnalités existantes

| Scénario sur copies de test | Résultat attendu / mesure |
| --- | --- |
| OV, HE Steam décompressée, HE Ubisoft, éditeur ; version non prise en charge et fichier tronqué | Les éditions annoncées sont reconnues ; cas invalides refusés explicitement, EXE inchangé. Reconnaissance seule ne certifie pas les sites x86 |
| Première application, même application deux fois, restauration deux fois | Première sauvegarde identique au hash initial et jamais écrasée ; restauration exacte de l'EXE initial ; sauvegarde encore présente |
| Changer une sélection binaire après application | Restaurer d'abord puis repatcher ; décocher seul ne promet pas d'annuler un patch existant |
| Sauvegarde historique et sauvegarde d'autre édition | Nom ancien reconnu ; autre édition refusée sans écriture |
| EXE verrouillé/read-only, source mod absente, disque de test saturé, dossier non inscriptible | Échec explicite ; EXE initial inchangé si Complete n'aboutit pas ; UI récupérée ; comparer aussi les fichiers auxiliaires, qui n'ont pas de transaction globale |
| Erreur d'interface injectée après Complete, uniquement dans un build de test | Message indiquant EXE déjà engagé ; aucun faux diagnostic « EXE non modifié » ; racine Documents validée conservée et interface libérée même si Dispose échoue |
| Valeurs NaN/Inf, négatives, texture < 4 ; chemin DCP à 89 puis 90 unités UTF-16 | Entrées invalides refusées ; buffer de 180 octets inclut NUL ; Unicode testé sur copie de profil |
| Documents personnalisé initialisé, puis dossier vide | Scripts/INI et DCP ciblent la même racine validée ; dossier vide refusé avant installation ; aucun profil personnel touché |
| INI avec commentaires, doublons de fragments de clé et réglages existants | Autres sections/commentaires intacts ; réglages présents conservés ; valeurs absentes initialisées |
| CRC source incorrect, BBA corrompu ou définition avec chemin dangereux | Échec clair ; ancien Bugfix Mod intact ; aucun fichier hors racine ; base/extra1 utilisateur conservés |
| Mod XML réel et plusieurs mods utilisateur | Vérifier les contrats D02 sur corpus légal : effets attendus identiques, tags absents/conflits refusés explicitement avant installation |
| Archive synthétique puis archive autorisée, extraction deux fois et pack | Contenu/CRC exacts, pas d'ancienne fin ; stream fermé. Vérifier les différences de dates ; extraction complète pas atomique |
| Download sans réseau ou lot original différent | Repli complet sur ressources embarquées, diagnostic exploitable ; correctif caméra toujours installé |
| Jour/nuit, ombres/réflexions, zoom, textures, menu/résolution/couleurs | Comparaison visuelle et stabilité au jeu témoin, une option à la fois puis combinaisons ; aucun succès enregistré à ce stade |
| Campagne originale, chevaliers/compétences, bandits, cartes et multijoueur | Vérification des scripts existants et absence de régression ; aucun changement de gameplay supplémentaire attendu |

Pour chaque échec, revenir au checkpoint. Si une panne touche scripts/INI/mods,
les remettre depuis ce checkpoint : Restore Backup ne couvre que l'EXE et les
options de nettoyage de mod proposées, pas le profil Documents entier.

## Diagnostics et critères de sortie

Recueillir le commit, les commandes/outputs, le hash du patcher et de l'EXE,
l'édition/distribution/langue, les options sélectionnées, les hashes avant/après
et `S6Patcher.log` situé à côté du patcher, ou son repli
`%LOCALAPPDATA%/S6Patcher/S6Patcher.log` si le premier dossier est non
inscriptible. Collecter stderr si les deux destinations échouent.

Le script écrit via `Framework.WriteToLog` les marqueurs `S6Patcher FPS entry
rejected`, `entry failed`, `update failed`, `recovery failed`. Recueillir le log
du jeu activé dans l'environnement de test ; sa localisation dépend de cette
configuration et n'est pas supposée ici. Ajouter étapes exactes, carte, moment,
sélection, état de marché et vidéo/capture de l'incohérence, sans partager de
fichiers propriétaires dans le dépôt.

Un résultat manuel doit indiquer PASS/FAIL/non testé et les preuves, pas seulement
« le jeu démarre ». Avant une release stable : matrice caméra passée, patches
vérifiés avec préimages/builds connus, récupération des erreurs validée, smoke
tests des features et campagnes sur toutes les éditions annoncées. Les
validations automatiques doivent rester vertes. Cette branche est destinée à
la revue et aux essais de développement, pas à une certification en jeu.
