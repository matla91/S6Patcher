# Caméra History Edition Steam — preuves REA/Ghidra

Analyse statique du 13 octobre 2026, REA 6.4.0 / Ghidra 12.1.4. Le premier
import de cette session a terminé l'auto-analyse par défaut ; les requêtes
suivantes réutilisent le même Program. Ce n'est pas une exécution du moteur,
ni une garantie que toutes les fonctions ont été définies par Ghidra.

## Identité et coordonnées

| Fichier | SHA-256 |
| --- | --- |
| `analysis/binaries/Settlers6R.exe.unpacked.exe`, cible analysée | `6236ccad70e84ee7768007f96c502657b4082707f68d33cd12e4b6d7e120b25d` |
| Copie originale `analysis/binaries/Settlers6R.exe` | `099123fd1d809eee7f8d83263c90483919b76404c3698eb618010724616e5cfc` |
| EXE Steam installé, lu sans modification | même hash que la copie originale |

PE32 x86, version de fichier/produit `1, 71, 4289, 0`, édition Steam History
Edition identifiée par l'installation et la préparation Steamless existante.
Copie décompressée : 17 214 464 octets, ImageBase `0x00400000`. Les VA
ci-dessous appartiennent **uniquement à cette copie** ; RVA = VA − ImageBase.
REA a également mesuré les offsets fichier des points clés ; ne pas employer
les RVA comme offsets de patch. Aucun patch natif n'est créé.

Le bundle intégral est conservé hors Git dans
`../analysis/first-person-20261013/rea-evidence.json`. Il contient les
Evidence IDs complets, pseudocode, instructions et limitations. Les préfixes
`ev_…` ci-dessous permettent de retrouver chaque preuve ; ce document ne
redistribue pas le code décompilé propriétaire.

## Enregistrement des méthodes Lua

Recherche des chaînes définies : `ev_6897f503`, profil
`b9d116ea480a02cc8773b19372074a3caf77785cbc080b1ac7133b5110e40b17`.
Les initialisateurs écrivent une paire nom/adresse de wrapper dans les
structures d'enregistrement. Exemple : chaîne Switch à `0x010518ac`, xref
`0x0057f0e1` (`ev_a3d809f0`), écriture du wrapper `0x00656e89` à
`0x0057f0eb` (`ev_bdf7ea0f`). Position/LookAt/FOV et RTS sont retrouvés par
la même procédure, pas attribués à partir d'un nom supposé.

Ces initialisateurs et quelques petites méthodes sont désassemblés mais
hors d'une fonction définie par Ghidra. Les échecs de dossier ont été suivis
par lecture d'octets et instructions exactes ; ils ne fournissent pas de
pseudocode. Aucun changement de base Ghidra n'a été nécessaire.

## Fonctions identifiées

Module pour toutes ces fonctions : `Settlers6R.exe.unpacked.exe`.

