# Corrections livrées

## Caméra — 13 octobre 2026

Depuis la base consolidée `32e5f10`, `9c25ef3` conserve `FPSMode` et les
protections précédentes. Le nettoyage marque `CleanupPending` avant ses
appels et protège ses diagnostics, même si le logger ou `tostring` échoue.
Une activation interrompue par un callback termine son bloc protégé puis
restaure toutes ses mutations ; une restauration réentrante est ignorée.
La hauteur d'entrée est alignée sur les 200 unités du déplacement existant,
et le look-at utilise la position finale de la frame. Le marqueur de
chargement `camera-engine-20261013-v1` et les transitions sont journalisés
sans dépendre du succès du diagnostic.

Neuf scénarios ciblés étendent le banc existant : 31/31 après correction,
23/31 sur la référence (les 22 anciens passent). Marché, options, autres
hooks, archives, offsets et sous-module sont conservés. Le protocole réel
est [ici](docs/FIRST_PERSON_WINDOWS_VALIDATION.md) ; ces tests ne déclarent
pas la caméra validée dans le moteur.

Le second commit `5e73c71` ajoute les corrections étayées par
[l'analyse native](docs/FIRST_PERSON_NATIVE.md) : vérifier les modes natifs à
l'entrée/sortie, libérer FPS lors d'une reprise de contrôleur en conservant
mode/cible externes, appliquer FOV avant position et lors d'un réglage sans
déplacement, restaurer la préférence d'occlusion capturée (0/1/2), protéger
les lectures de snapshots avant mutation. Le banc final passe **40/40**,
contre 31/40 sur `9c25ef3` et 23/40 sur `32e5f10`. Aucun patch binaire.

Référence : `fd44b9d367a0a43180465151aa8cd21a621d47aa` de `matla91/S6Patcher/master`.
Branche de revue : `fix/reliability-audit-20261011`. Les identifiants de problèmes
renvoient à [AUDIT.md](AUDIT.md) ; les résultats et limites de preuve figurent
dans [TESTING.md](TESTING.md).

## Suite XML/Lua du 12 octobre 2026

Branche : `fix/lua-xml-reliability-20261012`, depuis
`6feaf7d535bb35a2c8b9850e2ae9d7166237229b` de la branche de référence.

| Commit | Fichiers | Modification et raison |
| --- | --- | --- |
| `1fe43889a6d41abf10d39333ba12fb2962174f0a` | `Source/Patcher/GameplayModification.cs`, `Tests/Program.cs` | Refuser les mises à jour XML absentes, ambiguës ou incomplètes avant toute mutation ; 34 régressions dans le banc C# existant |
| `e2a86de6ab37ac207004439c3ea9f68d47bf185b` | `Resources/Scripts/UserScriptGlobal.lua`, `Tests/hooks_spec.lua`, `.github/workflows/Validate.yml` | Restaurer les hooks temporaires après succès/erreur/imbrication, préserver l'erreur et sa trace ; 34 scénarios Lua 5.1 intégrés à la CI |
| `c994914b723da9ffeafae0372d7bfedfe3b3ca4b` | `.github/workflows/Validate.yml` | Exposer la ligne de résultats réellement produite par le banc C# dans une étape CI publique ; conserver le code d'échec via pipefail |

### D02 — contrats des modifications XML

Chaque opération est liée à son archive/fichier et l'ensemble doit être
complet. Un index inconnu, une opération d'un autre fichier, un groupe absent
ou une configuration dupliquée ne peuvent plus produire un succès partiel.
Toutes les cibles sont résolues et validées sur le document original avant la
première affectation. L'erreur identifie le fichier et le contrat insatisfait ;
une erreur de parsing conserve aussi son exception interne.

| Modification existante | Contrat conservé et désormais vérifié |
| --- | --- |
| Couleur joueur | Une collection de `Color` partageant le même parent, au moins deux entrées et un canal direct unique `Red`/`Green`/`Blue` par entrée ; première entrée à zéro, deuxième personnalisée ; suivantes intactes |
| Habitants | Exactement cinq `SettlerLimit` dans l'ordre existant : 50 sans cathédrale, puis quatre valeurs d'amélioration |
| Soldats | Un seul `SoldierLimits` à exactement quatre enfants directs `Limit`, dans chacun des cinq fichiers de château existants |
| Entrepôt | Un seul `OutStockCapacities` à exactement quatre enfants directs `OutStockCapacity` |
| Dépendance de l'entrepôt | Premier `Worth` présent et scalaire mis à zéro ; les suivants restent inchangés |

Les cibles remplacées doivent être scalaires ; `InnerText` ne peut plus
supprimer un sous-élément. Pour les couleurs et les limites de soldats/entrepôts,
les tags homonymes hors des chemins documentés ne sont pas des cibles.
Le namespace par défaut reste compatible ; les homonymes
d'un autre namespace sont ignorés. DTD et résolution externe restent interdits.
Les tableaux de limites exigent les quatre valeurs fournies par l'interface.
Le contrat des `Color` est étayé par `Features/Modloader.md` et les cardinalités
des limites par les appels de `GameCustomization`.

La routine retourne un contenu complet uniquement après validation. En erreur,
les tests vérifient la conservation du tableau source et d'une sentinelle de
destination : aucun contenu partiel ne peut être remis à l'appelant pour écrire.
Les écritures/staging existants du ModLoader restent utilisés. La transaction
globale n'est pas commencée. Faute de corpus légal disponible, l'identité
sémantique des occurrences ordinales `SettlerLimit`/`Worth` reste à vérifier.
Ces deux recherches restent globales dans le namespace du document. Plusieurs
collections `Color` ou des cardinalités de limites inattendues sont refusées
conservativement, sans prétendre reconnaître ces variantes comme compatibles.

### D03 — durée de vie des hooks Lua

Seuls les remplacements temporaires de `Logic.CreateEntity` des deux callbacks
de feu de bandits sont modifiés. Chaque invocation capture sa table moteur et
sa fonction précédente ; les appels imbriqués/récursifs restaurent le hook de
leur appelant, puis l'appel extérieur restaure la fonction initiale. Installation,
callback et restauration sont protégés ; les affectations sont vérifiées, y
compris lorsqu'un setter les ignore sans lever d'erreur.

Lorsque l'installation réussit, le callback est appelé une seule fois avec son
argument inchangé. Son premier retour
reste le seul rendu, y compris `nil`/`false`. Après une faute, la restauration
est tentée avant la journalisation, puis `error(original, 0)` propage le même objet
ou message avec sa source d'origine. La trace est capturée par `xpcall` quand
`debug.traceback` est disponible. Capture, journal et conversion des erreurs
sont protégés : une faute de diagnostic ne transforme pas le gameplay en succès.

Une API refusant la restauration produit un échec explicite après succès du
callback ; en double faute, l'erreur initiale prime et la faute secondaire est
journalisée si possible. Aucun mécanisme Lua ne garantit une récupération
après crash natif ou refus de toute écriture. Les callbacks doivent rester
synchrones : `xpcall` n'est pas yieldable en Lua 5.1.
Aucun comportement volontaire des bandits/habitants/chevaliers, aucune caméra,
aucun mapping binaire et aucune logique ModLoader n'est changé.

## Commits fonctionnels et validation

| Commit du fork | Objet |
| --- | --- |
| [`603d9d16`](https://github.com/matla91/S6Patcher/commit/603d9d16b771143ae9b08667f5f1c23db192659f) | Récupération de la caméra, restauration des contrôles et banc Lua |
| [`fd3f774c`](https://github.com/matla91/S6Patcher/commit/fd3f774cf0ac55dd12d3dfe136db3032674ec72a) | Validation, écritures préparées, INI, ModLoader et fixtures C# |
| [`3ae2eb3d`](https://github.com/matla91/S6Patcher/commit/3ae2eb3d897ec46df7ce659cc2139a4fd3029574) | SDK reproductible et CI Windows/Linux |
| [`658df06b`](https://github.com/matla91/S6Patcher/commit/658df06b85fe6a750349b64ac06e3f0c5911ad70) | Routage du profil Documents personnalisé et fermeture des archives en erreur |
| [`8b534df0`](https://github.com/matla91/S6Patcher/commit/8b534df09a2951307293d1bf40b0644e9daacfab) | Vérification du package Windows Release avec trimming, sans release |
| [`5bb7e58d`](https://github.com/matla91/S6Patcher/commit/5bb7e58dbec69a63c4f080fe58825f40949c8c0c) | Diagnostic distinguant EXE engagé et erreur ultérieure de l'interface ; récupération UI même si Dispose échoue |
| [`cfc8ee8e`](https://github.com/matla91/S6Patcher/commit/cfc8ee8ef1568ac36f35ad90f4aa196ab9ca18bc) | Conservation d'un nettoyage FPS incomplet lors du rechargement de script ; test de faute supplémentaire |

Le commit documentaire qui suit ne change pas le fonctionnement du programme.
Les commits peuvent être examinés et annulés séparément. La protection des
scripts téléchargés appartient au correctif général : sans elle, le mode
Download pourrait remettre le script original et supprimer le correctif caméra.

## Première personne — C01 à C09

Fichiers : `Resources/Scripts/UserScriptLocal.lua`, `Tests/camera_spec.lua`.

- Repli anglais **par clé** quand une traduction manque. La concaténation des
  instructions ne laisse plus une activation interrompue en français.
- Vérification du marché, de coordonnées finies et des touches nécessaires
  avant modification du moteur. Sans marché valide, Z refuse l'entrée et écrit
  un diagnostic ; la présence d'habitants n'est pas requise.
- Activation et frame protégées ; une erreur déclenche la récupération. Chaque
  étape de restauration est tentée même si une autre échoue. `CleanupPending`
  permet de réessayer via Z/Échap et lors d'un rechargement du script. Si ce
  nettoyage échoue au rechargement, l'ancien état et ses callbacks restent
  disponibles au lieu d'être remplacés par un objet vide.
- La sortie est traitée avant les restrictions d'entrée. Un changement de
  `DisableFeatures`, de mode réseau ou d'état cinématique ne condamne plus Z.
- Les callbacks Back, Skip, clic ThroneRoom et Échap délèguent aux originaux
  hors première personne. La touche BuildingStartStop retrouve son binding ;
  les bindings de relâchement ajoutés pour FPS sont retirés.
- Les états de widgets modifiés sont mémorisés ; la page poussée par FPS est
  retirée une fois. Le booléen numérique 0 n'est plus interprété comme une
  demande d'affichage.
- Les coordonnées initiales de souris et les directions tenues sont remises
  à zéro. Relâcher une direction conserve l'autre encore tenue ; la durée de
  frame utilisée pour le déplacement est bornée à 0,1 seconde.
- La vitesse de simulation n'est plus forcée à 1 à l'entrée et à la sortie.
  Une animation/cinématique détectée conserve sa caméra native.

La clé INI `[S6Patcher] FirstPersonCamera=0` permet de neutraliser uniquement
cette caméra. Elle n'ajoute pas de nouvel écran au menu précompilé et conserve
le défaut activé. Les mouvements, FOV et distances déjà présents ne sont pas
étendus ; aucun contrôle de PNJ n'est ajouté.

**Preuve :** 22 scénarios du code de production avec doubles des API du jeu.
Sur le script de référence, ce même banc retourne 2 réussites et 20 échecs.
Les pages, le rendu et la propriété des callbacks du moteur ne sont pas
certifiés par ces doubles. Le témoignage utilisateur n'a pas été reproduit en
jeu : sa cause effective reste indéterminée.

## EXE, sauvegarde et orchestration — P01 à P09

| Fichiers | Changement et justification | Preuve disponible |
| --- | --- | --- |
| `ExecutableTransaction.cs`, `AtomicFile.cs`, `Patcher.cs` | EXE verrouillé et copie voisine ; patches dans cette copie ; `Complete` explicite ; `Dispose` abandonne ; première sauvegarde préparée puis remplacement final | Abandon, commit, sauvegarde initiale conservée, réapplication et refus de commit en erreur sur PE synthétiques |
| `Validator.cs` | En-têtes PE32/i386, tables et lectures bornées ; refus des marqueurs d'édition ambigus et des fichiers tronqués | Fixtures des quatre éditions, mauvais PE, troncatures et ambiguïtés |
| `Backup.cs` | Contrôle d'édition ; restauration depuis une copie ; sauvegarde conservée ; événements absents tolérés | Deux restaurations successives et refus d'une sauvegarde d'autre édition |
| `BinaryParser.cs` | Version EMX, décompression limitée à 16 MiB, tailles exactes, doublons et chaînes contrôlés ; disposal des ressources | Parsing valide/invalide et ressources réelles du dépôt |
| `Mappings.cs`, `Patcher.cs` | Marqueurs d'un octet exacts ; paramètres substitués depuis un snapshot ; conflits au même offset refusés ; écriture ne dépassant pas EOF | Résolution 257, trois éditions, répétabilité et collisions de mappings |
| `Mappings.cs`, `Patcher.cs` | Valeurs numériques finies ; texture ≥ 4, zoom/détail positifs, autosave ≥ 0 ; NUL compris dans les 180 octets du chemin DCP | Valeurs invalides et limites 89/90 unités UTF-16 |
| `CheckSumCalculator.cs` | Retour natif `IntPtr`, longueur checked et stream fermé | Appel réel d'imagehlp.dll sur PE synthétique, CI Windows |
| `MainWindow.axaml*` | Opérations ordonnées, try/catch/finally, récupération UI/streams ; fermeture empêchée pendant l'opération ; diagnostic exact si l'interface échoue après commit | Compilation/analyse ; intégration UI à vérifier sous Windows |

Le SHA-256 initial sert à détecter une modification concurrente avant remplacement.
Il n'est **pas** un hash de build connu. Les offsets et l'ordre MDL puis DCP
restent identiques ; aucune signature attendue n'a été inventée sans EXE légal
de référence. La petite fenêtre entre déverrouillage et remplacement, ainsi que
les interruptions matérielles, restent des limites.

Décocher une feature binaire déjà appliquée ne rétablit pas ses préimages.
Pour changer cette sélection, restaurer l'EXE puis repatcher. La restauration
de l'EXE ne restaure pas les fichiers Documents partagés.

## ModLoader et archives — M01 à M06

Fichiers : `Mod.cs`, `FileDataPatches.cs`, `ArchiveGuard.cs`, `Archives.cs`,
`SafePath.cs`, `MainWindow.axaml.cs` et leurs tests C#.

- Construction du Bugfix Mod hors du dossier actif ; installation seulement
  après vérification de toutes les sources, CRC et opérations. Une source
  manquante conserve l'ancien mod. Les mods utilisateur base/extra1 sont gardés.
- Remplacement, insertion et suppression ont des bornes strictes ; un opcode
  inconnu n'est plus ignoré et un remplacement hors fichier n'est plus tronqué.
- Chemins relatifs contrôlés : traversée, chemins absolus, ADS et liens détectés
  sous la racine sont refusés ; plusieurs définitions de la même cible sont
  rejetées.
- Prévalidation BBA avant le decoder unsafe : format, tailles, compteurs,
  chiffrement, CRC du répertoire, noms et plages de données. Métadonnées
  décompressées limitées à 64 MiB, fichier extrait à 512 MiB.
- Extraction par fichier temporaire avec contrôle de taille/CRC ; la sortie
  plus courte ne conserve plus les octets de fin d'une extraction antérieure.
  Packing préparé à côté de l'archive puis installé au succès.
- Streams d'archives fermés dans finally et parser du mod libéré ; un dossier
  absent est refusé. Le pack proposé par l'interface est `<dossier>.bba`.

**Risques résiduels :** les limites BBA doivent être confrontées aux archives
des éditions annoncées ; le sous-module n'est pas formellement certifié. Le
dossier d'extraction complet n'est pas transactionnel. Une faute tardive peut
laisser des fichiers précédents extraits, chacun entier. L'adaptateur ne
réapplique plus les dates de fichier via la conversion du sous-module : cette
différence de métadonnées est explicite et doit être vérifiée selon les usages
d'archives. Le remplacement du dossier Bugfix Mod a un rollback sur exception,
pas de journal persistant après coupure entre ses deux renommages.

## Configuration, chemins et téléchargements — I01 à N02

| Fichiers | Correction |
| --- | --- |
| `OptionsFile.cs`, `IOFileHandler.cs` | Édition de la clé exacte dans sa section ; commentaires, autres sections et sous-chaînes préservés ; snapshot écrit séquentiellement ; écritures temporaires par INI |
| `UserScriptHandler.cs` | Défauts ajoutés seulement si absents ; noms de script basenames ; dossiers manquants contrôlés ; profil initialisé requis |
| `MainWindow.axaml.cs`, `UserScriptHandler.cs` | Racine Documents personnalisée validée et routée avant scripts/INI ; routage précédent rétabli en cas d'échec ; DCP appliqué après MDL |
| `UserScriptHandler.cs`, `WebHandler.cs` | Lot des trois scripts téléchargés accepté uniquement s'il correspond aux SHA-256 embarqués ; sinon lot embarqué complet et diagnostic ; corps HTTP limité à 16 MiB/8 secondes, réponse disposée, repli optionnel non fatal |

Les tests vérifient l'édition INI, des chemins Windows synthétiques, le repli de
lot incompatible et le routage d'un profil temporaire. Ils ne simulent pas
toutes les pannes réseau ni tous les profils Windows réels. L'interface indique
le repli ; la conservation des préférences et le routage DCP/INI/script ensemble
restent à valider dans une installation isolée.

## Prévention des régressions

`Tests/Program.cs` et `Tests/S6Patcher.Tests.csproj` exercent l'assemblage réel.
`Properties/AssemblyInfo.cs` ouvre les seuls internes aux tests ; le projet
principal exclut les sources de `Tests/`. `global.json` épingle le SDK.
`.github/workflows/Validate.yml` compile avec warnings traités comme erreurs
et analyseurs activés, exécute les tests Windows/Linux, contrôle les scripts en
Lua 5.1 et construit le package Windows Release avec trimming. Les actions sont
épinglées par SHA, les droits limités à la lecture ; aucune release n'est créée.

Le SDK du workflow historique `Build-Publish.yml` est également fixé à
10.0.100 pour rester compatible avec global.json. Avec `latestPatch`, une
autre feature band installée par `10.0.x` seule ne suffit pas
([contrat SDK Microsoft](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json#rollforward)).
Ce workflow de release n'a pas été exécuté ; ses actions flottantes et son
absence de lien aux validations restent D09. L'épinglage des actions ci-dessus
concerne `Validate.yml`.

Résultat au commit de code `cfc8ee8e` : Windows 70/70 C#, Linux 69/69 C# et
22/22 Lua, compilations Debug à 0 avertissement/0 erreur, publication Windows
Release réussie. Voir les preuves et commandes dans [TESTING.md](TESTING.md).

Les fichiers de définitions, le menu binaire, le `mod.bba` embarqué, les offsets,
le sous-module, les licences et les assets originaux restent inchangés. Les
points D01 et D04–D09 ainsi que le mouvement RTS signalé dans l'issue #5 restent
ouverts. D02/D03 sont traités dans la suite ci-dessus, sans certification en jeu.
Aucun fichier propriétaire ou sauvegarde du jeu n'a été ajouté.
