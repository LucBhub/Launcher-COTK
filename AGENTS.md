# Launcher COTK — instructions pour les agents

## Lire ceci avant de modifier

- **Dépôt actif :** `LucBhub/Launcher-COTK`, branche **`launcher-next`**. Sur le poste actuel :
  `D:\projects\Launcher-COTK`. `main` contient le launcher WinForms historique et son ancien CI.
- **Un push de code sur `launcher-next` publie en production.** Ce n'est pas une sauvegarde Git.
  Ne pousser que si l'utilisateur a demandé/autorisé une publication. Pour préparer du travail sans
  publier, utiliser une branche de travail distincte ; ne pas publier le Next sur `main`.
- Lire [le guide de développement](docs/DEVELOPMENT.md) pour les procédures et la carte du code.
  Le [README](README.md) décrit les fonctionnalités.
- `D:\projects\COTK\tools\launcher-next` est l'ancien emplacement et le poste d'outils privés,
  **pas la source de développement à exporter par-dessus ce dépôt**. Ne pas le supprimer/déplacer :
  l'agent de publication y récupère ses scripts privés via `COTK_PUBLISHER_HOME`.

## Boucle de travail

1. Inspecter `git status`, la branche, les commits récents et le diff. Préserver le travail de
   l'utilisateur. Si le répertoire est propre, faire `git pull --ff-only` avant de partir de
   `launcher-next` : le robot y ajoute les métadonnées de version après une release. Ne pas forcer
   en cas de divergence.
2. Repérer les appelants et les tests du code concerné. Si un index GitNexus est disponible, vérifier
   sa fraîcheur puis analyser l'impact ; sinon utiliser la recherche texte et les tests. Un résultat
   vide/incomplet n'est pas une preuve de sûreté. Ce dépôt n'a pas nécessairement le runner
   `.gitnexus/run.cjs` du serveur de jeu : ne pas inventer cette commande ici.
3. Modifier les sources, pas les sorties de build. Préserver les conventions ci-dessous.
4. Pour du C#, compiler puis exécuter les tests pertinents. Le build doit précéder les tests UI :

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
   powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-tests.ps1
   powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-ui-tests.ps1
   ```

   Pour les scripts de publication :

   ```powershell
   python -m unittest discover -s tests -p "test*release.py" -v
   ```

   Les vérifications supplémentaires sont dans le guide. Documentation seule : vérifier les liens,
   chemins, commandes, filtres CI et `git diff --check` ; pas de rebuild du jeu pour cela.
5. Pour regarder l'UI, **recompiler**, puis utiliser `APERCU.cmd` ou
   `out\COTK.Launcher.Next.exe --preview`. `APERCU.cmd` ne recompile pas si l'exécutable existe déjà.
   `LANCER.cmd` est le mode réel : ne pas le lancer sans nécessité/accord.
6. Avant un commit autorisé : revoir `git diff`, `git diff --check`, `git status` et ne stager que les
   fichiers voulus. Avant un push autorisé sur `launcher-next`, vérifier les tests puis suivre
   **Publish Launcher Next**. Ne pas annoncer « déployé » parce que le push a réussi : contrôler le
   job et les manifestes publics.

## Conventions et invariants

- WPF .NET 10, UI **en C# sans XAML**. `LauncherApp` orchestre, `Core/` porte la logique, `UI/` affiche
  et réagit à `Changed`. Pas d'IO lourde sur le thread UI ; utiliser le Dispatcher pour l'UI.
- Local : `COTK.Launcher.Next.exe` et sa DLL dans `out/`, runtime requis.
  **Release : `COTK.Launcher.exe` à la racine du zip**, runtime embarqué, publié avec
  `-p:AssemblyName=COTK.Launcher`. Les anciens launchers exigent ce nom pour migrer et relancer.
- Réglages partagés avec l'ancien launcher : `%APPDATA%\COTK\settings.json`, clé `clientDir`.
  Conserver les clés inconnues et l'écriture atomique ; ne pas réinitialiser les préférences pendant
  une mise à jour. Le marqueur client reste `.cotk-client-version`.
- Ne pas écraser les `.ini`, `InputProfile_*.xml`, `SoundSettings.xml`, `UI/Settings/` et journaux
  existants du joueur. Respecter `ManifestRules.IsPlayerData`, l'inventaire des fichiers gérés et les
  contrôles de chemins/jonctions. Le repli par archive garantit ces réglages, pas tous les fichiers
  personnels inconnus : ne pas promettre davantage sans le modifier et le prouver.
- `ClientConfig.ini` reçoit le ticket et l'adresse serveur à chaque lancement ; ce n'est pas une remise
  à zéro des options graphiques ou des touches. Ne jamais journaliser de ticket.
- UI traduite via `src/Core/L.Catalogue.cs` : français historique, anglais, chinois simplifié.
  Anglais par défaut. Contrôles localisés pour les libellés, `Ui.Raw` pour pseudos, chemins et données
  libres. Garder le changement de langue à chaud.
- En production : manifeste **signé** obligatoire, catalogue épinglé, SHA-256 vérifié. Ne pas ajouter
  de repli non signé, désactiver TLS ou élargir les hôtes autorisés pour débloquer un téléchargement.
  Les surcharges réseau par environnement restent limitées à la boucle locale.
- Console F8 : **admin/modérateur uniquement**, API staff, jamais dans le paquet public. Garder
  `--native-ui`, jamais `--patch`. Ne pas persister les sessions staff.
- Rapports support : consentement explicite, extrait expurgé et borné. Pas de secrets, de clé privée,
  d'identifiants réels ou de logs utilisateur dans les commits/artefacts publics.

## Publication et agent Windows

- Workflow : `.github/workflows/release-next.yml`. Les pushes de `launcher-next` touchant ses chemins
  surveillés déclenchent une release. Documentation seule (`AGENTS.md`, `README.md`, `docs/`) ignorée.
  Une branche de travail ou une pull request ne publie pas via ce workflow.
- Patch automatique : pas de bump manuel ordinaire ni de tag à forcer/réutiliser. Le robot pousse les
  métadonnées avec **`[skip ci]`** et crée **`next-vX.Y.Z`**. Préserver l'anti-boucle, la sérialisation,
  le refus des pushes obsolètes et le contrôle des sources/paquets testés.
- Agent **COTK-Launcher-Windows**, tâche **COTK GitHub Release Runner** : PC allumé et session Windows
  de l'éditeur ouverte. Ne pas modifier ses tâches ni désenregistrer l'agent pour un changement UI.
- Checkouts/builds CI isolés : ne pas modifier `_work`, les dossiers de release de l'agent ou la
  production pour remplacer un vrai commit. Garder les tests d'update en bac à sable, la préservation
  de session en CI et le nettoyage limité à leurs propres processus/fichiers.
- La clé privée CNG est non exportable et reste sur le PC. Ne pas la recréer ni changer la clé publique
  embarquée sans migration de confiance explicite. Les scripts privés de `COTK_PUBLISHER_HOME` sont
  volontairement absents du dépôt public : ne pas les republier.
- Job rouge : vérifier **la version en production avant de relancer**, car l'échec peut arriver après
  la mise en ligne (métadonnées Git ou artefacts). Ne pas contourner les tests. Une restauration arrête
  les nouvelles migrations ; elle ne rétrograde pas les joueurs déjà mis à jour.

## Compte rendu attendu

Fichiers modifiés, commandes réellement exécutées et résultats, limites restantes, commit/push faits
ou non. Pour une release : lien du job, version, tag et vérification du manifeste ; récupérer le commit
automatique avec `git pull --ff-only` si le répertoire est propre.
