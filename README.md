# COTK Launcher

Launcher WinForms pour **COTK — Crown of the King**, un projet de restauration
de *H1Z1: King of the Kill* Pre-Season 3 (build 0.23.4.161178, 2017).

## Fonctionnalités

- Connexion via l'API COTK — les comptes se créent sur le site, jamais ici.
- Session persistée dans le Gestionnaire d'identification Windows
  (`COTK.Launcher.Session`) ; une session administrateur n'est jamais persistée.
- Vérifie que les ports UDP `20042` et `60000` du serveur répondent avant de
  lancer le client (le launcher ne démarre pas le serveur de jeu).
- Mise à jour du client gérée côté serveur (`UpdateService`).
- En jeu avec un compte admin : **F8** ouvre la console développeur
  (`/help`, `/give`, `/spawncar`, `/godmode`...).

## Compilation

Prérequis : Windows + [.NET 10 SDK](https://dotnet.microsoft.com).

```powershell
dotnet publish src/COTK.Launcher.csproj -c Release -r win-x64
```

L'exécutable est publié dans
`src/bin/Release/net10.0-windows/win-x64/publish/COTK.Launcher.exe`.

## Tests

```powershell
dotnet test tests/COTK.Launcher.Tests.csproj
```

## Configuration

| Variable | Description | Défaut |
|---|---|---|
| `COTK_API_URL` | URL de l'API d'authentification COTK | `http://localhost:8080` |

## Avertissement

Projet communautaire à but éducatif, non affilié à Daybreak Game Company,
Z1 Games ni à aucun détenteur des droits sur H1Z1. Ce dépôt contient
uniquement le code source du launcher : aucun asset ni binaire du jeu
n'est distribué ici.