| API / chaîne VA → xref | Wrapper VA (RVA) | Implémentation / résumé et effet | Preuves / confiance |
| --- | --- | --- | --- |
| `SwitchCameraBehaviour`, `0x010518ac` → `0x0057f0e1` | `0x00656e89` (`0x00256e89`) | Lit mode et durée optionnelle, défaut 0 ; manager virtuel +0x20 → `0x00655d21`. Réinitialise le contrôleur destination si différent, détache l'ancien, copie l'état caméra pour la transition, attache le nouveau, stocke l'ID et émet une notification. | `ev_6500ebcb`, `ev_13506c3e` ; forte, flot/instructions et vtable |
| `ThroneRoom_SetPosition`, `0x01051728` → `0x0057f255` | `0x0065703c` (`0x0025703c`) | Lit trois floats ; vtable ThroneRoom +0x24 → `0x0065563d`. Si le pointeur actif +0x38 est nul : **aucune écriture**, aucune erreur. Sinon position du contrôleur et caméra partagée, dirty view, puis copie FOV du contrôleur vers caméra. | `ev_b13bf4d2`, `ev_d2225e1b` ; forte |
| `ThroneRoom_SetLookAt`, `0x0105175c` → `0x0057f1d9` | `0x00656f7a` (`0x00256f7a`) | Vtable +0x28 → `0x006555d4` ; uniquement si actif, écrit cible et marque dirty via `0x006527ec`. Peut également réussir sans effet hors mode 5. | `ev_ee63ac58`, `ev_b020d03b`, `ev_a291fa0e` ; forte |
| `ThroneRoom_SetFOV`, `0x010517b4` → `0x0057f15d` | `0x00656f43` (`0x00256f43`) | Écrit le champ du contrôleur à `0x013695a0` (+0x10), sans recopier le FOV live. `SetPosition` appelle `0x006527ca`, qui écrit le FOV live +0x5c. Le FOV du contrôleur demeure après changement de mode. | `ev_67bbe68b`, `ev_8aee758f`, `ev_729e4a9c` ; forte |
| `RTS_GetLookAtPosition`, `0x01051928` → `0x0057e7eb` | `0x00656726` (`0x00256726`) | Retourne deux valeurs depuis les champs RTS `0x0136910c/110`. Ne lit pas la cible ThroneRoom. | `ev_8c8a5586` ; forte |
| `RTS_SetLookAtPosition`, `0x01051910` → `0x0057ead3` | `0x0065691b` (`0x0025691b`) | Appelle une préparation RTS virtuelle +0x2c (`0x0065498a`), puis stocke X/Y dans ces deux champs. N'effectue pas seul un changement de mode. | `ev_52dc80b4`, `ev_423572ee` ; forte |
| `UseCloseUpSettings`, `0x01055e84` → `0x0058154a` | `0x00693ae6` (`0x00293ae6`) | Accède au sous-objet rendu +0x80 du contexte `0x0136e560`, virtuel +0x44 → `0x006f1077` : met son octet +0x64 à 1. Pas de sauvegarde/restauration d'un ensemble de préférences dans cette méthode. | `ev_09447dd0`, `ev_06b40d14`, `ev_2ba27084` ; forte, instructions |
| `UseStandardSettings`, `0x01055e98` → `0x005815be` | `0x00693b19` (`0x00293b19`) | Même objet, virtuel +0x48 → `0x006f107c` : remet l'octet +0x64 à 0. Ne restaure pas à lui seul l'occlusion, les pages GUI ou les bindings. | `ev_b9f234f2`, `ev_20075b00` ; forte, instructions |
| `GetCameraBehaviour`, `0x010518d8` → `0x0057e5fb` | `0x006565ad` (`0x002565ad`) | Manager virtuel +0x1c → `0x005173d0`, retourne le champ mode +0x20. Permet une vérification Lua du changement réellement sélectionné. | `ev_1cc7ffda`, `ev_59dc720a` ; forte |
| `SetUserOptionOcclusionEffect`, `0x0105774c` → `0x00582870` | `0x006b15c8` (`0x002b15c8`) | `0x006b193c` stocke le niveau utilisateur +0xd0 : valeurs non négatives bornées à 0..2, négatif bascule 0/2. `0x006b1a56` réapplique le rendu. Rétablir 1 écrase donc un choix 0 ou 2. | `ev_e3e190ae`, `ev_b78d65ba`, `ev_775f7ea5` ; forte |

La chaîne `ThroneRoomCameraControl` n'est pas retrouvée dans l'inventaire
Ghidra (`ev_44aabbcf`) ni dans les Lua libres examinés. Elle est appelée
par les UpdateFunction de `ThroneRoom.xml` (voir
[diagnostic](FIRST_PERSON_CAMERA.md)). Son original Lua et l'ordonnancement
GUI exact restent non récupérés. **Ne pas confondre** ce callback avec la
méthode d'update native ThroneRoom, identifiée ci-dessous.

## Réponse centrale : modes 5 et 0

Le manager initialise ses slots via `0x00655b90` : slot 0 → objet RTS
`0x01369108`, slot 5 → objet ThroneRoom `0x01369590`. Vtable manager
`0x010514fc` (`ev_adabd31f`), vtable ThroneRoom `0x01051400`
(`ev_41971a0d`), vtable RTS `0x0105136c` (`ev_fe35f66f`). Les constructeurs
correspondants sont `0x006558f9`, `0x00655481`, `0x00654152` ; les tables
génériques écrites par leurs destructeurs ne sont pas les tables actives.

`Switch(5)` sélectionne donc le contrôleur ThroneRoom et lui attache l'objet
caméra partagé à +0x38 par `0x006555c7`. `Switch(0)` le détache et attache
RTS à son champ +0xc8 par `0x00654aa5`. Les objets des deux contrôleurs
restent distincts et persistent ; **il ne s'agit pas d'un reset global**.
Le manager n'effectue ni restauration de pages/widgets/bindings ni restauration
des options d'affichage dans le chemin examiné.

L'update manager `0x00655e5e` appelle uniquement le slot correspondant à son
ID actif, puis applique la caméra/projection ou le blending. L'update native
ThroneRoom (slot +0x18) à `0x004c7940` est un simple `RET 0x10`
(`ev_fb5842f6`), alors que RTS possède son update à `0x00654c0c`.
Le mode 5 dépend donc de positions/cibles écrites explicitement ; il n'a
pas un contrôleur RTS concurrent dans ce dispatch. D'autres callbacks
peuvent néanmoins sélectionner un nouveau mode.

La pose marque la vue dirty ; `0x006526c4`/`0x006523f1` mettent à jour la
matrice/projection. Ce dernier utilise `tan(FOV * 0.5 * 0.017453292)` :
champ de vision en degrés. Le changement de mode sans second argument
demande le chemin sans blending. Ces observations statiques ne garantissent
pas la cadence de rendu ou des callbacks GUI.

