# Reprise sur le PC Windows distant

Le dépôt GitHub est le point de passage entre les deux PC et les deux comptes
Codex. La branche à utiliser est **`fix/first-person-camera-engine-20261013`**,
pas `master`. Les corrections caméra sont dans `9c25ef3` et `5e73c71` ; les
commits suivants ajoutent les rapports, le protocole et la livraison.

## Chemin le plus court : package GitHub déjà construit

1. Sur GitHub, ouvrir **Actions → First-person Windows test package** et
   choisir l'exécution réussie du commit voulu sur cette branche.
2. Télécharger l'artefact `S6Patcher-first-person-windows-<SHA complet>` et
   décompresser son ZIP sur le PC distant, par exemple dans `C:\S6FPS\Patcher`.
   Si la construction a échoué, ne pas remplacer ce package par le package
   Release trimmed du workflow historique.
3. Lire `DELIVERY.json` : commit, révision du sous-module, SDK et empreintes
   du patcher et des trois scripts. Vérifier les hashes après téléchargement.
   La machine distante n'a besoin ni de SDK .NET ni de Lua pour exécuter
   ce patcher autonome.
4. Donner au Codex distant le message ci-dessous et le dossier décompressé.
   Il prépare l'installation/profil de test selon
   [FIRST_PERSON_WINDOWS_VALIDATION.md](FIRST_PERSON_WINDOWS_VALIDATION.md),
   puis accompagne l'essai. Copier les trois fichiers `Resources` du package
   dans `C:\S6FPS\Evidence\Resources` pour comparer les scripts déployés.

Le package est explicitement **sans trimming** afin de conserver la
sérialisation JSON du journal de récupération HE Steam. Il contient les
scripts du même build ; ni EXE du jeu, ni sauvegarde, ni données du portable.
Le workflow de validation habituel contrôle aussi les scripts avec Lua 5.1
sous Linux. Vérifier son succès sur le même commit ; un package téléchargé
n'est pas une preuve de fonctionnement dans le moteur.

## Message à copier au Codex distant

```text
Nous reprenons le test First-Person de Settlers VI History Edition Steam sur ce PC Windows.
Le dépôt est https://github.com/matla91/S6Patcher et la branche est fix/first-person-camera-engine-20261013.
Le package de test GitHub Actions est déjà téléchargé : je te donne son dossier.
Lis DELIVERY.json, vérifie les SHA-256 et relève le commit exact testé.
Lis FIRST_PERSON_REMOTE_START.md et FIRST_PERSON_WINDOWS_VALIDATION.md inclus dans ce package ; si tu utilises les sources, lis aussi les instructions locales AGENTS.md et les docs de la branche.
Inspecte cette machine et son installation existante ; les chemins du portable ne s'appliquent pas ici.
Prépare puis applique le patcher exclusivement sur une copie complète du jeu et un profil Documents de test, avec checkpoint et procédure de restauration. Conserve l'installation Steam et les sauvegardes personnelles.
Utilise les ressources embarquées de ce package sans trimming. Compare les trois scripts effectivement écrits aux hashes du manifeste, puis vérifie le marqueur moteur S6Patcher FPS script loaded: camera-engine-20261013-v1.
Accompagne-moi pour une nouvelle partie : marché présent avant habitants, Z, orientation/déplacement, sortie Z, réactivation et sortie Échap, vingt cycles, retour RTS/UI, puis cinématique et sauvegarde/rechargement si disponibles.
Note le logiciel de connexion distante, le mode d'affichage et les éventuelles limitations de souris de cette session ; n'attribue pas automatiquement un problème d'entrée à la caméra.
Ne modifie pas les corrections avant d'avoir décrit et reproduit une anomalie. Distingue tests réels et tests simulés, et conserve commit/hashes, étapes exactes, logs et résultats PASS/FAIL/non testé dans un compte rendu sans fichiers propriétaires.
```

Le compte OpenAI différent ne reçoit pas l'historique de ce chat par ce
message. Le manifeste, les documents et les rapports du dépôt fournissent
le contexte nécessaire ; les bases Ghidra et binaires du portable ne sont
pas nécessaires au premier test.

## Si le Codex distant doit construire depuis les sources

Pour un nouveau checkout, depuis un dossier de travail choisi sur ce PC :

```powershell
git clone --recurse-submodules --branch fix/first-person-camera-engine-20261013 https://github.com/matla91/S6Patcher.git S6Patcher-FPS-Test
Set-Location S6Patcher-FPS-Test
git rev-parse HEAD
git submodule status --recursive
```

Pour un checkout existant, inspecter d'abord `git status` et ses instructions
locales ; préserver son travail. Faire fetch puis utiliser un checkout neuf
ou une branche adaptée, sans reset. Fixer le commit voulu : le HEAD de la
branche peut évoluer entre deux essais.

Installer/utiliser le SDK sélectionné par `global.json` (10.0.100 ou patch
compatible), et le sous-module épinglé. Si un script `Enter-DevEnvironment.ps1`
existe sur cette machine, le charger ; ne pas réutiliser aveuglément celui
du portable. Construire le package avec :

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Build-FirstPersonTest.ps1
```

Le script refuse les modifications suivies non commitées et vérifie la
révision du sous-module ; il compile, exécute le banc C#, publie sans trimming
et produit `artifacts/first-person-test/<SHA>/package`. Il ne lance aucune UI
et ne cherche/modifie aucune installation du jeu. Le dossier `artifacts` est
ignoré par Git. Les tests Lua du protocole nécessitent Lua 5.1 séparément.

## Retour du test

Le Codex distant remplit la matrice du protocole. Pour reprendre sur le
portable, transmettre le compte rendu texte, le commit et les diagnostics
utiles. Aucun lien automatique entre les comptes n'est requis. Si le compte
rendu est ajouté au dépôt, ne pousser que Markdown/texte expurgé, jamais
EXE/BBA/sauvegardes ou journals contenant des fichiers originaux.
