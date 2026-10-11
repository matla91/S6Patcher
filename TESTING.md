# Validation reproductible

## Référence et résultats enregistrés

Référence auditée : `fd44b9d367a0a43180465151aa8cd21a621d47aa`,
`matla91/S6Patcher/master`. Branche de développement :
`fix/reliability-audit-20261011`.

La validation du **dernier commit de code**
[`cfc8ee8ef1568ac36f35ad90f4aa196ab9ca18bc`](https://github.com/matla91/S6Patcher/commit/cfc8ee8ef1568ac36f35ad90f4aa196ab9ca18bc)
est visible dans [GitHub Actions, run 38176956069](https://github.com/matla91/S6Patcher/actions/runs/38176956069).
Le commit documentaire ultérieur ne change ni sources ni tests.

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
| Mod XML réel et plusieurs mods utilisateur | Fichiers cibles et effets attendus identiques ; consigner tags absents/conflits, encore ouverts |
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
et `S6Patcher.log` situé à côté du patcher. Le journal peut manquer si ce dossier
est non inscriptible : défaut encore ouvert, à noter dans le résultat.

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
