# Développer et publier le launcher COTK

Ce guide complète [AGENTS.md](../AGENTS.md). Les commandes partent de la racine du dépôt.

## 1. Choisir le bon dépôt

| Emplacement | Rôle |
|---|---|
| `LucBhub/Launcher-COTK`, branche `launcher-next` | Source active du launcher WPF et publication sur push |
| `D:\projects\Launcher-COTK` | Clone de développement sur le poste actuel |
| Branche `main` du même dépôt | Ancien launcher WinForms et son ancien CI |
| `D:\projects\zemu\launcher` | Copie historique WinForms ; pas le Next |
| `D:\projects\COTK\tools\launcher-next` | Ancien emplacement et outils privés du poste de publication |

Ne pas synchroniser le monorepo historique vers `main` pour publier le Next : cela relancerait le mauvais
workflow. Ne pas exporter l'ancien miroir par-dessus la branche active : son code/version peut être ancien.
Garder les outils privés à leur emplacement tant que l'agent les utilise via `COTK_PUBLISHER_HOME`.

```powershell
git clone --branch launcher-next https://github.com/LucBhub/Launcher-COTK.git
```

Au début d'une intervention :

```powershell
git status --short
git branch -vv
git remote -v
git log --oneline -5
```

Si le répertoire est propre, faire `git pull --ff-only` pour récupérer les métadonnées de version du robot.
Sinon, examiner les modifications/divergences avant de synchroniser. Ne pas reset/force-push le travail existant.
Pour préparer sans publier, utiliser une branche de travail : `git switch -c work/description-du-changement`.
Le passage sur `launcher-next` et sa publication doivent être autorisés.

## 2. Carte du code

| Besoin | Points d'entrée |
|---|---|
| Démarrage, arguments, instance unique, mode updater | `src/Program.cs` |
| Session, données, opérations et états exposés aux vues | `src/LauncherApp.cs` |
| Fenêtre, navigation et pages | `src/UI/MainWindow.cs`, `src/UI/Views/` |
| Contrôles, dialogues, scènes et animations | `src/UI/Controls/`, `src/UI/Anim.cs`, `src/UI/Ticker.cs` |
| HTTP et contrats API | `src/Core/ApiClient.cs`, `src/Core/Models.cs` |
| Réglages, chemins, réseau, stockage du jeton | `src/Core/Settings.cs`, `src/Core/AppPaths.cs`, `src/Core/Config.cs`, `src/Core/CredentialStore.cs` |
| Traductions et texte localisé | `src/Core/L.cs`, `src/Core/L.Catalogue.cs`, `src/UI/LocalizedText.cs` |
| Installation, delta, réparation, reprise et disque | `src/Core/Updates/ClientInstaller.cs`, `src/Core/Updates/StorageCheck.cs`, `src/Core/Updates/HashCache.cs`, `src/Core/Updates/TransferPause.cs` |
| Désinstallation et conservation des fichiers | `src/Core/Updates/ClientUninstaller.cs` |
| Chemins sûrs, manifestes et données joueur | `src/Core/Updates/Manifest.cs` |
| Signature et mise à jour du launcher | `src/Core/Updates/SignedRelease.cs`, `src/Core/Updates/LauncherUpdater.cs` |
| Précontrôles, ticket, configuration et surveillance du jeu | `src/Core/Game/LaunchPreflight.cs`, `src/Core/Game/GameFiles.cs`, `src/Core/Game/PlaySession.cs` |
| Console admin/modérateur | `src/Core/Game/StaffConsole.cs` |
| Erreurs et diagnostics | `src/Core/Errors.cs`, `src/Core/Log.cs`, `src/Core/SupportReport.cs` |
| Publication sur push | `.github/workflows/release-next.yml`, `tools/ci-release.py` |

L'UI est en C#, pas en XAML. Les vues consomment les états de `LauncherApp` et son événement `Changed` ; elles
ne doivent pas réimplémenter téléchargements et sessions. Les écritures UI passent sur le Dispatcher ; les
lectures longues, hachages et extractions restent hors du thread UI.

## 3. Construire et regarder sans toucher au joueur

### Build local Roslyn

Le poste actuel dispose de Roslyn BuildTools et du runtime .NET Desktop 10 x64 :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
.\APERCU.cmd
```

`APERCU.cmd` utilise le binaire existant si présent : **recompiler après une modification**.
Pour construire puis lancer directement l'aperçu :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Run -Preview
```

