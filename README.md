# Launcher COTK

Launcher WinForms (.NET 10) pour **COTK — Crown of the King**, projet de
restauration de *H1Z1: King of the Kill* Pre-Season 3 (build 0.23.4.161178).

## Fonctionnalités

- **Connexion** via l'API COTK (`POST /api/v1/launcher/login`) — les comptes
  se créent sur le site, jamais dans le launcher
- **Session persistée** dans le Gestionnaire d'identification Windows
  (`COTK.Launcher.Session`) ; une session administrateur n'est jamais persistée
- **Vérification serveur** : les ports UDP 20042/60000 doivent répondre avant
  de lancer le client (le launcher ne démarre pas le serveur de jeu)
- **Téléchargement du client** : première installation avec choix du dossier,
  confirmation de taille, téléchargement avec reprise HTTP Range et % en direct
- **Mise à jour différentielle** : compare un catalogue SHA-256 fichier par
  fichier, ne télécharge que les différences (4 en parallèle)
- **Auto-update du launcher** : télécharge le zip, extrait via un updater
  temporaire, remplace les fichiers, relance
- **Console développeur F8** (compte admin) : `/help`, `/give`, `/spawncar`, `/godmode`...

## Flux utilisateur

```text
Ouvrir le launcher
  → Écran de connexion (rien ne se passe avant login)
  → Login réussi
      → Vérification des versions (launcher + client)
      → Si launcher périmé → auto-update silencieux → relance
      → Si client absent → bouton TÉLÉCHARGER LE CLIENT
          → Choix du dossier d'installation
          → Confirmation de taille (13,6 Go)
          → Téléchargement avec reprise + % en direct
          → Extraction → marqueur .cotk-client-version
      → Si client à jour → bouton JOUER MAINTENANT
  → Clic JOUER → ticket lp2 → ClientConfig.ini → H1Z1.exe
```

## Configuration

| Variable | Description | Défaut |
|---|---|---|
| `COTK_API_URL` | URL de l'API (HTTPS obligatoire hors localhost) | `https://api.cotk.fr` |
| `COTK_GAME_SERVER` | Adresse du serveur de jeu (ip:port) | `164.132.200.95:20042` |

En dev local, `JOUER.bat` surcharge ces valeurs vers `localhost`.

## Compilation

```powershell
dotnet publish src/COTK.Launcher.csproj -c Release -r win-x64 --self-contained true
```

Prérequis : Windows + .NET 10 SDK.

## Tests

```powershell
dotnet test tests/COTK.Launcher.Tests.csproj
```

29 tests couvrant : config client, détection de stage, retry logic,
delta update (sandbox + serveur HTTP simulé), reprise de téléchargement,
régression 416, BOM settings, validation d'archive updater.

## Assistant d'installation (Inno Setup)

Le zip publié contient `Install-COTK.bat` → `install/install.ps1` :
- Installation dans `%LOCALAPPDATA%\COTK` (sans droits admin)
- Raccourcis Bureau + Menu démarrer
- Entrée Ajout/Suppression de programmes (désinstallation qui conserve les données joueur)

## Avertissement

Projet communautaire à but éducatif, non affilié à Daybreak Game Company,
Z1 Games ni aucun détenteur des droits sur H1Z1. Ce dépôt contient uniquement
le code source du launcher — aucun asset ni binaire du jeu n'est distribué ici.
