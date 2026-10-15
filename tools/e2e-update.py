"""Test de mise a jour AUTOMATIQUE d'une version publiee vers une nouvelle version (le cas de toutes les releases a venir).

  python tools/e2e-update.py --to-zip release/COTK-Launcher-Next-1.0.3-win-x64.zip --to-version 1.0.3
  (--from-zip : launcher precedent ; par defaut, celui que le manifeste de production annonce aujourd'hui)

Installe le launcher precedent dans un bac a sable (%TEMP%\\cotk-e2e), annonce la nouvelle version depuis un faux serveur
sur 127.0.0.1, puis verifie : mise a jour SANS clic, client/journaux/console staff intacts, aucun dossier temporaire laisse,
un seul launcher. Meme isolation que e2e-migration.py (jeton factice, aucun en-tete journalise).
"""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, sys, threading, time, urllib.request
from http.server import ThreadingHTTPServer
from pathlib import Path

spec = importlib.util.spec_from_file_location('e2e', Path(__file__).with_name('e2e-migration.py'))
e2e = importlib.util.module_from_spec(spec)
spec.loader.exec_module(e2e)
check, State, TEMP, SANDBOX, PKGS = e2e.check, e2e.State, e2e.TEMP, e2e.SANDBOX, e2e.PKGS
SERVER = None


def previous_release():
    """Le launcher que les joueurs ont aujourd'hui = celui du manifeste de production (verifie par SHA-256)."""
    manifest = json.loads(urllib.request.urlopen('https://api.cotk.fr/api/v1/launcher/updates', timeout=30).read())
    release = manifest['launcher']
    target = TEMP / f"cotk-previous-{release['version']}.zip"
    if not target.exists():
        print(f"Telechargement du launcher actuellement publie ({release['version']})...", flush=True)
        urllib.request.urlretrieve(release['downloadUrl'], target)
    if hashlib.sha256(target.read_bytes()).hexdigest() != release['sha256'].lower():
        target.unlink()
        sys.exit('Le zip precedent ne correspond pas a son SHA-256 publie.')
    return target, release['version']