## Conséquences et correctif Lua

- Vérifier mode 0 avant entrée et mode 5 après sélection. Un `pcall` réussi
  sur les setters ne prouve pas l'activation du contrôleur.
- Détecter toute perte du mode 5, même si `g_Throneroom` et l'animation sont
  absents. Sortir du FPS en conservant le mode et la cible choisis ailleurs.
- Vérifier le retour 0 à la sortie ; conserver pending si le setter n'a pas
  produit cet état. La simulation d'un switch ignoré est un risque de contrat
  couvert par test, **pas** un incident observé de cette fonction native.
- Positionner le FOV avant `SetPosition` à l'entrée et réappliquer la position
  au changement de FOV stationnaire. Cela suffit ; aucun patch x86 nécessaire.
- Capturer `Options.GetIntValue("Display", "Occlusion", 1)` avant entrée,
  restaurer ce niveau et le conserver lors d'une reprise. Les scripts
  officiels confirment cette option persistée et sa plage 0/1/2. Cela ne
  constitue pas un getter d'un override graphique tiers effectué seulement
  à l'exécution.

## Hypothèses confrontées aux preuves

| Hypothèse | État / éléments et test restant |
| --- | --- |
| H1 restauration caméra incomplète | Partiellement confirmée : mode 0 ne remet pas tous les états ; les objets persistent. Vue RTS a ses propres paramètres, mais FOV partagé ThroneRoom reste modifié. Comparer la cinématique après FPS ; aucun getter du FOV ThroneRoom n'est établi. |
| H2 contrôleurs concurrents | Pas de double update RTS/ThroneRoom dans le dispatch analysé. Une reprise de mode externe laisse toutefois les setters FPS silencieux ; détectée et corrigée. Reproduire les interruptions réelles. |
| H3 ordre des opérations | Confirmée pour le FOV : setter après position n'applique pas le FOV initial ; changement stationnaire n'est pas transmis. Corrigé et simulé. Switch 5 avant position est au contraire requis. |
| H4 Échap/GUI | Délégation déjà corrigée ; réentrance sortie/entrée reproduite puis corrigée. Le chemin Lua original annule les cinématiques et peut forcer mode 0 ; effets natifs de Game.Escape antérieurs au callback non étudiés complètement. Essai réel indispensable. |
| H5 pile GUI | Non résolue : mocks ne connaissent que le compte, pas la propriété. GetTopPage retourne un champ de widget via `0x008721e0`, insuffisant pour distinguer deux pushes de la même page. Tester une cinématique superposée ; aucun remaniement spéculatif. |
| H6 effets silencieux/différés | Confirmée : setters de pose ignorés hors contrôleur actif, FOV différé jusqu'à copie via position, matrices dirty consommées ultérieurement. Les switches eux-mêmes sont synchrones dans le chemin examiné ; scheduling GUI restant inconnu. |
| H7 début de partie | Aucun besoin d'habitants dans le code. Marché conservé ; snapshots protégés. Disponibilité exacte du callback/updater en première frame à tester dans une nouvelle partie. |
| H8 bindings/rendu | Restauration des commandes standard déjà testée ; occlusion 0/2 écrasée par 1 confirmée et corrigée. Overrides tiers et autres états graphiques sans getter restent à comparer au témoin. |
| H9 réentrance/hooks | Confirmée sur doubles, gardes ajoutés. Autres hooks 34/34 ; aucun changement de leur code. |

L'incident historique n'a pas été reproduit dans cette session. Les preuves
identifient des défauts actuels, pas une cause unique certaine de ce témoignage.

## Reproduction et limites

Ouvrir le même fichier et vérifier son hash avec REA/Ghidra, rechercher les
chaînes, suivre leurs xrefs d'enregistrement, lire les pointeurs wrapper,
puis les tables et méthodes indiquées. Les Evidence IDs complets du bundle
permettent de distinguer instructions observées et pseudocode dérivé.
Les signatures `in_ECX`, `extraout_ECX`, variables de pile et types récupérés
sont des artefacts de décompilation, pas des noms/types source établis.

L'import complet par défaut a abouti, mais plusieurs petites fonctions
restent sans frontière FunctionManager. Les appels virtuels ont été suivis
par leurs constructeurs/vtables quand ces liens étaient établis. Il n'y a
ni exécution du binaire, ni observation de matrices/runtime, ni validation
multiversion. Les DLL n'ont pas été importées : les mécanismes nécessaires
ci-dessus ont été suivis dans le module principal ; BBLua51/debugger et
autres imports ne sont pas présentés comme analysés.

Le bundle/snapshot de session et les hashes avant/après sont hors dépôt.
Aucun EXE, base Ghidra, script commercial ni credential n'est commité.
