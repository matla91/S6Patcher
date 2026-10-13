# Validation First-Person sur Windows — History Edition Steam

Pour une reprise sur un autre PC avec un autre compte Codex, commencer par
[FIRST_PERSON_REMOTE_START.md](FIRST_PERSON_REMOTE_START.md). Le workflow
`First-person Windows test package` fournit sur GitHub un package autonome
sans trimming, ses trois ressources exactes et le manifeste de commit/hashes.
La construction locale ci-dessous reste disponible pour reproduire le build.

Protocole préparé le 13 octobre 2026 pour le PC fixe Windows équipé d'une RTX
3060 12 Go. **Aucun résultat en jeu n'est enregistré dans ce document.** Les
tests Lua simulent les API du moteur ; la compilation et REA/Ghidra ne prouvent
ni le chargement du script ni le rendu dans le jeu.

La branche de travail est `fix/first-person-camera-engine-20261013`, issue de
`32e5f105effcd476c5b9a3bc82f63d944bdb1471`. Ce SHA est la base, pas le candidat
final à tester. Le sous-module `Source/Archive` doit rester à
`ec1424232f56a7b5c44d5c5acb4a200925573c45`. Reporter le SHA complet du candidat
final et les empreintes mesurées dans le compte rendu ; ne pas identifier un
build par son seul numéro de version `9.0.0.0`.

## 1. Fixer et construire le candidat

Sur le PC de construction, choisir le commit final annoncé dans le compte
rendu de mission, dans un checkout sans modifications locales. Ne pas faire
de reset d'un checkout contenant du travail. Sous PowerShell :

```powershell
. 'C:/Users/HP/Documents/Codex/Settlers6/Enter-DevEnvironment.ps1'
Set-Location 'C:/Users/HP/Documents/Codex/Settlers6/S6Patcher'
git status --short
git rev-parse HEAD
git submodule status --recursive
dotnet --version
$luaExe = Join-Path $env:SETTLERS6_TOOLS 'lua-5.1.5/lua.exe'
$luacExe = Join-Path $env:SETTLERS6_TOOLS 'lua-5.1.5/luac.exe'
& $luaExe -v
dotnet build S6Patcher.csproj -c Debug -warnaserror -p:EnableNETAnalyzers=true
if ($LASTEXITCODE -ne 0) { throw 'Compilation échouée' }
dotnet run --project Tests/S6Patcher.Tests.csproj -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Banc C# échoué' }
& $luaExe Tests/camera_spec.lua
if ($LASTEXITCODE -ne 0) { throw 'Banc caméra échoué' }
& $luaExe Tests/hooks_spec.lua
if ($LASTEXITCODE -ne 0) { throw 'Banc hooks échoué' }
foreach ($script in @('Resources/Scripts/UserScriptLocal.lua',
    'Resources/Scripts/UserScriptGlobal.lua', 'Resources/Scripts/MainMenuUserScript.lua',
    'Tests/camera_spec.lua', 'Tests/hooks_spec.lua')) {
    & $luacExe -p $script
    if ($LASTEXITCODE -ne 0) { throw "Syntaxe Lua invalide : $script" }
}
dotnet publish S6Patcher.csproj -c Release -r win-x64 -p:PublishTrimmed=false -o development-build-camera-untrimmed
if ($LASTEXITCODE -ne 0) { throw 'Publication locale échouée' }
```

Les deux exécutables doivent être Lua **5.1** ; le script d'environnement
local ne les ajoute pas au PATH, d'où les chemins explicites. Si le PC fixe
n'a pas cette arborescence d'outils, transférer le dossier
`development-build-camera-untrimmed` produit et les résultats depuis le portable, ou installer
le SDK sélectionné par `global.json` et Lua 5.1 puis adapter les chemins. Ne
pas transférer les fichiers propriétaires du jeu dans Git.

### Pourquoi publier sans trimming pour cet essai

