# COTK · Launcher Next

Refonte indépendante du launcher dans **`D:\projects\COTK\tools\launcher-next`**.
L'ancien launcher, dans `D:\projects\zemu\launcher`, reste intact.

## Voir la refonte

Double-cliquer sur **`APERCU.cmd`**.

- Fenêtre WPF native, palette noir chaud / braise / or, typographie Oswald.
- Paysage vectoriel original : montagnes, forêt, parachutes, parallaxe et braises.
- Entrées en cascade, compteurs animés, survols, reflet sur le bouton principal.
- Accueil, bibliothèque, historique, classement, paramètres et écran de connexion.
- Téléchargement simulé pour découvrir les barres, débit, estimation et annulation.
- Le mode aperçu est signalé dans l'interface. Ses chiffres sont **fictifs** ; il ne lit
  pas les identifiants Windows, ne lance pas le jeu et utilise des réglages séparés.

Le binaire local est **`out\COTK.Launcher.Next.exe`**. Le conserver avec la DLL, le
runtimeconfig, les polices et le modèle de configuration. `APERCU.cmd` le lance en aperçu ;
**`LANCER.cmd`** lance le mode connecté à l'API réelle.

Les raccourcis **Ctrl+1 à Ctrl+5** changent de page. La fermeture des dialogues se fait aussi
avec Échap. La fenêtre s'adapte uniformément à un écran trop petit.

### Désinstallation du client

Dans **Library / Bibliothèque**, le bouton **Uninstall client / Désinstaller le client** apparaît
quand un client est présent ou qu'une désinstallation interrompue doit être terminée.
La confirmation affiche le dossier exact, le nombre de fichiers enregistrés et leur taille.

La suppression porte uniquement sur les fichiers identifiés comme appartenant au jeu :
inventaire local `.cotk-managed-files.json`, ou catalogue validé pour une ancienne installation
qui n'a pas encore d'inventaire. Ce dernier cas nécessite une connexion pour récupérer le catalogue.
Les nouvelles installations par archive enregistrent désormais cet inventaire dès l'installation.

Les `.ini`, profils de touches, réglages sonores, `UI/Settings`, logs et fichiers inconnus sont conservés.
Le dossier client n'est jamais supprimé récursivement. Le launcher, le compte et les identifiants
Windows ne sont pas désinstallés. La désinstallation est refusée si le jeu tourne, si le dossier
n'est pas reconnu, ou si la liste de fichiers contient un chemin dangereux ou une jonction.

Une fois la suppression commencée, il faut attendre sa fin avant de fermer le launcher.
En cas de fichier verrouillé, le message précise le fichier et propose de réessayer ; un reçu
`.cotk-uninstall.json` permet de reprendre. Le reçu permet ensuite de réinstaller dans le même
dossier en restaurant aussi les fichiers personnels inconnus. Un conflit avec un nouveau fichier
du jeu bloque le remplacement plutôt que de perdre le fichier personnel.

Le mode aperçu simule cette opération sans supprimer de fichiers réels. Tests de suppression,
reprise, protection des données et réinstallation exécutés dans des dossiers temporaires uniquement.

### Langues

**English** est le choix par défaut lorsqu'aucune préférence n'est enregistrée. Le menu en haut
à droite est présent dès l'écran de connexion, dans l'ordre **English / Français / 简体中文**
(chinois simplifié). Il est aussi accessible dans **Settings → Language**.

Le changement est immédiat, sans redémarrage, sans interrompre un téléchargement, une
validation de connexion ou la surveillance du jeu. La clé `language` est sauvegardée dans
`%APPDATA%\COTK\settings.json` (`en`, `fr`, `zh-CN`) en conservant les autres réglages.
Une valeur inconnue retombe sur l'anglais. L'aperçu conserve ses réglages séparément.

Les menus, états de connexion/installation, erreurs, actions, consentement des rapports et paliers
sont localisés. Les caractères chinois utilisent Microsoft YaHei. Les codes d'erreur,
pseudos, chemins, versions et extraits de logs conservent leur valeur. Les actualités réelles
restent dans leur langue de publication ; les actualités fictives de l'aperçu sont localisées.

