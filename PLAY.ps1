# ZEmu PLAY - one-click playable: PS3 client -> config -> servers -> game.
#
# WORKING STACK (validated 2026-08-24, full solo match played):
#   Mixcria C# server + opcode fix (NetworkProximityUpdatesComplete =
#   '11 34 00', no payload) + transport dials. See server/DIAGNOSTIC.md.

param([switch]$NoGame)

# Mode dev local : surcharge des defauts production du launcher.
$env:COTK_API_URL = $env:COTK_API_URL ?? 'http://localhost:8080'
$env:COTK_GAME_SERVER = $env:COTK_GAME_SERVER ?? '127.0.0.1:20042'

# Tunables below - safe to edit:
$LobbySeconds    = 30     # duree du compte a rebours de lobby avant le drop
$MatchAutostart  = 10     # demarrage auto du premier match apres arrivee (0 = manuel F8 /startmatch)
$MaxOutstanding  = 96     # datagrams reseau en vol (96 = fiable; baisser si pertes)
$SweepMs         = 40     # frequence d'envoi reseau (40 ms -> chargement ~10-15 s)
# Lobby dynamique : si le roster est petit, la map ouvre deja reduite par le gaz
# (rayon ~ sqrt(joueurs/150), plancher $DynamicGasMinRadius ; roster complet = map retail).
$DynamicGas      = 1      # 1 = active, 0 = map retail 4200 quel que soit le nombre de joueurs
$DynamicGasMinRadius = 900    # rayon mini du cercle de depart (~1.8 km de large)

$ErrorActionPreference = "Stop"
$Root    = $PSScriptRoot                                   # launcher\
$Repo    = Split-Path $Root -Parent                        # repo root
$Tools   = Join-Path $Repo "client\tools"
$Ps3Srv  = Join-Path $Repo "server\H1Z1-2017-CSharp-Server"

function Info($m) { Write-Host "[ZEmu] $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "[OK]   $m" -ForegroundColor Green }
function Warn($m) { Write-Host "[!]    $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "[X]    $m" -ForegroundColor Red; exit 1 }

# --- 1. locate / fetch the real PS3 client -------------------------------
$clientDir = $null
foreach ($c in @((Join-Path $Repo "client"))) {
    if (Test-Path (Join-Path $c "H1Z1.exe")) { $clientDir = $c; break }
}

if (-not $clientDir) {
    $clientDir = Join-Path $Repo "client"
    New-Item -ItemType Directory -Path $clientDir -Force | Out-Null
    Warn "H1Z1.exe introuvable -> telechargement du VRAI build PS3 (KotK manifest 6098349229565958949)."
    Warn "Un compte Steam est requis UNE SEULE fois (Z1BR est F2P : tout compte fonctionne)."
    & (Join-Path $Tools "get_client.ps1") -Preset ps3 -OutDir $clientDir
}

$h1z1 = Join-Path $clientDir "H1Z1.exe"
if (-not (Test-Path $h1z1)) { Fail "H1Z1.exe introuvable dans $clientDir" }
Ok "Client : $h1z1"

# --- 2. ClientConfig.ini (Mixcria's exact spec, port 20042) ---------------
$cfgPath = Join-Path $clientDir "ClientConfig.ini"
$example = Join-Path $Ps3Srv "ClientConfig.example.ini"
if (-not (Test-Path $cfgPath)) {
    if (-not (Test-Path $example)) { Fail "ClientConfig.example.ini manquant dans $Ps3Srv" }
    Copy-Item $example $cfgPath -Force
    Ok "ClientConfig.ini installe depuis l'exemple officiel (Server=127.0.0.1:20042)"
} else {
    $cfg = Get-Content $cfgPath -Raw
    if ($cfg -notmatch "Server=127\.0\.0\.1:20042") {
        Copy-Item $example $cfgPath -Force
        Ok "ClientConfig.ini remis d'aplomb (Server=127.0.0.1:20042)"
    } else {
        Ok "ClientConfig.ini deja cible sur le serveur local"
    }
}

# --- 3. server tuning (env vars read by the patched build) ----------------
$env:H1Z1_MIN_PLAYERS      = "1"
$env:H1Z1_MATCH_AUTOSTART  = "$MatchAutostart"
$env:H1Z1_LOBBY_SECONDS    = "$LobbySeconds"
$env:H1Z1_MAX_OUTSTANDING  = "$MaxOutstanding"
$env:H1Z1_SWEEP_MS         = "$SweepMs"
$env:H1Z1_DYNAMIC_GAS      = "$DynamicGas"
$env:H1Z1_DYNAMIC_GAS_MIN_RADIUS = "$DynamicGasMinRadius"

# Advertise the zone on the LAN IP so a second player can join (see LAN.md).
# Loopback still works locally even when the LAN IP is advertised.
$lanIp = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.InterfaceAlias -notmatch "Loopback|vEthernet|WSL" -and
                   $_.IPAddress -notlike "169.254.*" -and $_.IPAddress -ne "127.0.0.1" } |
    Select-Object -First 1).IPAddress