La publication Release standard active le trimming et produit un runtimeconfig
avec `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault=false`.
Le journal HE Steam hérité appelle la sérialisation/désérialisation JSON par
réflexion dans `PatchSessionRecovery.cs`, sans métadonnées générées. Le risque
a été reproduit avec un exécutable console temporaire appelant les **vraies
méthodes** `Begin` et `Recover` de l'assemblage Release sur des fichiers texte
synthétiques : avec le même switch désactivé, les deux lèvent
`InvalidOperationException: Reflection-based serialization has been disabled
for this application`. Avec le switch activé, `Begin` réussit et `Recover`
retourne `true`. Aucun jeu ni UI n'a été lancé pour ce contrôle.

Le package trimmed standard ne convient donc pas à ce protocole HE Steam.
Utiliser le package de développement explicitement publié avec
`-p:PublishTrimmed=false`, puis vérifier son runtimeconfig généré dans
`bin/Release/net10.0/win-x64/S6Patcher.runtimeconfig.json` : la propriété JSON
doit être absente (défaut activé) ou `true`, jamais `false`. Cette adaptation
permet l'essai caméra sans modifier la récupération transactionnelle. La
correction durable du package trimmed par métadonnées JSON générées reste
un travail distinct ; le succès de ce protocole ne certifiera pas ce package.
Ne pas ajouter `development-build-camera-untrimmed` à Git : le répertoire peut
ne pas être ignoré par les règles actuelles du dépôt.

Pour attester le contenu de la livraison, conserver `git rev-parse HEAD`,
`git status --short`, `git submodule status --recursive`, la sortie des tests
et les SHA-256 de ces fichiers :

```powershell
$releaseFiles = @('development-build-camera-untrimmed/S6Patcher.exe',
    'Resources/Scripts/UserScriptLocal.lua', 'Resources/Scripts/UserScriptGlobal.lua',
    'Resources/Scripts/EMXBinData.s6patcher')
$releaseFiles | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 }
```

Copier les trois ressources dans le dossier de preuves de l'essai, avec leurs
empreintes. La version Release embarque ces ressources ; le patcher livré et
ces sources doivent provenir de la même construction, sans édition entre les
deux. Calculer à nouveau le hash de `S6Patcher.exe` après transfert au PC fixe.

## 2. Isoler l'installation et Documents

Fermer le jeu et S6Patcher. Utiliser un compte Windows de test distinct, avec
ses propres Documents et sans synchronisation des sauvegardes personnelles.
Une copie de l'installation n'isole pas le profil. Dans ce compte, lancer le
jeu officiel une fois pour créer `Config/Options.ini`, puis le fermer.

Créer une arborescence courte et neuve, par exemple :

```text
C:\S6FPS\Game\                         copie intégrale du jeu
C:\S6FPS\Documents\                    racine Documents isolée
C:\S6FPS\Documents\THE SETTLERS - Rise of an Empire\Config\Options.ini
C:\S6FPS\Checkpoint\                   copie avant application
C:\S6FPS\Evidence\                     hashes, résultats, logs et captures
C:\S6FPS\Patcher\                      livraison du commit candidat
```

Copier **toute** l'installation Steam dans `Game`, en conservant
`Data/base/bin/Settlers6R.exe` et les archives/répertoires associés. Copier
uniquement le profil du compte de test, avec son nom réel, dans `Documents`.
Le patcher reconnaît les répertoires contenant `Rise of an` ou `Aufstieg eines`
et peut écrire dans chacun d'eux : inventorier les profils présents et n'y
placer aucun profil personnel. L'option Documents attend la racine
`C:\S6FPS\Documents`, et non `...\Config` ni `...\Script`.

Confirmer la distribution **History Edition Steam**, la langue et la version
de fichier de l'EXE. La validation actuelle attend `1, 71, 4289, 0`. Conserver
la version de Windows, celle du pilote NVIDIA, la résolution, le mode
plein écran/fenêtré, la disposition clavier et les options Steam de lancement.

### Préparer l'EXE Steam de test

Avant toute préparation, copier l'EXE Steam original du PC fixe dans
`Evidence/Settlers6R.steam-original.exe` et calculer son SHA-256. Préparer
uniquement la **copie de test** avec Steamless lorsque le validateur exige
un EXE décompressé. Enregistrer la version de Steamless et le SHA-256 de son
résultat, puis placer ce résultat sous le nom attendu :
`C:\S6FPS\Game\Data\base\bin\Settlers6R.exe`.

