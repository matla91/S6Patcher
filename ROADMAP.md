# Suite de la fiabilisation

Aucune nouvelle feature prévue dans cette mission. Les priorités ci-dessous
reposent sur les preuves de [AUDIT.md](AUDIT.md), les corrections de
[FIXES.md](FIXES.md) et les limites de [TESTING.md](TESTING.md).
Efforts estimatifs, à ajuster une fois l'environnement de jeu disponible.

## Trois prochaines interventions

| Ordre | Intervention | Bénéfice / effort | Dépendance et critère d'acceptation |
| --- | --- | --- | --- |
| 1 — P0 | Exécuter la matrice caméra dans une installation Windows isolée, d'abord HE et scénario de début de partie | Très élevé / quelques sessions ; directement lié au blocage joueur | Jeu légalement disponible et environnement autorisé ; Z/Échap répétés, marché absent/présent sans habitants, sélections, cinématiques, chargements et cartes ; page/rendu/contrôles restaurés et diagnostic de toute faute |
| 2 — P0 | Établir un manifeste de builds et préimages par site de patch | Très élevé / moyen avec corpus ; évite les modifications d'un EXE incompatible | EXE légalement détenus hors dépôt ; SHA-256, PE/sections et octets d'origine/patchés documentés par édition ; refus de toute préimage inconnue avant écriture ; fixtures synthétiques et états de repatch explicites |
| 3 — P1 | Étendre la récupération à EXE + scripts + INI + dossier mod | Élevé / moyen ; ferme les états partiels en cas d'échec tardif | Préparer tous les fichiers avant installation, conserver leur état initial, journal persistant et reprise/rollback ; injecter des fautes avant/après chaque étape et entre renommages sans toucher une installation personnelle |

Les essais du point 1 peuvent révéler une correction minimale supplémentaire
de propriété des pages/widgets ou de restauration du rendu. Ils doivent précéder
toute affirmation que le témoignage est résolu dans le moteur. Le point 2 ne
doit pas distribuer les EXE ni copier du code propriétaire au-delà des données
de validation légalement publiables ; les fixtures restent artificielles.

## Travaux suivants

| Priorité / preuve | Travail | Retour attendu / effort | Validation requise |
| --- | --- | --- | --- |
| P1 — D02 | Contrat XML explicite : tags/occurrences attendus et gestion des conflits | Élevé / faible à moyen | XML synthétiques complets/incomplets, pas de succès silencieux, mods/campagnes réels isolés |
| P1 — D03 | Restauration de Logic.CreateEntity même si le callback bandit échoue | Élevé / faible | Test Lua de faute et propagation conservée ; smoke test bandits dans le jeu |
| P1 — D08 | Smoke tests jour/nuit, graphismes, chevaliers, campagnes et multijoueur | Élevé / moyen ; donne une couverture réelle des features inchangées | Matrice par édition et options, comparaison au jeu témoin, captures/logs |
| P2 — D04/M05 | Fixtures de collisions hash et audit ciblé du decoder S6Packer | Moyen à élevé / moyen | Collision incluant Offset=0 ; entrées malformées ; tailles réelles confrontées aux limites ; changement local minimal ou sous-module révisé avec provenance |
| P2 — D05/D06 | Repli de journal vers un dossier utilisateur et validation des profils Windows | Moyen / faible à moyen | Dossier application non inscriptible ; Unicode/UNC/jonctions ; choix de profil sans modifier d'autres profils |
| P2 — M04/M05 | Décider le contrat de métadonnées d'archives et préparer l'extraction entière avant installation | Moyen / moyen | Dates définies et testées ; corruption tardive n'installe aucun résultat partiel ; archives autorisées |
| P2 — D01 | Chaîne reproductible du menu EMXBinData.s6patcher | Moyen / moyen, outils à identifier | Source et binaire liés par hash/version ; rebuild vérifiable avant toute modification du menu |
| P2 — issue #5 | Reproduire déplacement RTS/coins et relâchement de touches du moteur | Moyen / incertain | Distinguer comportement natif, patch et configuration ; aucune assimilation à la correction FPS |
| P2 — N02 | Tests réseau déterministes : timeout, corps excessif, proxy et lot incomplet | Moyen / faible à moyen | Serveur local simulé ; repli atomique du lot et diagnostics sans blocage UI |
| P2 — D09 | Lier le workflow de release historique aux validations du même commit et épingler ses dépendances | Moyen / faible | CI passée, package identifié par hash, rapport en jeu associé avant passage d'une draft à une version stable |
| P3 — D07 | Documenter les instructions/chevauchements binaires puis isoler seulement les frontières nécessaires | Maintenance / moyen | Manifeste lisible, liens aux tests, sans refonte générale ni modification des offsets non justifiée |

Ne pas commencer par une refonte de tous les singletons, une réécriture de
l'interface, un moteur de caméra nouveau ou un changement des mécaniques.
Le contrôle de PNJ, les nouveaux déplacements et l'extension du FOV restent
hors périmètre. Le menu, les assets et les licences originaux sont conservés.

## Conditions d'une future version stable

1. Rapport de test réel avec toutes les cases critiques PASS et échecs expliqués.
2. Identité/préimages des builds supportés établies ; diagnostic clair des autres.
3. Sauvegarde/restauration et récupération de fichiers auxiliaires validées sur
   fautes injectées, sans corruption ni consommation de l'unique sauvegarde.
4. CI verte, package Windows vérifié, licences/provenance conservées, aucun
   fichier propriétaire ajouté au dépôt.

La branche actuelle fournit un socle de développement testable. Aucune release
stable ne doit être publiée avant les validations en jeu nécessaires.