Le catalogue est dans `src/Core/L.Catalogue.cs`, avec des clés françaises historiques et
deux traductions. Les labels conservent leur valeur source pour changer de langue à chaud ;
les données libres sont affichées par `Ui.Raw` pour ne pas les traduire par accident.
`--lang=en`, `--lang=fr` ou `--lang=zh-CN` permettent une surcharge de test sans changer
la préférence sauvegardée. `tests/capture-languages.ps1` produit les captures des trois langues.

## Fonctionnement réel déjà câblé

- Connexion par navigateur avec les routes `/launcher/device/*` existantes,
  code à durée limitée et polling ; identifiant/mot de passe en secours.
- Session dans le Gestionnaire d'identification Windows ; sessions staff non mémorisées.
- Classement et notes de patch servis par `api.cotk.fr`.
- Détection du client, téléchargement avec reprise, vérification SHA-256, delta à quatre
  transferts simultanés, contrôle de disque, cache d'empreintes, vérification et réparation.
- Fichiers temporaires puis renommage ; suppression limitée aux fichiers précédemment
  gérés par le launcher ; aucun fichier inconnu supprimé lors de la première migration.
- Repli sur l'archive complète après confirmation, jamais 15 Go téléchargés silencieusement.
- Installation, réparation et changement de dossier bloqués si le client tourne déjà.
- Ticket `lp2`, `ClientConfig.ini` atomique, lancement du client et surveillance des étapes.
- Erreurs par domaine avec codes stables, causes et actions, export de diagnostic expurgé.
- Mutex partagé avec l'ancien launcher : deux launchers réels ne peuvent pas réécrire
  simultanément les tickets. L'aperçu est indépendant.

### Précontrôles avant écriture et avant lancement

- **Disque** : budget = téléchargement restant + taille décompressée + données joueur + 256 Mio
  de marge, en tenant compte d'une reprise. Un espace illisible bloque (`E-DISK-004`) plutôt que
  de deviner. Le dossier doit être inscriptible. La confirmation affiche dossier, volume à
  télécharger, espace nécessaire et disponible.
- **Lancement** : `H1Z1.exe`, `mss32.dll`, `steam_api64.dll`, `vivoxsdk_x64.dll` présents,
  dossier inscriptible, modèle `ClientConfig.ini` valide.
- **Pause / reprise** du téléchargement sans annulation ni perte (les fichiers `.part` restent).

### Statut réel du serveur de jeu

`GET /api/v1/launcher/status` (déployé en production) lit le point `/status` du serveur de jeu et un
éventuel `maintenance.json` ; il renvoie `apiOnline`, `gameOnline`, `maintenance`, `players`.
La pastille et le bouton Jouer reposent sur ces valeurs : API seule ne vaut pas « jeu en ligne »
(`E-GAME-010` en maintenance). Si la route manque (ancien backend), le launcher n'invente rien.

### Rapports de diagnostic (production)

`POST /api/v1/launcher/reports` : consentement explicite, quota 5/h/compte, expurgation côté
serveur, conservation 7 jours, lecture réservée au staff (403 sinon), purge physique par la
tâche d'entretien. Preuve rejouable : `tools/prove-production-reports.py` (compte QA isolé,
nettoyé en fin de test). Le profil joueur (`/launcher/profile`) est lui aussi en production.

### Publications signées

**En production, le launcher n'accepte plus que `https://dl.cotk.fr/launcher-next/manifest.signed.json`**
(aucun repli non signé ; l'API locale de développement garde son manifeste simple, boucle locale
uniquement). Après **chaque release client**, et au moins tous les 30 jours (durée de validité),
relancer `python tools/publish-signed-manifest.py` : il lit ce que voient les joueurs, copie le
catalogue sous une URL immuable `client-files-<version>-<sha12>.json`, signe avec la clé locale,
téléverse, puis relit depuis `dl.cotk.fr`. Un manifeste expiré bloque l'installation (message clair).

`SignedRelease` vérifie une enveloppe ECDSA P-256 : signature, produit, canal `stable`,
dates de validité, manifeste, et catalogue de fichiers **épinglé par SHA-256**. La clé publique est
embarquée ; la clé privée est une clé Windows CNG **non exportable** sur la machine de
publication (`tools\publisher.ps1 key|sign`). Rejets testés : contenu altéré, autre clé,
expiré, mauvais produit/canal, catalogue non épinglé, enveloppe illisible.

## Sécurité et état de livraison

Cette livraison est une **refonte de développement exécutable**, pas encore une release publique signée.