Ne pas remplacer l'EXE Steam installé dans `Program Files`. La copie
`analysis/binaries/Settlers6R.exe.unpacked.exe` du portable est une cible
d'analyse et n'est pas une installation déployée sur le PC fixe. Ses adresses
et hashes ne sont pas ceux de l'EXE Steam original. Ne pas supposer que le PC
fixe possède le même build sans comparer les empreintes.

## 3. Prendre un checkpoint complet

Avant application, sauvegarder intégralement `Game` et `Documents` dans
`Checkpoint/Game` et `Checkpoint/Documents`. Cela couvre l'EXE préparé,
ses sauvegardes éventuelles, tous les INI/scripts/mods et les sauvegardes de
test, ainsi que l'absence initiale de fichiers qui seront créés. Un simple
backup d'EXE n'est pas suffisant.

Les exemples suivants supposent que `C:\S6FPS` a été créé pour cet essai et que
les destinations n'existent pas encore :

```powershell
$caseRoot = 'C:\S6FPS'
$gameRoot = Join-Path $caseRoot 'Game'
$documentsRoot = Join-Path $caseRoot 'Documents'
$checkpointRoot = Join-Path $caseRoot 'Checkpoint'
$evidenceRoot = Join-Path $caseRoot 'Evidence'
New-Item -ItemType Directory -Path $checkpointRoot -ErrorAction Stop
New-Item -ItemType Directory -Path $evidenceRoot -Force
Copy-Item -LiteralPath $gameRoot -Destination (Join-Path $checkpointRoot 'Game') -Recurse -ErrorAction Stop
Copy-Item -LiteralPath $documentsRoot -Destination (Join-Path $checkpointRoot 'Documents') -Recurse -ErrorAction Stop

function Get-TestManifest([string] $root) {
    Get-ChildItem -LiteralPath $root -File -Recurse -Force | ForEach-Object {
        [pscustomobject]@{
            RelativePath = $_.FullName.Substring($root.TrimEnd('\').Length + 1)
            Length = $_.Length
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    } | Sort-Object RelativePath
}
Get-TestManifest $gameRoot | Export-Csv (Join-Path $evidenceRoot 'game-before.csv') -NoTypeInformation -Encoding utf8
Get-TestManifest $documentsRoot | Export-Csv (Join-Path $evidenceRoot 'documents-before.csv') -NoTypeInformation -Encoding utf8
```

Conserver le checkpoint hors des deux racines modifiées. Vérifier les copies
par les mêmes manifests avant de commencer ; consigner aussi les dossiers
vides si leur présence importe au scénario. Les exemples ne suppriment ni
ne déplacent une installation personnelle.

## 4. Appliquer et prouver le déploiement

Ouvrir la livraison `Patcher/S6Patcher.exe`. Sélectionner explicitement
`Game/Data/base/bin/Settlers6R.exe` ; vérifier le chemin complet et l'édition
affichée. Choisir **Use local embedded Resources**, cocher **Update Documents
Folder Path** et saisir `C:\S6FPS\Documents` (chemin absolu, au plus 89 unités
UTF-16 avec le buffer actuel). Laisser **Only update Script and Mod files**
décoché : ce mode ne réapplique pas la redirection DCP nécessaire au premier
essai. Noter/capturer toutes les options sélectionnées. Pour isoler la caméra,
ne pas ajouter de changement de gameplay ou de nouveau réglage graphique.

Appliquer ; attendre la fin et fermer le patcher. Relever les SHA-256 de
l'EXE après patch, `Settlers6R.backup`, `Options.ini` et les trois fichiers
du répertoire `Script`. Chaque script doit être identique à la ressource de
la livraison (hash, pas seulement présence ou date) :

```powershell
$profileRoot = Join-Path $documentsRoot 'THE SETTLERS - Rise of an Empire'
$resourceRoot = Join-Path $evidenceRoot 'Resources' # les 3 ressources transférées
foreach ($name in @('UserScriptLocal.lua', 'UserScriptGlobal.lua', 'EMXBinData.s6patcher')) {
    $expected = (Get-FileHash -LiteralPath (Join-Path $resourceRoot $name) -Algorithm SHA256).Hash
    $actual = (Get-FileHash -LiteralPath (Join-Path $profileRoot "Script\$name") -Algorithm SHA256).Hash
    if ($actual -ne $expected) { throw "Script déployé différent : $name" }
}
```