def run(args):
    global SERVER
    previous, previous_version = (Path(args.from_zip), args.from_version) if args.from_zip else previous_release()
    PKGS.mkdir(exist_ok=True)
    shutil.copy2(args.to_zip, PKGS / 'pkg-new.zip')

    for row in e2e.launchers():
        print(f"Fermeture du launcher en cours (PID {row['pid']}) pour liberer le verrou commun", flush=True)
        e2e.ps(f"$p = Get-Process -Id {row['pid']} -ErrorAction SilentlyContinue; if ($p) {{ [void]$p.CloseMainWindow(); [void]$p.WaitForExit(8000) }}")
    if e2e.launchers():
        sys.exit('Le launcher est encore ouvert (operation ou partie en cours ?) : test annule, aucun processus utilisateur force.')
    time.sleep(1)

    shutil.rmtree(SANDBOX, ignore_errors=True)
    install = SANDBOX / 'local' / 'COTK'
    install.mkdir(parents=True)
    subprocess.run(['powershell', '-NoProfile', '-Command', f'Expand-Archive -LiteralPath "{previous}" -DestinationPath "{install}" -Force'], check=True)
    for relative, content in (('client/H1Z1.exe', 'player-game'), ('logs/keep.log', 'player-log'), ('ToggleConsole/ToggleConsole.exe', 'staff-console')):
        (install / relative).parent.mkdir(parents=True, exist_ok=True)
        (install / relative).write_text(content)
    exe = install / 'COTK.Launcher.exe'
    if not exe.exists():
        sys.exit('Le launcher precedent n\'a pas COTK.Launcher.exe (version anterieure a la 1.0.2 ?) : utiliser --from-zip avec une 1.0.2+.')
    version_now = e2e.ps(f"(Get-Item '{exe}').VersionInfo.FileVersion")
    check(version_now.startswith(previous_version), f'launcher precedent installe (version {version_now})')

    State.chain = False
    State.launcher = (args.to_version, 'pkg-new.zip')
    server = ThreadingHTTPServer(('127.0.0.1', 0), e2e.Handler)
    SERVER = server
    State.base = f'http://127.0.0.1:{server.server_port}'
    threading.Thread(target=server.serve_forever, daemon=True).start()
    env = dict(os.environ)
    env.update({'LOCALAPPDATA': str(SANDBOX / 'local'), 'APPDATA': str(SANDBOX / 'roaming'),
                'COTK_SETTINGS_DIR': str(SANDBOX / 'settings'), 'COTK_LOG_DIR': str(SANDBOX / 'logs'), 'COTK_API_URL': State.base})
    before = e2e.updater_dirs()

    print(f'Demarrage du launcher {previous_version} ; la version {args.to_version} est annoncee...', flush=True)
    subprocess.Popen([str(exe)], cwd=str(install), env=env)

    def updated():
        return [r for r in e2e.launchers() if r['path'].lower() == str(exe).lower()
                and r['version'].split('.')[:3] == args.to_version.split('.')
                and r['title'].startswith('COTK') and r['title'].endswith('Crown of the King')]

    check(e2e.wait_for(updated, 150) is not None, f'{previous_version} -> {args.to_version} automatique, sans aucun clic')
    check('/pkg-new.zip' in State.seen, 'nouveau paquet telecharge depuis le serveur')
    time.sleep(3)
    check((install / 'client' / 'H1Z1.exe').read_text() == 'player-game', 'client du joueur intact')
    check((install / 'logs' / 'keep.log').read_text() == 'player-log', 'journaux du joueur intacts')
    check((install / 'ToggleConsole' / 'ToggleConsole.exe').read_text() == 'staff-console', 'console staff intacte')
    check(e2e.wait_for(lambda: e2e.updater_dirs() <= before, 40) is not None or e2e.updater_dirs() <= before, 'dossier temporaire de l\'installateur supprime')
    check(not any('.update-' in p.name or '.rollback-' in p.name for p in (SANDBOX / 'local').iterdir()), 'aucun dossier d\'etape ni de sauvegarde laisse')
    check(len(updated()) == 1, 'un seul launcher en cours')
    e2e.ps(f"Get-Process -Name COTK.Launcher -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -eq '{exe}' }} | Stop-Process -Force -ErrorAction SilentlyContinue")
    server.shutdown()
    server.server_close()
    SERVER = None
    time.sleep(1)
    if not args.keep and not e2e.FAILS:
        shutil.rmtree(SANDBOX, ignore_errors=True)
    if e2e.FAILS:
        print('Journaux conserves :', SANDBOX / 'logs', flush=True)
        for log in (SANDBOX / 'logs').glob('*.log'):
            print(log.read_text(encoding='utf-8', errors='replace')[-4000:], flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--to-zip', required=True)
    ap.add_argument('--to-version', required=True)
    ap.add_argument('--from-zip')
    ap.add_argument('--from-version', default='1.0.2')
    ap.add_argument('--keep', action='store_true')
    args = ap.parse_args()
    initial = e2e.cred('exists')
    if initial not in ('True', 'False'):
        sys.exit('Etat du Gestionnaire d\'identification illisible : aucun jeton ne sera modifie.')
    if initial == 'True':
        sys.exit('Une session COTK est enregistree dans le Gestionnaire d\'identification : deconnectez-vous du launcher avant ce test.')
    try:
        e2e.cred('write')
        if e2e.cred('exists') != 'True':
            sys.exit('Impossible de preparer le jeton de test.')
        run(args)
    finally:
        # Meme sur exception : arreter uniquement les processus du bac a sable, jamais le vrai launcher du joueur.
        exe = SANDBOX / 'local' / 'COTK' / 'COTK.Launcher.exe'
        e2e.ps(f"Get-Process -Name COTK.Launcher -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -eq '{exe}' }} | Stop-Process -Force -ErrorAction SilentlyContinue")
        if SERVER is not None:
            SERVER.shutdown()
            SERVER.server_close()
        e2e.cred('delete')
        deleted = e2e.cred('exists') == 'False'
        check(deleted, 'jeton factice retire du Gestionnaire d\'identification')
        if not deleted:
            sys.exit('Le jeton de test n\'a pas pu etre nettoye.')
    print()
    print('RESULTAT :', 'TOUT EST VERT' if not e2e.FAILS else f'{len(e2e.FAILS)} ECHEC(S)')
    for f in e2e.FAILS:
        print('  -', f)
    sys.exit(1 if e2e.FAILS else 0)


if __name__ == '__main__':
    main()  # les assertions ci-dessus determinent le code de sortie
