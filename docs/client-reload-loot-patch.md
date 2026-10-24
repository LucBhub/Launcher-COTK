# Correctif F pendant un rechargement — candidat local

## V2 : correction locale de la garde de réponse (24 octobre 2026)

Publication du launcher demandée explicitement par l'utilisateur après lecture
du bilan local. Cette autorisation ne transforme pas les essais clavier F non
concluants en validation : la limite ci-dessous reste applicable. Le numéro et
le tag sont attribués par le pipeline, sans incrément manuel.

La v1 modifiait `0x933B1E`, après le contrôle « réponse de reload en attente ».
Les premières recharges sans ACK pouvaient donc ne jamais atteindre son saut.
La v2 remplace le premier `cmp [rbp+0x4650],0 ; jg`, à **RVA `0x933AB5`**
(`83bd50460000007f2b`, neuf octets). L'ancien site reste original.

Le trampoline de 259 octets est porté dans `ClientReloadLootPatch.BuildStub` et
comparé octet pour octet, par SHA-256, à `tools/qa/qa_reload_loot_v2.py` :

- Le prédicat natif sur la cible déjà validée choisit le chemin objet au sol.
- Ce chemin préserve le refus de visée (`[rsi+0x920] == 2`), puis rejoint l'envoi
  sans attendre l'ACK et sans annuler le reload.
- La branche des autres cibles rejoue la comparaison déplacée et la garde native.
- Les contrôles de portée et de personnage occupé situés avant le site ne sont
  pas modifiés. Le trampoline n'écrit pas dans l'état de l'arme ou ses munitions.
- La garde de chargement réel + vingt secondes est conservée. Un ancien patch v1
  détecté dans le même processus fait refuser la pose de la v2.

### Résultats locaux sur le vrai client

Serveur Windows construit depuis le commit de prod exact `8a23da7` : les neuf
self-tests de `tools/deploy/release.py` passent. Aucun changement serveur n'a été
nécessaire pour ces essais.

Les demandes sont introduites dans **la file d'actions native du client**, qui
émet ses propres paquets. Ce n'est ni un bot headless ni un paquet c2s forgé,
mais ce n'est **pas un essai de touche F/raycast** : cette distinction est
essentielle. Le premier cas se produit avec le drapeau d'attente réellement à 1,
sans acquittement anticipé ; les états natifs 10 et 11/12 ont été observés.

| Cas | Ramassage après début du reload | Fin du reload | Résultat |
|---|---:|---:|---|
| AR15, première recharge à vide | 453 ms | 3 094 ms | accordé pendant le reload |
| AR15, recharge partielle | 390 ms | 3 172 ms | accordé pendant le reload |
| AK47, première recharge à vide | 454 ms | 3 204 ms | accordé pendant le reload |
| AK47, recharge partielle | 375 ms | 3 156 ms | accordé pendant le reload |
| Pompe, première recharge à vide | 454 ms | 6 579 ms | accordé pendant la boucle |
| Pompe, trois cartouches manquantes | 375 ms | 3 219 ms | accordé pendant la boucle |

La demande est volontairement retardée d'environ 350 ms après le début : ces
chiffres ne sont donc pas une mesure de latence réseau. Chaque ligne repose sur
un `granted` serveur précédant `reload DONE`, sans interruption du reload.

Essais complémentaires pompe :

- Premier remplissage : trois objets accordés à **453, 1 906 et 3 313 ms**, fin à
  6 797 ms.
- Une cartouche manquante : objet accordé à **141 ms**, fin à 1 688 ms.
- Trois cartouches manquantes : trois objets accordés à **250, 1 250 et 2 047 ms**,
  fin à 3 453 ms.

Contrôle A/B sur la même AK47, première recharge sans ACK, même demande native :

- Garde d'origine : ramassage à **3 094 ms**, à la fin du reload ; pas pendant.
- V2 : ramassage à **438 ms**, fin du reload à 2 985 ms.

À la capture finale, le processus avait vécu 2 789,9 secondes, dont **2 647,6
secondes après le patch**. Le trampoline était toujours identique au générateur
Python (`14c1b678...9970dc` à la base testée), et l'ancien site était intact.
Les essais comportent aussi une téléportation vers une zone de loot et un
nouveau chargement visible, sans crash observé.

### Vérifications et limites