Adapter `$profileRoot` au nom réel. Vérifier `[S6Patcher] FirstPersonCamera`
dans `Config/Options.ini` : la clé absente signifie activé ; `0` désactive
l'entrée FPS. Ne pas confondre ce réglage avec un échec de caméra.

Le téléchargement des scripts est un contrat de **lot complet** :
`UserScriptLocal.lua`, `EMXBinData.s6patcher` et `UserScriptGlobal.lua` doivent
tous correspondre aux SHA-256 embarqués. Si un seul fichier diffère/manque,
le patcher écrit le lot embarqué et journalise
`Online scripts are missing or differ from this build. Using the complete bundled script set to preserve compatibility.`
Il ne mélange pas ancien script et correction. Après le scénario embarqué,
on peut refaire un essai Download depuis le checkpoint : mêmes trois hashes
attendus, avec preuve du repli si le lot distant est incompatible. Les
téléchargements de mods ont un contrat distinct ; ne pas en déduire leur
identité à partir des hashes de scripts.

### Prouver que le moteur charge ce script

Lancer directement **l'EXE patché de test** ; Steam peut devoir rester ouvert.
Vérifier le chemin du processus lancé dans le Gestionnaire des tâches. Un
raccourci Steam qui relance l'installation officielle invalide cet essai.
Avant le lancement, relever `$sessionStarted = Get-Date` dans PowerShell.
Ouvrir une nouvelle partie solo et rechercher dans le journal du jeu :

```text
S6Patcher FPS script loaded: camera-engine-20261013-v1
```

Ce marqueur est écrit par le bloc caméra du script local via un appel protégé
à `Framework.WriteToLog`. Il atteste son exécution ; les hashes attestent
le fichier déployé et le commit conservé atteste la livraison. La destination
du journal moteur n'est pas encore vérifiée et dépend de la configuration :
ne pas supposer qu'il s'agit de `S6Patcher.log`.

Consigner l'heure de lancement, puis rechercher les fichiers `*.log` modifiés
depuis cette heure dans le profil de test, ses Documents Windows et
l'installation de test. Chercher le marqueur exact dans les logs texte.
Par exemple, après la session, dans le compte Windows de test :

```powershell
$windowsDocumentsRoot = [Environment]::GetFolderPath('MyDocuments')
$logCandidates = @($gameRoot, $documentsRoot, $windowsDocumentsRoot) |
    Select-Object -Unique | ForEach-Object {
        Get-ChildItem -LiteralPath $_ -Filter '*.log' -File -Recurse -Force -ErrorAction SilentlyContinue
    } | Where-Object LastWriteTime -ge $sessionStarted | Sort-Object FullName -Unique
$logCandidates | Select-Object FullName, Length, LastWriteTime
$logCandidates | Select-String -SimpleMatch 'S6Patcher FPS script loaded: camera-engine-20261013-v1'
```

Si nécessaire, déterminer/activer la journalisation du jeu et relancer une
partie. **Absence de marqueur sans log moteur disponible = chargement non
prouvé**, pas preuve que le script ne s'est pas exécuté. Ne pas valider un
essai avec seulement les messages d'écriture du patcher.

## 5. Matrice en jeu à remplir

Enregistrer une capture/vidéo courte par anomalie et un bilan PASS/FAIL/non
testé avec carte, joueur, heure et séquence des touches. Après chaque sortie,
tester immédiatement défilement, rotation/zoom RTS, sélection, ordres,
minicarte, menus et widgets. Comparer avec l'état avant entrée, pas seulement
avec une caméra qui semble bouger.

