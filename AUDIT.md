# Audit technique — première étape de fiabilisation

Date de mission : 11 octobre 2026. Fork confirmé par l'API GitHub :
[`matla91/S6Patcher`](https://github.com/matla91/S6Patcher), parent
`Eisenmonoxid/S6Patcher`. Départ : `master` à
[`fd44b9d367a0a43180465151aa8cd21a621d47aa`](https://github.com/matla91/S6Patcher/commit/fd44b9d367a0a43180465151aa8cd21a621d47aa).
Travail publié sur `fix/reliability-audit-20261011` ; aucun changement du dépôt
original, de `master`, de tag ou de release.

## Portée et niveau de preuve

L'audit couvre les sources C#, les trois sources Lua, les ressources et les
formats EMX, les mappings des quatre éditions, le sous-module S6Packer épinglé,
la configuration Release, les workflows, le README, les documents Features et
l'historique pertinent. [ARCHITECTURE.md](ARCHITECTURE.md) détaille les mécanismes.

Le jeu et ses EXE/archives de référence sont absents. Les tests utilisent des
API du jeu simulées, des PE artificiels et des BBA contenant nos propres textes.
Il n'y a donc ni reproduction **en jeu** du témoignage, ni validation complète
des instructions x86 injectées ou de l'équilibre visuel après une cinématique.
Une compilation ou une CI verte ne fournit pas cette preuve.

| Classe | Signification |
| --- | --- |
| B : bug démontré | Branche de code incorrecte ou échec reproductible avec fixture/API simulée ; préciser la portée |
| R : risque justifié | Mécanisme dangereux identifié, sans incident joueur observé ou sans validation native complète |
| D : dette technique | Coût de maintenance ou faiblesse de traçabilité sans conséquence immédiate prouvée |
| H : hypothèse en jeu | Explication ou interaction à vérifier avec moteur, version et scénario précis |

Gravité : P0 peut bloquer la caméra ou compromettre l'EXE ; P1 altère les contrôles,
la récupération ou la compatibilité ; P2 touche les réglages/diagnostics ; P3
concerne surtout la maintenance. Fréquences ci-dessous estimées à partir des
préconditions du code, sans télémétrie joueur. Coût : faible < une journée,
moyen quelques jours, élevé ou indéterminé lorsqu'il manque le jeu ou un corpus.

## Caméra première personne

| ID | Classe / priorité | Preuve au commit audité | Fréquence probable / coût | Traitement |
| --- | --- | --- | --- | --- |
| C01 | B / P0 | `GetLocalizedText` retourne nil pour Increase/Decrease/Exit en français, polonais, néerlandais et russe ; `Enable` concatène ensuite ces valeurs après avoir changé le moteur | Systématique dans ces langues / faible | Repli par clé et activation avec récupération |
| C02 | B / P0 | `ToggleGameWidgets(0)` exécute `if _show then` : en Lua, 0 est vrai ; une page est poussée à la sortie | Chaque sortie / faible | Page possédée, retrait unique, états de widgets mémorisés |
| C03 | B / P0 | `Enable` change le rendu et la caméra avant de demander l'approche de `GetMarketplace`; marché nul ou erreur d'API laisse une entrée partielle avec Enabled=false | Début de certaines cartes / faible | Prévalidation et abandon sans mutation si origine invalide |
| C04 | B / P1 | `Toggle` refuse aussi la sortie lorsque g_Throneroom, animation, réseau ou DisableFeatures change ; Échap appelle ce même Toggle | Interruption / faible | Sortie prioritaire, restrictions limitées à l'entrée |
| C05 | B / P1 | Back, Skip et ThroneRoomLeftClick sont remplacés par des fonctions vides même hors FPS | Cinématique/ThroneRoom après chargement du script / faible | Délégation aux callbacks sauvegardés hors FPS |
| C06 | B / P1 | La touche de BuildingStartStop conserve le mouvement arrière après Disable | Chaque session FPS / faible | Restauration du KeyBindDown et retrait des KeyBindUp FPS |
| C07 | B / P2 | LastMouseX/Y commencent à 0 ; IsMoving n'est pas remis à zéro ; relâcher une direction arrête l'autre encore tenue | Souris éloignée de l'origine, bascules/accords de touches / faible | Origine souris et touches tenues réinitialisées ; relâchement cohérent |
| C08 | B / P2 | Enable et Disable imposent GameTimeSetFactor(...,1), écrasant pause/accélération existante | Joueur ayant modifié la vitesse / faible | La caméra ne modifie plus la vitesse de simulation |
| C09 | B / P1 | Exceptions de frame/activation et rechargement du script n'ont aucun nettoyage structuré | API en erreur ou rechargement / moyen | Restauration indépendante de chaque étape, diagnostic et possibilité de réessayer |
| C10 | H / P0 | Témoignage utilisateur : blocage avant les habitants, sortie partielle via Échap | Fréquence inconnue / essais en jeu | Causes C01–C04 plausibles et corrigées ; cause effective non attribuée |
| C11 | H / P1 | Partage de ThroneRoom et de sa pile de pages avec cinématiques/dialogues ; états de rendu sans getters documentés dans le dépôt | Campagnes, cinématiques, chargements / moyen | Caméra native préservée si interruption détectée ; widgets et pile à valider en jeu |

Le banc livré, exécuté aussi sur le script du commit audité, reproduit
**20 échecs sur 22 cas** (2 réussites). Après correction, ses 22 cas réussissent.
Il inclut des exigences de récupération et de désactivation, pas 20 causes
indépendantes du témoignage. L'absence d'habitants
n'est pas une condition d'activation dans le code ; l'absence d'un marché valide
est un scénario distinct. Aucun lien certain entre le témoignage et une langue,
un marché ou une cinématique n'est affirmé.

## Patcher, sauvegardes et versions

| ID | Classe / priorité | Preuve et conséquence | Fréquence / coût | Traitement |
| --- | --- | --- | --- | --- |
| P01 | B / P0 | `WriteBytes` écrit directement dans l'EXE et absorbe les erreurs ; une suite de mappings peut être appliquée partiellement | Échec disque, offset ou exception / moyen | Copie de travail, erreurs propagées, Complete explicite et remplacement final |
| P02 | B / P1 | `MainPatchingTask` n'a pas de finally ; échec asynchrone laisse UI/streams dans un état de patching | Toute exception non absorbée / faible | try/catch/finally, opérations ordonnées, fermeture différée pendant le traitement |
| P03 | B / P1 | `Validator` compare longueur avec offset seul puis lit offset+taille ; les fichiers tronqués passent ce test insuffisant | Fichier incomplet / faible | Bornes complètes et diagnostic de version non prise en charge |
| P04 | R / P0 | Une chaîne de version à un offset et deux octets Steam ne certifient pas les sites x86 ; aucun hash d'EXE ni octet attendu par patch dans Definitions.bin | Build différent/autre modification / moyen avec corpus | PE32/i386 et ambiguïtés contrôlés ; **préimages/hashes restent ouverts** |
| P05 | B / P1 | Sélection crée la sauvegarde avant validation ; Restore utilise File.Replace sur l'unique sauvegarde, qui est consommée | Première sélection/restauration / faible | Sauvegarde au commit ; copie à la restauration ; contrôle d'édition |
| P06 | B / P1 | Substitutions recherchent seulement Value[0] ; une résolution 257 transforme le premier marqueur en octet 1 puis réutilise le mauvais offset | Valeurs particulières / faible | Marqueurs exacts, substitutions sur snapshot et contrôle d'unicité |
| P07 | B / P1 | Limite DCP vérifie les octets du chemin mais omet le NUL UTF-16 ; message parle de caractères alors que limite=180 octets | Limite du buffer / faible | Terminaison incluse, maximum de 89 unités UTF-16 |
| P08 | B / P2 | NaN, infinis et valeurs négatives peuvent être parsés puis générés en paramètres binaires | Saisie invalide / faible | Valeurs finies et domaines non négatifs/positifs contrôlés |
| P09 | R / P1 | Déclaration int de CheckSumMappedFile alors que Windows retourne PIMAGE_NT_HEADERS ; ABI incorrecte sur x64 | Adresse native particulière / faible | IntPtr, taille checked, fermeture du stream ; appel Windows testé sur PE synthétique |
| P10 | R / P1 | Pas de transaction commune EXE + scripts + INI + mod, ni journal résistant à une panne pendant les deux renommages d'un dossier | Échec tardif/panne / moyen | Écriture atomique par fichier, retour arrière des dossiers sur exception ; **transaction globale ouverte** |
| P11 | R / P2 | Une feature binaire décochée ne rétablit pas les octets d'origine déjà patchés | Réapplication avec options différentes / moyen | Comportement antérieur conservé ; restoration EXE et repatch documentés |

Les offsets des quatre éditions ne sont pas modifiés. L'identification ne doit
pas être présentée comme une certification des builds non connus. Les tests
d'idempotence portent sur une même feature et la sauvegarde initiale, pas sur
la réversibilité de toutes les combinaisons de patches.

## ModLoader, configuration et ressources

| ID | Classe / priorité | Preuve et conséquence | Fréquence / coût | Traitement |
| --- | --- | --- | --- | --- |
| M01 | B / P1 | OV supprime `modloader/shr` avant de générer tous les fichiers ; HE écrit chaque résultat dans le dossier actif | Source manquante, CRC ou E/S / moyen | Génération isolée, installation seulement sans erreur ; fixture d'échec conserve l'ancien mod |
| M02 | B / P1 | ReplaceRange raccourcit silencieusement un remplacement au-delà de EOF ; opérations inconnues ignorées | Définition invalide / faible | Opérations et bornes strictes, parsing limité et exact |
| M03 | R / P1 | Chemins téléchargés passés à Path.Combine sans contrôle de racine, `..`, ADS ou liens | Définition/archives invalides / faible | SafePath et prévalidation des entrées ; fixture de traversal refusée |
| M04 | B / P1 | Extraction du sous-module utilise File.OpenWrite : une sortie plus courte laisse une ancienne fin de fichier | Réextraction / faible | Écriture temporaire puis remplacement, test de round-trip |
| M05 | R / P1 | Decoder BBA unsafe consomme longueurs, compteurs et buffers chiffrés sans prévalidation complète | Archive tronquée/corrompue / moyen | ArchiveGuard : tailles, CRC de répertoire, chemins, fichiers ; tests synthétiques de corruption |
| M06 | B / P2 | Une exception après la construction d'un BBA laisse son stream enregistré ouvert ; nom de sortie de pack utilise le parent du dossier choisi | Archive invalide / faible | finally sur lectures ; sortie `<dossier>.bba` au lieu du parent |
| I01 | B / P1 | `Lines.RemoveAll(Line.Contains(Key))` supprime des autres sections, commentaires et clés contenant un même fragment | Réglages existants / faible | Édition de la clé exacte dans sa seule section |
| I02 | R / P1 | Parallel.ForEach modifie son dictionnaire de fichiers pendant son énumération, sans verrou | Plusieurs profils / faible | Snapshot et écriture séquentielle |
| I03 | B / P2 | Réapplication remet toutes les features script à leurs défauts, écrasant les réglages in-game | Toute réapplication / faible | Initialisation seulement des clés absentes |
| I04 | B / P1 | Le chemin DCP choisi est appliqué après installation des scripts, sans mettre GlobalDocuments à jour | Documents redirigés / faible | Même racine validée pour script, INI et DCP ; profil vide refusé |
| I05 | B / P2 | Directory.GetDirectories d'un dossier inexistant et parcours de parents/missing directories sans garde produisent des exceptions génériques | Premier lancement/mauvais chemin / faible | Dossiers absents et chemins trop courts gérés explicitement |
| N01 | R / P1 | Les scripts en ligne proviennent de master du dépôt original, peuvent remplacer les corrections du fork et ne sont pas versionnés avec l'EXE | Mode Download, activé par défaut / faible | Lot complet identique aux ressources de cette compilation, sinon repli documenté |
| N02 | R / P2 | Timeout HTTP ne borne pas la copie du corps avec ResponseHeadersRead ; réponses non disposées et repli compté comme fatal | Réseau lent/interrompu / faible | Timeout du corps, limite 16 MiB, disposal et repli non fatal |

ArchiveGuard protège les points d'entrée de S6Patcher sans modifier le dépôt
du sous-module. Il n'est pas un audit formel de tout le decoder unsafe et ne
prouve pas la compatibilité des CRC/variantes sur chaque archive du jeu réel.
Les extractions ne sont pas transactionnelles au niveau du dossier complet :
une corruption de données tardive peut laisser les fichiers précédents extraits.
Le nom/date des fichiers extraits n'a pas de validation de métadonnées équivalente
à celle d'un EXE ; les dates historiques ne sont plus réappliquées par l'adaptateur.

## Dette et risques encore ouverts

| ID | Classe / priorité | Justification | Validation ou intervention |
| --- | --- | --- | --- |
| D01 | D / P2 | Menu source et `EMXBinData.s6patcher` sans générateur déterministe dans le dépôt | Documenter puis reproduire compilation/minification du menu |
| D02 | R / P1 | `GameplayModification.UpdateTags` ignore une occurrence XML absente ; sélection par ordre global des tags | Tests XML de fichiers isolés et erreur explicite sur contrat non satisfait |
| D03 | R / P1 | Les wrappers feu des bandits changent Logic.CreateEntity puis la restaurent seulement si le callback ne lève pas d'erreur | Test de faute Lua et restauration garantie sans masquer l'erreur |
| D04 | R / P2 | Table de hash S6Packer utilise Offset=0 comme sentinelle alors qu'une première entrée peut avoir cet offset ; collisions possibles | Fixture de collision et correction locale/évolution du sous-module avec autorisation de son fork |
| D05 | R / P2 | Logger ouvre le fichier à côté de l'application et ignore l'échec ; journal peut être absent dans un dossier non inscriptible | Destination utilisateur de repli et tests d'E/S |
| D06 | R / P2 | Lecture des profils par noms anglais/allemand, modification de plusieurs profils ; dispositions non standard non certifiées | Sélection explicite d'un profil, chemins Unicode/UNC/jonctions en environnement Windows |
| D07 | D / P3 | Singletons, ErrorTracking global, signatures sans manifests lisibles, paramètres numériques sans tests GPU | Isoler seulement les frontières nécessaires, conserver une matrice de compatibilité |
| D08 | H / P1 | Jour/nuit, zoom extrême, ombres/réflexions, chevaliers, campagnes et commandes multijoueur dépendent du moteur | Validation manuelle par feature et par édition ; aucun succès en jeu annoncé |

## Historique et issues examinés

- Commits de caméra `f7a72d4`, `fcb1eab`, `9188354`, `d2a9434` et modifications
  ultérieures jusqu'à la référence ; modifications de limites `850a94a` à
  `6943fe2` ; ordre de backup `b1a5cdd` et préparation `fd44b9d`.
- [Issue #1](https://github.com/Eisenmonoxid/S6Patcher/issues/1) : ajout HE Steam
  et nécessité de décompression selon l'auteur.
- [Issue #2](https://github.com/Eisenmonoxid/S6Patcher/issues/2) : signalement zoom,
  puis correction par l'utilisateur de 120000 à 12000. Ce n'est pas une preuve
  de bug à la valeur normale.
- [Issue #3](https://github.com/Eisenmonoxid/S6Patcher/issues/3) : fond de menu
  changé intentionnellement. Aucun changement cosmétique de ce comportement.
- [Issue #4](https://github.com/Eisenmonoxid/S6Patcher/issues/4) : commentaire ancien
  sur Linux/Mono ; il ne décrit plus le projet actuel .NET 10/Avalonia.
- [Issue #5](https://github.com/Eisenmonoxid/S6Patcher/issues/5) : relâchement de
  touches de déplacement RTS et coins d'écran. L'auteur ne fournit pas de fix.
  Le problème du moteur RTS reste ouvert ; la correction des touches FPS n'est
  pas annoncée comme sa résolution.
- Contributions #6 et #7 : traductions ukrainiennes intégrées ; auteurs et
  licences conservés.

## Décision de livraison

Livraison d'une **branche de développement pour revue**, avec corrections et
tests exécutables, pas d'une release stable ni d'une preuve de fonctionnement
de toutes les features dans le jeu. Les risques P04, P10 et C11 déterminent les
trois prochaines interventions : corpus/signatures, récupération globale et
validation réelle de la caméra. [ROADMAP.md](ROADMAP.md) les ordonne ;
[TESTING.md](TESTING.md) donne les procédures et résultats exacts.