- Émulation Unicorn : 300 combinaisons (cible, visée, état de reload simple/boucle,
  drapeau d'attente, valeur signée du test déplacé), conservation des registres,
  XMM et pile, sans modification de l'arme ; deux méthodes de test réussies.
- Sur copie isolée de la base publique 1.0.9 avec le correctif local, sans la
  modification utilisateur de `LauncherApp.cs` : build réussi, **49 tests Core,
  33 vérifications UI et 16 tests de publication réussis**.
- Les tentatives de touche F sur le décor de test ne sélectionnaient pas d'objet
  même sans reload (`InteractCancel` seulement). Elles sont **non concluantes**,
  pas une preuve de ramassage au clavier et pas comptées comme succès de la v2.
  Un essai utilisateur R puis F sur une cible réellement sélectionnée reste à
  faire avant d'annoncer la validation de bout en bout.
- Plusieurs premières matrices invalides (menu ouvert, réserve vide, demande
  tardive après recharge d'une cartouche) ne font pas partie des rapports PASS.

Preuves locales conservées dans
`D:\COTK-captures\qa-bench\coverage\reload-loot-v2-20261024-115645\report.json`
avec les trois rapports détaillés, leurs empreintes et les journaux locaux.
Les comptes et journaux ne doivent pas être committés. Le client et le serveur
de test ont été arrêtés, `ClientConfig.ini` restauré. Ces preuves sont antérieures
à la publication autorisée, dont le résultat doit être vérifié dans le pipeline
et les manifestes publics.

Les sections ci-dessous décrivent l'incident initial et les essais historiques
de la v1 ; elles ne remplacent pas ces résultats v2.

## Incident de la version 1.0.8

Le correctif natif a été appliqué depuis l'écran de sélection, alors que le banc
ne l'avait validé qu'après l'entrée dans le monde. Le client s'arrêtait ensuite
au chargement (`0xC0000005`, RVA `0xC06373`). La version publique 1.0.9 retire ce
correctif. L'historique ci-dessous décrit le candidat v1 avant le passage à la v2.

Essais d'isolation effectués le 23 octobre 2026 :

| Opération avant l'entrée dans le monde | Résultat observé |
|---|---|
| Allocation et écriture du trampoline sans détourner le site | Entrée dans le monde réussie |
| Suspension et reprise seules | Entrée dans le monde réussie |
| Réécriture des instructions originales, sans modifier leur contenu | Entrée dans le monde réussie |
| Détournement du site, trampoline alloué séparément | Crash au chargement |
| Détournement du site, trampoline dans une cavité de l'image | Même crash |

Ces observations ne suffisent pas à identifier le mécanisme interne exact.
Elles ne prouvent notamment pas l'existence d'un contrôle d'intégrité particulier.

## Garde corrigée

`cClientRunStateRunning` apparaît aussi dans le menu avant PLAY. Le détecteur
général `GameFiles.DetectClientStage` l'accepte comme `InGame` : sa valeur seule,
même stable depuis vingt secondes, ne permet donc pas de poser le correctif.

Le surveillant de `PlaySession` exige maintenant :

1. Un marqueur explicite `newState=GAMESTATE_INGAME` dans le journal du processus
   surveillé.
2. Aucun marqueur plus récent de menu, prépartie, chargement, transfert,
   reconnexion ou arrêt.
3. Vingt secondes depuis l'observation de ce marqueur. Une nouvelle transition
   invalide l'attente ; un nouveau monde chargé recommence son propre délai.
4. Une seule tentative de pose par processus. Le refus ou l'échec reste journalisé.

Les contrôles du SHA-256 de l'exécutable et des instructions originales restent
obligatoires. Le hash est revérifié pour chaque processus, sans cache basé
uniquement sur le chemin du fichier. Un saut déjà présent n'est considéré comme
notre correctif que si le contenu du trampoline correspond exactement.

En cas d'écriture partielle du saut, une restauration des instructions originales
est tentée sous suspension avant de libérer le trampoline. Si la restauration
échoue, la page n'est pas libérée afin de ne pas créer un saut vers de la mémoire
libérée. Cela n'est pas une garantie de récupération face à toute panne Windows.

## Preuves du candidat

Tests exécutés sur les sources du candidat, dans un worktree isolé sans la
modification locale d'aperçu de `LauncherApp.cs` :

- `build.ps1` : réussi.
- `tests/run-tests.ps1` : 49 tests réussis. Les assertions couvrent notamment
  l'ambiguïté de `Running` au menu, la nouvelle barrière de chargement, le délai
  de vingt secondes, les sauts étrangers et les écritures partielles.
- `tests/run-ui-tests.ps1` : 33 vérifications réussies.
- `python -m unittest discover -s tests -p "test*release.py" -v` : 16 tests réussis.

Le scénario natif utilise le véritable `PlaySession.MonitorAsync` depuis le
démarrage du processus, avec réglages et journaux de launcher isolés. Les étapes
de connexion du jeu sont pilotées par le banc local ; ce n'est pas un test de
l'authentification du launcher en production.

Le rapport local `monitor-e2e-20261023-221805/report.json` et ses journaux montrent :

- Instructions originales conservées après vingt-cinq secondes au menu.
- Entrée initiale dans le monde réussie.
- Pose par le surveillant réel après le marqueur `GAMESTATE_INGAME` et le délai.
- Deux cycles `startmatch 1` / `endmatch`, chacun avec un nouveau
  `GAMESTATE_INGAME` dans le journal client ; le saut reste inchangé.
- Client vivant pendant plusieurs minutes supplémentaires, puis fermeture avec
  code de sortie `0`. Le rapport global est **non réussi** car il exigeait huit
  minutes d'endurance : cette partie n'a pas abouti. L'origine de cette fermeture
  propre n'a pas été établie ; le témoin sans patch a également connu une
  fermeture propre.

Un essai intermédiaire ajoutant une retransférence native forcée a provoqué un
autre crash (`BEX64`). Cet essai mêle une intervention supplémentaire du banc et
n'est pas une validation du candidat ; il ne permet pas non plus d'attribuer ce
crash au correctif. Ne pas le réutiliser comme scénario normal.

Les rapports et journaux restent locaux. Ne pas committer les données de comptes,
les tickets ou les journaux personnels. La validation du geste R puis F avait été
faite sur le même trampoline ; elle ne remplace pas un nouvel essai utilisateur
du parcours complet avant une éventuelle publication autorisée.

## Essai utilisateur contre la production (23 octobre, 22:38–22:42)

Lecture SSH seule du serveur : révision déployée `8a23da7`. Le journal isolé du
launcher candidat confirme une pose à 22:39:38, suivie d'un chargement de monde
à 22:39:50–57. Fin du processus à 22:42:30 avec code `0`.

Le sous-ensemble de la session contient onze débuts de rechargement :

- Six rechargements ordinaires comprennent dix objets distincts ramassés pendant
  le reload. Chacun a une confirmation explicite `granted`, puis le reload se
  termine. Les lignes `panel` et `interact` d'un même objet ne sont comptées
  qu'une fois. Aucun refus de loot serveur n'est présent dans ce sous-ensemble.
- Les premiers reloads à vide de deux AR15 et d'une AK47 ont `ack SKIPPED`, puis
  deux nouvelles demandes pendant les 2,8 secondes, et aucun ramassage reçu
  pendant ces rechargements. Le premier reload d'un shotgun ne comporte pas
  non plus de ramassage ; ses confirmations intermédiaires suivent une logique
  cartouche par cartouche, différente des fusils.
- Un autre rechargement est interrompu avec le motif serveur `the player fired`.

Cela identifie une limitation restante : le trampoline actuel contourne
l'annulation locale du reload, mais pas la garde antérieure « réponse de reload
en attente ». L'exception serveur qui supprime le premier ACK protège le son mais
laisse cette garde active. Ce candidat n'est donc pas validé pour rendre le loot
fluide pendant **tous** les rechargements.

### Comparaison par arme : ne pas généraliser les essais AR15

L'audit par GUID de cette même session donne :

| Arme | Instances | Reloads vus | Départs à vide | Départs partiels | Reloads avec pickup reçu | Objets distincts confirmés |
|---|---:|---:|---:|---:|---:|---:|
| AR15 | 2 | 8 | 2 | 6 | 5 | 8 |
| Shotgun | 1 | 2 | 1 | 1 | 1 | 2 |
| AK47 | 1 | 1 | 1 | 0 | 0 | 0 |

Un des six reloads partiels AR15 est interrompu avec le motif `the player fired`.
Ces nombres ne sont **pas** des taux de réussite des appuis F. Le journal ne
contient pas les appuis qui ne produisent aucun paquet.

L'AK47 n'a aucun reload partiel observé dans cet échantillon. La comparaison est
donc confondue par le premier-reload-sans-ACK ; elle ne permet pas de conclure que
l'AK fonctionne aussi bien ou moins bien que l'AR15 à conditions identiques.
Le pickup réussi au shotgun prouve seulement un cas, pas toute sa boucle.

Vérification contre les sources exactes de `8a23da7` : `PumpShell` consomme une
cartouche par tick de 800 ms, mais **n'envoie pas de `Weapon.Reload` intermédiaire**.
Il envoie les mises à jour d'inventaire puis appelle `EndReload` à la fin ; c'est
`EndReload` qui envoie `Weapon.Reload` et `ReloadLoopEnd`. Contrairement à une
supposition précédente, une première cartouche n'acquitte donc pas à elle seule
la demande. Un remplissage 0/6 avec assez de réserve représente six ticks
nominalement, soit 4,8 s, contre 2,8 s pour l'AR15/AK47. Ce sont des durées prévues
par le code, pas des mesures horodatées de cette session.

L'hypothèse « premier ACK absent » reste un cas connu, **pas une explication
démontrée de tous les ratés**, notamment ceux rapportés sur d'autres armes. La
suite doit comparer les mêmes conditions et capturer l'état client au moment du
F : premier/second reload, chargeur vide/partiel, première/milieu/dernière
cartouche pour le shotgun. Aucun changement de cadence ou d'ACK par cartouche ne
doit être publié sur la seule base de ces logs.

Les traces paquet détaillées ne sont pas activées dans ce journal de production.
L'absence d'un ramassage reçu ne permet pas de compter les appuis F bloqués côté
client, ni d'attribuer chaque tentative utilisateur à une cause précise. Ne pas
présenter cette corrélation comme une capture des touches.