| Cas | Actions et observations requises |
| --- | --- |
| Référence RTS | Avant Z : observer position/zoom, sélection, UI, vitesse du jeu et réglages de rendu. Noter les six touches locales affichées par le mode FPS ; ne pas supposer W/S sur un clavier français. |
| Début de partie | Nouvelle carte, marché déjà présent, aucun habitant arrivé. Appuyer sur Z dès que les entrées de la carte sont disponibles. Confirmer une position valide près de l'approche du marché, hauteur terrain et absence de blocage. Ne pas construire un marché. |
| Orientation | Souris horizontalement et verticalement, petits/grands mouvements, retour aux bords de l'écran. Vérifier yaw/pitch, absence de saut au premier mouvement et comportement après Alt-Tab. |
| Déplacement | Avancer/reculer, relâcher chaque touche ; maintenir les deux puis relâcher successivement. Tester terrain en pente et limite de secteur/carte accessible. Aucun mouvement ne doit persister après relâchement ou sortie. |
| Z / Z | Entrer avec Z, bouger, sortir avec Z. RTS, UI et fonctions des touches d'origine restaurés ; nouvelle activation possible. |
| Z / Échap | Réactiver puis Échap. Sortie complète ; un nouvel Échap retrouve le comportement normal du jeu, sans page ThroneRoom résiduelle. |
| 20 cycles | Numéroter 1 à 20, alterner sorties Z et Échap ; bouger à chaque cycle et relâcher les touches avant le suivant. Vérifier RTS/UI entre chaque paire, pas seulement à la fin. |
| Réglages FPS/rendu | Modifier FOV et distance avec les touches affichées, sortir et comparer ciel, brouillard de guerre, frontières, occlusion, sons/alertes et vue RTS aux valeurs de départ. Tester au moins deux configurations graphiques déjà disponibles, sans ajouter de feature. |
| Sortie pendant mouvement | Maintenir avancer, sortir par Z puis par Échap dans un second essai ; relâcher hors FPS et réactiver. Pas de direction bloquée ni d'ordre de jeu parasite. |
| Menus et sélection | Hors FPS, les six touches reprennent météo, start/stop, amélioration, sentier, route et production selon leur mapping. Tester sélection d'un bâtiment et UI. En FPS, aucune sélection ou commande contextuelle involontaire. |
| Cinématique/animation | Carte de test avec un déclencheur connu : interruption FPS par cinématique/animation caméra ; contrôler sa fin, Échap/skip si permis, retour RTS et réactivation. Consigner non testé si aucun déclencheur n'est disponible. |
| Sauvegarde/rechargement | Sauvegarde de test RTS puis rechargement ; nouvelle activation. Essayer la commande de sauvegarde FPS et noter le comportement prévu (bloquée par le hook actuel). Recharger une sauvegarde de test et changer/recommencer la carte ; confirmer marqueur, bindings, UI et absence d'état FPS résiduel. |
| Rechargement Lua | Si un mécanisme local sûr et connu existe, recharger le script pendant FPS puis retester ; consigner la méthode. Un simple chargement de sauvegarde ne prouve pas un rechargement dans le même état Lua. Sinon non testé ; ne pas inventer de commande console. |
| Désactivation optionnelle | Jeu fermé, mettre `FirstPersonCamera=0` sous `[S6Patcher]`, relancer : Z ne doit pas entrer en FPS, les autres fonctions restent disponibles. Remettre la valeur puis relancer. |
| Smoke tests | Caméra RTS prolongée, menu/résolution, sauvegarde/chargement, hooks existants sur une carte pertinente. Rapporter séparément les scénarios non accessibles pendant cet essai. |

Si le tout début avant habitants est trop court à reproduire, relancer la
carte et enregistrer l'instant exact ; ne pas déclarer ce cas réussi à partir
d'une ville déjà peuplée. Les pannes moteur natives et récupérations simulées
restent couvertes séparément par les bancs : ne pas modifier le jeu réel pour
injecter arbitrairement une erreur.

## 6. Collecter les preuves et restaurer

Conserver les manifests après application et après session, commit et hashes,
logs complets de construction/tests, options patcher, carte et séquence de
touches, captures et paramètres Windows/NVIDIA. `S6Patcher.log` est à côté du
patcher, avec repli vers `%LOCALAPPDATA%\S6Patcher\S6Patcher.log` si le dossier
principal n'est pas inscriptible. Capturer aussi les diagnostics stderr si
aucun journal n'est disponible. Dans les logs moteur, conserver le marqueur
de chargement et les messages `S6Patcher FPS` d'entrée/sortie/interruption et
les fautes `entry rejected`, `entry failed`, `update failed`, `recovery failed`.
Un appel protégé qui réussit ne prouve pas que le moteur a appliqué l'effet.