`--preview` utilise des données fictives et des réglages séparés, ne lit pas les identifiants Windows et ne
lance pas le jeu. Les arguments `--screen=settings`, `--screen=library`, `--screen=installing` et
`--lang=fr`/`en`/`zh-CN` servent à vérifier les scénarios et les langues ; voir `src/Program.cs` et les scripts de capture.

`LANCER.cmd` est au contraire le mode réel (API, réglages, session du joueur). Ne pas le confondre avec l'aperçu,
ni déclencher une installation/désinstallation réelle pour un simple test visuel.

### SDK et paquet distribuable

Le SDK du poste est dans `%LOCALAPPDATA%\cotk-dotnet-sdk`, sans modification du PATH :

```powershell
& "$env:LOCALAPPDATA\cotk-dotnet-sdk\dotnet.exe" publish COTK.Launcher.Next.csproj -c Release -r win-x64 --self-contained true -p:AssemblyName=COTK.Launcher -o publish-local
```

Localement, `out/COTK.Launcher.Next.exe` demande le runtime installé. Le zip de release doit contenir
**`COTK.Launcher.exe`**, sa DLL, ses ressources et son runtime. Ce nom historique est nécessaire pour
l'auto-update des versions déjà installées et leurs raccourcis : ne pas le changer.
`out/`, `bin/`, `obj/`, `publish*/`, `release/` et caches Python ne sont pas des sources à committer.
Le pipeline fabrique les vrais paquets ; une compilation locale n'est pas une publication.

## 4. Vérifier selon le changement

| Modification | Vérifications |
|---|---|
| C#, Core, installation, réglages, réseau | Build et tests Core ; ajouter un scénario significatif si le comportement change |
| UI, navigation, dialogues, interactions | Build, tests Core/UI, observation du rendu en aperçu |
| Traductions | Trois langues et données libres ; tests Core/UI, captures si le rendu change |
| Animations/scènes | Tests UI, captures et comparaison avec `docs/performance.json` |
| Publication/auto-update | Tests Python et passage réel d'une version à l'autre en bac à sable |
| Documentation seule | Liens/chemins, commandes, filtres CI, `git diff --check` |

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-ui-tests.ps1
python -m unittest discover -s tests -p "test*release.py" -v
```

Le build précède les tests UI, qui reprennent `out/build.rsp`. Les tests Core recompilent leurs propres sources.
Une compilation échouée, un exit non nul ou une assertion en échec ne sont pas à ignorer.
Ces tests sont des exécutables de test et des scripts Python, pas l'ancien projet xUnit WinForms :
`dotnet test` seul ne remplace pas cette suite. Si les outils requis manquent sur un autre poste,
le signaler et configurer l'environnement ; ne pas présenter des tests non exécutés comme réussis.

Compléments, quand ils sont pertinents :

```powershell
# Lecture de la production et contrôles de refus sans authentification
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-tests.ps1 -ApiSmoke