if ($lanIp) {
    $env:H1Z1_ZONE_ADDRESS = "${lanIp}:60000"
    Ok "Zone annoncee sur le LAN : ${lanIp}:60000 (un 2e joueur peut rejoindre - voir LAN.md)"
} else {
    $env:H1Z1_ZONE_ADDRESS = "127.0.0.1:60000"
}

# --- 3b. integration web (panneau admin : progression XP/crates + dons de crates et skins) ---
# Sans ces variables, le serveur de jeu ignore totalement l'API (WebConfigured=false).
$env:H1Z1_WEB_API = "http://localhost:8080"
$adminDotEnv = Join-Path $Repo "admin\.env"
if (Test-Path $adminDotEnv) {
    Get-Content $adminDotEnv | ForEach-Object {
        if ($_ -match '^\s*INTERNAL_API_KEY\s*=\s*(\S+)\s*$') { $env:H1Z1_SERVER_KEY = $Matches[1].Trim() }
    }
}
if (-not $env:H1Z1_SERVER_KEY) { Warn "INTERNAL_API_KEY absente de admin\.env -> pas de synchro web en jeu" }

# --- 4. start the servers (Start-H1Z1.cmd = official recipe) --------------
$running = Get-Process -Name "H1Z1.Server" -ErrorAction SilentlyContinue
if ($running) {
    Ok "Serveur PS3 deja lance (PID $($running.Id)). Ferme sa fenetre pour appliquer de nouveaux reglages."
} else {
    $starter = Join-Path $Ps3Srv "Start-H1Z1.cmd"
    if (-not (Test-Path $starter)) { Fail "Start-H1Z1.cmd introuvable ($starter)" }
    Info "Demarrage du serveur PS3 (login UDP 20042 + zone UDP 60000, lobby ${LobbySeconds}s)..."
    Start-Process -FilePath $starter -WorkingDirectory $Ps3Srv
    $up = $false
    foreach ($i in 1..30) {
        Start-Sleep -Milliseconds 1000
        if (Get-NetUDPEndpoint -LocalPort 20042 -ErrorAction SilentlyContinue) { $up = $true; break }
    }
    if ($up) { Ok "Serveur pret (UDP 20042 + 60000)." }
    else { Warn "Ports non detectes apres 30 s - regarde la fenetre du serveur." }
}

# --- 5. launch the game ---------------------------------------------------
if ($NoGame) { Ok "Mode -NoGame : serveur seul."; exit 0 }

Info "Lancement de H1Z1.exe (sans arguments - ClientConfig.ini pilote)..."
Start-Process -FilePath $h1z1 -WorkingDirectory $clientDir
Write-Host ""
Write-Host "=== ZEmu : H1Z1 KotK Pre-Season 3 (build 0.23.4.161178) ===" -ForegroundColor Magenta
Write-Host "Si le jeu se ferme dans la premiere seconde, relance-le (bug client connu)."
Write-Host "En jeu : F8 = console deve (/help, /give, /spawncar, /godmode, /gas...)"