History Edition Steam possède un journal de session voisin de l'EXE :
`Settlers6R.exe.s6patcher-session`. Il sauvegarde EXE, INI, les trois scripts
des profils reconnus et `modloader/shr` avant patch. Un échec avant validation
déclenche la récupération ; une session interrompue est récupérée à la
prochaine sélection de l'EXE. Conserver ce dossier et les logs s'il reste
présent ; ne pas le supprimer avant diagnostic. Cette protection ne remplace
pas le checkpoint externe et n'est pas une fonction d'annulation après patch
réussi. La création de la première sauvegarde `.backup` n'est pas incluse
dans les cibles du journal.

**Restore Backup** restaure l'EXE depuis `Settlers6R_BACKUP.exe` (ancien nom,
prioritaire s'il existe), sinon `Settlers6R.backup` ; la sauvegarde est conservée.
Il ne remet pas les INI/scripts du profil à l'état initial. Répondre **Yes**
à `Keep Mod files in separate folder?` pour préserver les mods : **No** peut
supprimer tout `modloader`, y compris les mods utilisateur. Pour revenir
exactement au checkpoint, fermer jeu/patcher et restaurer les deux racines
complètes ; cela annule aussi les fichiers nouvellement créés.

Exemple de restauration conservant l'état défaillant, pour les deux seules
racines explicitement réservées à cet essai :

```powershell
$caseRoot = (Resolve-Path -LiteralPath 'C:\S6FPS').Path.TrimEnd('\')
$suffix = Get-Date -Format 'yyyyMMdd-HHmmss'
foreach ($name in @('Game', 'Documents')) {
    $target = [IO.Path]::GetFullPath((Join-Path $caseRoot $name))
    $baseline = Join-Path $caseRoot "Checkpoint\$name"
    $preserved = Join-Path $caseRoot "$name-failed-$suffix"
    if ([IO.Path]::GetDirectoryName($target) -ne $caseRoot -or
        $target -notin @((Join-Path $caseRoot 'Game'), (Join-Path $caseRoot 'Documents'))) {
        throw 'Cible hors des racines de test autorisées'
    }
    if (-not (Test-Path -LiteralPath $baseline -PathType Container) -or
        (Test-Path -LiteralPath $preserved)) { throw 'Checkpoint absent ou destination déjà présente' }
    Move-Item -LiteralPath $target -Destination $preserved -ErrorAction Stop
    Copy-Item -LiteralPath $baseline -Destination $target -Recurse -ErrorAction Stop
}
```

Comparer les manifests restaurés aux manifests `before`. L'EXE préparé du
checkpoint peut toujours avoir besoin d'être repatché pour la redirection
Documents ; ne pas le lancer en croyant que la redirection précédente subsiste.
Le compte Windows de test garantit alors l'absence d'accès au profil personnel.
Pour un nouvel essai, reprendre à l'application depuis le checkpoint. Les
répertoires `*-failed-*` restent disponibles pour l'analyse ; ne pas les
committer ni partager les binaires/sauvegardes propriétaires.

## Compte rendu minimal

| Information | Valeur à renseigner |
| --- | --- |
| Commit candidat complet / état Git / sous-module | |
| SHA-256 patcher transféré et trois ressources | |
| Distribution, version/langue ; SHA-256 Steam original/préparé/patché | |
| Windows, RTX 3060 12 Go, pilote, écran/clavier | |
| Chemin réel du processus et profil isolé | |
| Carte, joueur, marché présent, habitants au premier Z | |
| Options et hashes des scripts déployés | |
| Log moteur et preuve du marqueur chargé | |
| Chaque cas de matrice : PASS / FAIL / non testé | |
| Retour checkpoint et comparaison des manifests | |

Le statut **validé en jeu** exige des résultats observés sur le moteur réel,
avec chargement attesté, pour les entrées/sorties Z/Échap, vingt cycles et
restauration RTS/UI. Les cinématiques/rechargements non exécutés doivent
rester explicitement non testés, même si les bancs simulés sont verts.