powershell -NoProfile -ExecutionPolicy Bypass -File tests/capture-ui.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/capture-languages.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests/performance.ps1
```

Fichiers et suppressions de test restent dans les bacs à sable prévus : ne pas remplacer le vrai client ni
supprimer un compte réel pour fabriquer une fixture. Ne pas arrêter une partie pour obtenir un test vert.

### Test de mise à jour

`tools/e2e-update.py` télécharge et vérifie le launcher actuellement publié, l'installe dans un bac à sable,
puis exige sa mise à jour automatique vers le zip candidat. Il contrôle données joueur, nettoyage et processus :

```powershell
python tools/e2e-update.py --to-zip release/COTK-Launcher-Next-X.Y.Z-win-x64.zip --to-version X.Y.Z
```

Le mode normal refuse de remplacer une session mémorisée. En CI, `--keep-existing-session` laisse le launcher
de test la lire face au faux serveur loopback ; le banc ne l'écrit/supprime pas et compare son empreinte avant
et après sans l'afficher. Les en-têtes ne sont pas journalisés. Préserver ces contrôles et ne pas envoyer le
jeton vers une autre API. Le banc peut fermer le launcher pour libérer le mutex, mais ne force pas un launcher
qui reste ouvert.

`tools/e2e-migration.py` reste le banc historique 0.10.10 → 1.0.2 → 1.0.3, avec versions/paquets fixes.
Pour les prochaines releases normales, utiliser **`e2e-update.py`**.
La suite Python du clone public peut contenir moins de tests que le CI : l'agent injecte aussi une suite privée
de garde-fous. Ne pas créer de faux script privé pour masquer son absence sur un clone ordinaire.

## 5. Préserver le client et les réglages

- `%APPDATA%\COTK\settings.json` et `clientDir` sont partagés avec l'ancien launcher. Conserver les clés inconnues,
  le BOM toléré et l'écriture atomique. Sans préférence : `client/` à côté du launcher.
- Le marqueur reste `.cotk-client-version`. Un client présent dans une ancienne version demande une **mise à jour**,
  pas une première installation.
- Les `.ini`, `InputProfile_*.xml`, `SoundSettings.xml`, `UI/Settings/` et logs existants ne doivent pas être remis
  aux défauts du catalogue, même en réparation. Respecter `ManifestRules.IsPlayerData`.
- Le delta ne supprime que les fichiers obsolètes de son inventaire. Lors de la première migration sans inventaire,
  ne pas supprimer des fichiers inconnus.
- Le repli par archive remplace le dossier puis restaure les réglages protégés. Les captures/mods/fichiers inconnus
  ne sont pas tous garantis sur ce chemin : ne pas affirmer le contraire.
- `ClientConfig.ini` est adapté au lancement pour le ticket et le serveur ; c'est distinct des réglages graphiques,
  sonores et des touches. Les logs doivent masquer tickets et jetons.
- Le zip du launcher ne contient jamais le client, ses données ou `ToggleConsole`. L'accès staff reste contrôlé par
  l'API ; un bouton masqué n'est pas une autorisation. La console utilise `--native-ui`, pas `--patch`.

Les scénarios de `tests/CoreTests.cs` et `tests/UninstallTests.cs` couvrent ces invariants. Les étendre si le
comportement change, avec conservation des données et cas de refus pertinents.

## 6. Textes et diagnostics

Dans `src/Core/L.Catalogue.cs`, une ligne = `français|anglais|chinois simplifié`. Tout nouveau texte visible doit
avoir ses trois traductions. Conserver les variables (`{0}`, `{1}`, etc.) et tester la langue à chaud.
L'anglais est le défaut. Utiliser les helpers localisés pour les libellés et `Ui.Raw` ou le mécanisme existant
approprié pour les pseudos, chemins, contenus distants et extraits de logs. Les codes d'erreur restent stables.

Réseau absent, disque illisible, archive invalide et mauvaise empreinte doivent produire un état explicite avec
une action utile. Ne pas transformer une erreur en « prêt ». Pas d'envoi automatique des logs sans consentement,
ni de repli sur un manifeste non signé pour contourner une erreur de signature.

## 7. Un push peut être un déploiement

La référence est [le workflow](../.github/workflows/release-next.yml), pas un numéro fixé dans cette documentation.

| Push | Effet |
|---|---|
| `launcher-next` + code, ressources, bootstrap, tests, outils, projet, manifeste, build ou workflow surveillé | Release automatique |
| `launcher-next` + uniquement `AGENTS.md`, `README.md`, `docs/**` | Aucune nouvelle release binaire |
| Branche de travail distincte | Pas de release par ce workflow |
| `main` | Ancien workflow historique : ne pas l'utiliser pour le Next |

Le pipeline accepte seulement les pushes du dépôt/de la branche autorisés, sérialise les jobs et construit dans
un dossier isolé. Il récupère les outils privés via `COTK_PUBLISHER_HOME`, choisit le prochain patch libre (ou
respecte une version déclarée supérieure), puis teste, construit et vérifie l'upgrade depuis la version publiée.
Un numéro réservé lors d'un échec peut être sauté : ne pas le réutiliser ni supposer « +1 » exact.

Le zip et le setup d'installation (`COTK-Launcher-Next-Setup-X.Y.Z.exe`, construit par `tools/package-setup.ps1`
avec Inno Setup 6) sont d'abord envoyés sans être annoncés ; une preuve locale lie paquets, sources et contrôles.
Ensuite : vérification de clé, sauvegarde du manifeste, publication (le bloc `launcher` pointe `setupUrl` sur le
setup de la version), signature, mise à jour de l'alias `cotk-launcher-setup-latest.exe` et vérification réelle.
Conserver les
garde-fous de restauration ; ils ne rétrogradent pas un joueur déjà migré.

Le robot pousse seulement les métadonnées de version et de FAQ avec **`[skip ci]`**, plus le tag immuable
**`next-vX.Y.Z`**. Ne pas supprimer l'anti-boucle ni forcer un tag. Les artefacts et `result.json` sont joints
au job (rétention configurée : 14 jours).

Quand la publication est autorisée :

```powershell
git diff --check
git status --short
git diff
git add src/chemin/du/fichier.cs
git commit -m "fix(launcher): description du changement"
git push origin launcher-next
```

Après réussite, récupérer le commit du robot avec `git pull --ff-only` si le répertoire est propre.
Ne pas pousser un ancien arbre par-dessus ses changements.

## 8. Suivre et diagnostiquer

Workflow et historique : <https://github.com/LucBhub/Launcher-COTK/actions>.
Avec un GitHub CLI authentifié dans le PATH :

```powershell
gh run list --repo LucBhub/Launcher-COTK --branch launcher-next --limit 5
gh run view ID_DU_RUN --repo LucBhub/Launcher-COTK
gh run view ID_DU_RUN --repo LucBhub/Launcher-COTK --log-failed
```

Contrôles publics en lecture seule :

```powershell
curl.exe --fail --silent https://api.cotk.fr/api/v1/launcher/updates
curl.exe --fail --silent https://dl.cotk.fr/launcher-next/manifest.signed.json
git ls-remote --tags origin "refs/tags/next-v*"
```

HTTP 200 et présence d'un JSON ne prouvent pas sa signature : le launcher et le publisher la vérifient.
Le zip servi doit aussi correspondre à la taille et au SHA-256 publiés.

| Symptôme | Première vérification |
|---|---|
| Job en attente | PC allumé, session éditeur ouverte, agent `COTK-Launcher-Windows` en ligne, label `cotk-launcher-release` |
| Échec avant publication | Logs, contrôles et `result.json` ; ne pas sauter le test qui échoue |
| Fichier publisher absent | Copie des scripts ; un filtre `publish*` exclurait aussi `publisher.ps1` |
| Session signalée | Ne pas effacer les identifiants ; vérifier le mode préservé et son contrôle avant/après |
| Push obsolète | Un commit plus récent existe ; ne pas rétablir l'ancien arbre/forcer le push |
| Échec Git/artefact en fin de job | Lire d'abord le manifeste : la version peut être déjà en production |
| Échec de clé/signature | Compte Windows, publisher et clé épinglée ; ne pas recréer une clé pour débloquer |

Ne pas éditer `_work` ou les builds de l'agent pour simuler un correctif. Garder les preuves et corriger les
sources. Une relance tient compte de la production, des versions réservées et des paquets immuables.
**Un job rouge ne signifie pas automatiquement que rien n'a été déployé.**

## 9. Ce qui reste local au publisher

- Clé CNG non exportable, jamais dans le dépôt, les logs, le zip ou GitHub Secrets.
- Accès et outils privés fournis par `COTK_PUBLISHER_HOME`. Une référence à un script dans le README
  ne signifie pas qu'il doit être présent sur le dépôt public.
- Tâche **COTK GitHub Release Runner**, en session utilisateur interactive pour les tests WPF.
- Tâche **COTK Renew Signed Manifest** : renouvelle une publication déjà approuvée, pas un contenu nouveau
  arbitraire. La nouvelle release du launcher est signée par le workflow ; une release du client du jeu
  demande aussi sa publication signée dans le processus d'exploitation séparé.

Ne pas déplacer les outils, changer leurs tâches ou la clé comme effet secondaire d'un changement UI.
La distribution reste sans Authenticode : pas de dépendance à un certificat payant sans demande.
Les commandes locales de secours du README sont réservées à une exploitation autorisée.

## 10. Terminer une intervention

Indiquer les fichiers modifiés, les commandes réellement exécutées, leurs résultats et les limites restantes.
Dire si un commit/push a été fait. Pour une release : URL du job, version, tag et vérification du manifeste.
Ne pas présenter un envoi ou un démarrage comme une application réussie.
Maintenir `AGENTS.md` et ce guide quand les commandes, invariants ou le workflow changent.