- TLS standard Windows, aucun contournement de certificat ; HTTP limité à la boucle locale de développement.
- Hôtes de téléchargement limités à `api.cotk.fr` et `dl.cotk.fr` ; redirections automatiques refusées.
- URL de validation navigateur contrôlée, délais HTTP/JSON, erreurs 401/403/429/5xx explicites.
- Chemins de catalogue contrôlés : version, schéma, doublons, sommes de taille, chemins relatifs,
  noms Windows réservés, jonctions et liens ; extraction anti-zip-slip et anti-ADS.
- Logs avec rotation et masquage des tickets, tokens et mots de passe.
- Les variables `COTK_API_URL` et `COTK_GAME_SERVER` n'acceptent que des surcharges locales.

**À terminer avant diffusion publique :** signature Authenticode (aucun certificat disponible),
updater transactionnel dédié à cette refonte et installateur de release (le manifeste signé est
publié et vérifié, mais le launcher Next ne se met pas encore à jour lui-même). Le launcher
Next n'applique **pas** les mises à jour du launcher WinForms 0.10.x : ce sont deux produits distincts.
Le SHA-256 garantit l'intégrité, pas l'authenticité si le serveur de publication est compromis.

### Console F8 staff

Intégrée depuis la 1.0.1 (comme l'ancien launcher) : après connexion d'un compte **admin ou modérateur**,
le launcher interroge `GET /api/v1/staff/console` (lecture seule) et compare le SHA-256 à la console
installée. Absente ou périmée → notification « Installer / Mettre à jour » et carte **Paramètres → Staff**.
Le zip est téléchargé depuis l'API réservée au staff (chaque téléchargement est tracé dans l'audit côté
serveur), limité à la taille annoncée, vérifié par SHA-256, extrait sans zip-slip puis installé dans
`ToggleConsole\` à côté du launcher. Un refus (empreinte, taille, archive sans `ToggleConsole.exe`) laisse
la console déjà installée intacte. Installation bloquée pendant une partie. Un joueur ne voit rien et
ne déclenche aucun appel. Au lancement du jeu, le surveillant démarre en `--native-ui` uniquement.
Vérifié : tests du noyau (installation, mise à jour, 4 refus), test UI avec clic réel, et production
(403 pour un joueur, 200 `sha256`+`sizeBytes` pour un modérateur). **Non vérifié en conditions réelles :**
le téléchargement avec un vrai compte staff et l'ouverture en jeu avec F8.

La connexion navigateur complète et le parcours jusqu'au jeu nécessitent une recette avec un compte
autorisé : ils n'ont pas été validés de bout en bout durant les captures de l'aperçu.

## Performance mesurée

Rapport reproductible : [`docs/performance.json`](docs/performance.json).

Sur cette machine Windows à **16 processeurs logiques**, échantillons de 12 secondes :

| État | CPU total | CPU équivalent à un cœur | Mémoire résidente |
|---|---:|---:|---:|
| Accueil animé, au premier plan | 1,46 % | 23,29 % | 224,5 Mio |
| Fenêtre réduite | 0,11 % | 1,82 % | 226,3 Mio |

Ces chiffres sont des mesures locales, pas une garantie sur tous les PC. Les effets de fond sont
plafonnés à 30 images/s, les transitions à 60 ; les animations sont suspendues quand la fenêtre
est réduite ou inactive. Le mode « Réduire les animations » est applicable immédiatement.
Les images du login sont libérées après connexion. Hachage, téléchargement et surveillance du
client tournent hors du thread d'interface.

## Vérification

Sur cette machine sans SDK, Roslyn BuildTools + .NET Desktop Runtime 10 compilent exactement les
sources `src/`. Le petit apphost dans `bootstrap/` permet un lancement local sans console.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1 -ApiSmoke
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-ui-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\capture-ui.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\capture-languages.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\performance.ps1
```

Résultats : **30 scénarios du noyau/API**, **27 vérifications UI dans une vraie fenêtre WPF**.
Le test delta utilise un serveur HTTP local : reprise Range, noms accentués, fichiers atomiques,
cache, conservation des réglages, corruption réparée, fichier manquant, repli complet et refus d'un
catalogue incompatible. Les tests UI couvrent 25 navigations avec un nombre d'horloges stable,
focus modal, confirmations, annulation et réduction des animations.

Les tests et rapports sont indépendants du build serveur. Rien n'a été publié ni commité.

### Build SDK pour une future release

```powershell
dotnet publish COTK.Launcher.Next.csproj -c Release -r win-x64 --self-contained true -o publish
```

Ce chemin a été exécuté et vérifié (SDK 10.0.402, installé dans le profil utilisateur sans droits
admin via le script officiel `dotnet-install.ps1` : `-Channel 10.0 -InstallDir %LOCALAPPDATA%\cotk-dotnet-sdk -NoPath`,
puis `%LOCALAPPDATA%\cotk-dotnet-sdk\dotnet.exe publish …`). Résultat : 263 fichiers, 133 Mio, zip de
57,7 Mio ; le zip extrait dans un dossier vierge démarre avec son propre `coreclr.dll`, sans .NET
installé. Le SDK génère l'apphost natif et embarque le runtime ; ne pas mélanger avec le bootstrap local.
Le `.csproj` ajoute `System.IO` et `System.Net.Http` aux usings, que WPF retire par défaut (`build.ps1` les fournit déjà).

### Distribution sans certificat Authenticode

Choix assumé : pas de certificat payant, comme l'ancien launcher. SmartScreen avertira donc au
premier lancement ; [`docs/download-faq.md`](docs/download-faq.md) (FR/EN/ZH) explique les deux
clics à faire et comment comparer l'empreinte. Après `dotnet publish` (SDK .NET 10 gratuit,
**requis pour distribuer** : le build local suppose .NET installé chez le joueur) :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\package-release.ps1 -PublishDir publish -Version 1.0.0
```

Le script produit `release\COTK-Launcher-Next-<version>-win-x64.zip` et son `.sha256`, refuse
d'écraser une version déjà empaquetée et avertit si le runtime n'est pas embarqué. Publier le zip
et le `.sha256` ensemble, et recopier l'empreinte dans la FAQ. Si le code devient public un jour,
SignPath Foundation propose une signature gratuite pour l'open source.

## Migration depuis l'ancien launcher (0.10.x) et mises à jour

**Ce que fait déjà la 0.10.10 chez les joueurs (non modifiable)** : après la connexion, si le manifeste annonce
`launcher.version` supérieure à la sienne, elle télécharge `launcher.downloadUrl` (taille et SHA-256 exacts),
**sans demander**, se ferme, copie TOUS les fichiers du zip par-dessus son dossier, puis relance
**`COTK.Launcher.exe`** (le zip doit donc contenir ce nom à la racine). Publier ce paquet dans le bloc
`launcher` de `manifest.json` migre donc chaque joueur vers ce launcher à sa prochaine connexion.

Le paquet de migration est le zip de ce projet, publié avec `-p:AssemblyName=COTK.Launcher` :

```powershell
dotnet publish COTK.Launcher.Next.csproj -c Release -r win-x64 --self-contained true -p:AssemblyName=COTK.Launcher -o publish-X.Y.Z
powershell -File tools\package-release.ps1 -PublishDir publish-X.Y.Z -Version X.Y.Z   # refuse sans COTK.Launcher.exe
```

**Le launcher se met à jour lui-même** (`LauncherUpdater`, depuis la 1.0.2) selon le même protocole : détection
dans le manifeste signé, proposition avec « Plus tard », téléchargement vérifié (taille + SHA-256), copie de
l'installateur dans `%TEMP%`, **sauvegarde des fichiers remplacés et restauration complète en cas d'échec**,
relance, nettoyage de la copie temporaire (limité à nos dossiers). Un paquet contenant `client\`, `logs\`,
`ToggleConsole\` ou `settings\`, un zip-slip ou un zip sans `COTK.Launcher.exe` est refusé. Sans cette
fonction, les joueurs migrés seraient bloqués sur leur version.

Preuve rejouable : `python tools/e2e-migration.py` — télécharge la vraie 0.10.10 de production, l'installe dans un
bac à sable (`%TEMP%\cotk-e2e`), la fait migrer vers 1.0.2 sans clic puis 1.0.2 → 1.0.3 par un clic réel sur
« Update », contre un faux serveur sur 127.0.0.1 (aucun en-tête journalisé). Contrôle : client, journaux et console
staff intacts, aucun dossier temporaire laissé, un seul launcher. Il pose un jeton factice dans le Gestionnaire
d'identification et le retire à la fin ; il refuse de démarrer si un vrai jeton existe. Prérequis : les deux zips
`pkg-1.0.2.zip` / `pkg-1.0.3.zip` dans `%TEMP%\cotk-e2e-pkgs`.

**Publier la migration en production** (étape qui touche tous les joueurs, volontairement non faite par les scripts) :
téléverser le zip dans le dossier `launcher-next/` du serveur de téléchargement, puis remplacer le bloc `launcher` du
`manifest.json` de production (`version`, `downloadUrl`, `sha256`, `sizeBytes` ; garder `setupUrl`) — l'API relit
ce fichier à chaque requête, l'effet est immédiat. Relancer ensuite `tools/publish-signed-manifest.py`. Les scripts
d'exploitation serveur (déploiement de l'API, publication du manifeste signé, preuves de production) restent dans le
dépôt privé du projet et ne sont pas publiés dans ce dépôt public.
**Retour arrière :** remettre l'ancien bloc `launcher` (0.10.10) arrête les migrations ; les joueurs déjà migrés
restent sur le nouveau launcher (une version inférieure n'est jamais proposée) et reçoivent les correctifs par
une version supérieure. Attention : le CI de l'ancien launcher réécrit ce bloc à chaque release 0.10.x.

## Captures

- [Accueil](docs/captures/home.png)
- [Connexion](docs/captures/login.png)
- [Bibliothèque](docs/captures/library.png)
- [Téléchargement](docs/captures/installing.png)
- [Classement](docs/captures/leaderboard.png)
- [Historique](docs/captures/matches.png)
- [Paramètres](docs/captures/settings.png)
- [Erreur détaillée](docs/captures/error.png)
- [Notes de patch](docs/captures/note.png)

Les chiffres et contenus des captures sont ceux de l'aperçu.

## Structure

```
src/Core/       API, sessions, erreurs, logs, fichiers, installateur, jeu
src/UI/         thème, icônes, animation, effets, contrôles et pages
src/LauncherApp.cs   orchestration des états
bootstrap/      apphost local, hors build WPF SDK
assets/         polices, icône et configuration par défaut
tests/          vérifications du noyau, UI, captures et performance
docs/           preuves et captures
```

Les polices Oswald sont accompagnées de leur [licence SIL OFL](assets/fonts/OFL-Oswald.txt).
Le paysage et les icônes de l'interface sont construits par code ; aucune image de jeu n'est embarquée.

## Rangs, icône et support (ajouts)

- **Emblèmes de rang** : les six `icon_tier_01..06.dds` du client PS3 ont été lus en lecture seule
  dans `Assets_*.pack` puis recadrés en PNG (`assets/ranks/`, provenance et SHA-256 dans
  `assets/ranks/provenance.json`, script `tests/import-client-art.py`). Ces images appartiennent
  au jeu : elles sont utilisées pour ce launcher communautaire, à valider avant une diffusion publique.
  Si les PNG manquent, l'interface retombe sur les écussons vectoriels.
- **Icône** : couronne d'origine conservée, teinte orange braise (`tests/ember-icon.py`,
  aperçu `docs/icon-orange.png`), ICO 16 à 256 px. L'ancienne icône n'est pas modifiée.
- **Envoi de logs au support** (Paramètres, ou bouton « Envoyer au support » dans toute erreur) :
  sources `launcher`, `H1Z1.log`, `H1Z1 PlayClient (Live).log`, `Login.log`, `NetInfo.log` ;
  50/200/500 lignes max., 6 000 caractères par source ; aperçu exact, consentement explicite,
  masquage des tickets, mots de passe, e-mails, IP, nom du PC et dossier utilisateur ;
  numéro de rapport renvoyé. Jamais d'envoi automatique.
- **Production** (`D:\projects\zemu\backend`, migration `045`) : `POST /api/v1/launcher/reports`
  (session launcher, 5 rapports/h/compte, 10 req./min/IP), lecture réservée au staff revalidé en base,
  expiration à **7 jours** (illisible dès l'expiration, suppression physique toutes les 15 min).
  Testé avec PostgreSQL local. **Non déployé** : en attendant, le launcher affiche `E-SUPPORT-002`.
  Déploiement : le workflow `tools/ci/backend/deploy.yml` ou `docker compose up -d --build api`.
