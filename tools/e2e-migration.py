"""Migration de bout en bout, avec la VRAIE 0.10.10 de production, dans un bac a sable.

  0.10.10 (ancien launcher WinForms, telecharge depuis dl.cotk.fr)
    -> se met a jour seule (comportement deja deploye, impossible a changer) vers 1.0.2 (ce launcher)
    -> 1.0.2 trouve 1.0.3 et s'installe seul (aucun clic), 1.0.3 demarre.

Isolation : APPDATA/LOCALAPPDATA/reglages/journaux dans %TEMP%\\cotk-e2e, API = faux serveur sur 127.0.0.1
(aucun en-tete journalise). Aucun fichier du vrai profil n'est modifie. Seule la session Windows enregistree
est lue par les launchers (comme d'habitude) et n'est envoyee qu'a 127.0.0.1.

Prerequis : %TEMP%\\cotk-e2e-pkgs\\pkg-1.0.2.zip et pkg-1.0.3.zip (voir README, section migration).
"""
import hashlib, json, os, shutil, subprocess, sys, tempfile, threading, time, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

TEMP = Path(tempfile.gettempdir())
SANDBOX = TEMP / 'cotk-e2e'
PKGS = TEMP / 'cotk-e2e-pkgs'
OLD_ZIP = TEMP / 'cotk-launcher-0.10.10.zip'
OLD_URL = 'https://dl.cotk.fr/cotk-launcher-0.10.10.zip'
FAILS = []


def check(ok, message):
    print(('PASS ' if ok else 'FAIL ') + message, flush=True)
    if not ok:
        FAILS.append(message)
    return ok


def ps(command, timeout=60):
    return subprocess.run(['powershell', '-NoProfile', '-Command', command], capture_output=True, text=True, timeout=timeout).stdout.strip()


CRED_CODE = r'''
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class E2eCred {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  struct C { public uint Flags; public uint Type; public string TargetName; public string Comment; public long LastWritten; public uint BlobSize; public IntPtr Blob; public uint Persist; public uint AttributeCount; public IntPtr Attributes; public string TargetAlias; public string UserName; }
  [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CredWrite(ref C c, uint flags);
  [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CredRead(string target, uint type, uint flags, out IntPtr c);
  [DllImport("advapi32.dll", SetLastError=true)] static extern void CredFree(IntPtr c);
  [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CredDelete(string target, uint type, uint flags);
  public static bool Exists() { IntPtr p; if (!CredRead("COTK.Launcher.Session", 1, 0, out p)) return false; CredFree(p); return true; }
  public static string Fingerprint() {
    IntPtr p; if (!CredRead("COTK.Launcher.Session", 1, 0, out p)) return "";
    try {
      var c = (C)Marshal.PtrToStructure(p, typeof(C)); var b = new byte[c.BlobSize]; Marshal.Copy(c.Blob, b, 0, b.Length);
      try { using (var sha = System.Security.Cryptography.SHA256.Create()) { return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", ""); } }
      finally { Array.Clear(b, 0, b.Length); }
    } finally { CredFree(p); }
  }
  public static void Write(string token) {
    var b = Encoding.UTF8.GetBytes(token); var p = Marshal.AllocCoTaskMem(b.Length); Marshal.Copy(b, 0, p, b.Length);
    var c = new C { Type = 1, TargetName = "COTK.Launcher.Session", BlobSize = (uint)b.Length, Blob = p, Persist = 2, UserName = "COTK Launcher" };
    try { if (!CredWrite(ref c, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); } finally { Marshal.FreeCoTaskMem(p); }
  }
  public static void Delete() { CredDelete("COTK.Launcher.Session", 1, 0); }
}
"@
'''


def cred(action):
    script = TEMP / 'cotk-e2e-cred.ps1'
    body = {'exists': '[E2eCred]::Exists()', 'fingerprint': '[E2eCred]::Fingerprint()', 'write': "[E2eCred]::Write('e2e-fake-token')", 'delete': '[E2eCred]::Delete()'}[action]
    script.write_text(CRED_CODE + '\n' + body + '\n', encoding='utf-8')
    return subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(script)], capture_output=True, text=True).stdout.strip()


def launchers():
    out = ps("Get-Process -Name COTK.Launcher,COTK.Launcher.Next -ErrorAction SilentlyContinue | ForEach-Object { try { '{0}|{1}|{2}|{3}' -f $_.Id,$_.Path,$_.MainModule.FileVersionInfo.FileVersion,$_.MainWindowTitle } catch {} }")
    rows = []
    for line in out.splitlines():
        parts = line.split('|')
        if len(parts) >= 3:
            rows.append({'pid': int(parts[0]), 'path': parts[1], 'version': parts[2], 'title': parts[3] if len(parts) > 3 else ''})
    return rows


def updater_dirs():
    return {p.name for p in TEMP.glob('COTK.Launcher.Updater-*')}


# ---------------------------------------------------------------- faux serveur API
class State:
    launcher = ('1.0.2', 'pkg-1.0.2.zip')
    chain = True  # True : au 2e appel /me le manifeste annonce la 1.0.3 (banc de migration). Faux : le manifeste ne change pas.
    real_zip = None  # (url, sha256, taille) : le vrai paquet servi par dl.cotk.fr (--real-zip)
    seen = []
    base = ''


def package(name):
    data = (PKGS / name).read_bytes()
    return data, hashlib.sha256(data).hexdigest()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):  # jamais d'en-tete ni de jeton dans un journal
        pass

    def reply(self, code, body, ctype='application/json'):
        raw = body if isinstance(body, bytes) else json.dumps(body).encode()
        self.send_response(code)
        self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_GET(self):
        path = self.path.split('?')[0]
        State.seen.append(path)
        if path == '/healthz':
            return self.reply(200, b'ok', 'text/plain')
        if path == '/api/v1/launcher/me':
            # 1er appel = l'ancien launcher, 2e appel = le launcher migre (1.0.2) : il doit voir la 1.0.3.
            if State.chain and State.seen.count('/api/v1/launcher/me') == 2:
                State.launcher = ('1.0.3', 'pkg-1.0.3.zip')
            return self.reply(200, {'id': 'e2e', 'username': 'e2e-user', 'role': 'user', 'expiresAt': '2030-01-01T00:00:00Z'})
        if path == '/api/v1/launcher/updates':
            version, name = State.launcher
            if name == 'pkg-1.0.2.zip' and State.real_zip:
                url, sha, size = State.real_zip
            else:
                data, sha = package(name)
                url, size = f'{State.base}/{name}', len(data)
            return self.reply(200, {
                'schemaVersion': 1,
                'launcher': {'version': version, 'downloadUrl': url, 'sha256': sha, 'sizeBytes': size, 'required': False},
                'client': {'version': '0.23.4.161181', 'archiveUrl': f'{State.base}/client.zip', 'sha256': '0' * 63 + '1', 'sizeBytes': 1, 'executable': 'H1Z1.exe', 'required': True},
            })
        if path.startswith('/pkg-') and path.endswith('.zip'):
            name = path[1:]
            data, _ = package(name)
            return self.reply(200, data, 'application/zip')
        self.reply(404, {'error': 'not found'})

    def do_POST(self):
        State.seen.append(self.path.split('?')[0])
        self.reply(404, {'error': 'not found'})


def wait_for(predicate, timeout, interval=1.0):
    end = time.time() + timeout
    while time.time() < end:
        value = predicate()
        if value:
            return value
        time.sleep(interval)
    return None


def click_modal_button(pid, fx=0.6116, fy=0.6657):
    """Clique le bouton principal de la modale (UIA n'expose pas cette modale) : position relative a la fenetre,
    mesuree sur une vraie capture. Le curseur de l'utilisateur est restaure ensuite."""
    script = r"""
Add-Type -TypeDefinition "using System;using System.Runtime.InteropServices;public static class M{[DllImport(""user32.dll"")]public static extern bool GetWindowRect(IntPtr h,out R r);[DllImport(""user32.dll"")]public static extern bool SetForegroundWindow(IntPtr h);[DllImport(""user32.dll"")]public static extern bool ShowWindow(IntPtr h,int c);[DllImport(""user32.dll"")]public static extern bool SetCursorPos(int x,int y);[DllImport(""user32.dll"")]public static extern bool GetCursorPos(out P p);[DllImport(""user32.dll"")]public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);[DllImport(""user32.dll"")]public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);[StructLayout(LayoutKind.Sequential)]public struct R{public int L,T,Rt,B;}[StructLayout(LayoutKind.Sequential)]public struct P{public int X,Y;}}"
$h = (Get-Process -Id PIDHERE).MainWindowHandle
$r = New-Object M+R; [void][M]::GetWindowRect($h,[ref]$r)
$old = New-Object M+P; [void][M]::GetCursorPos([ref]$old)
[M]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [M]::keybd_event(0x12,0,2,[UIntPtr]::Zero)
[void][M]::ShowWindow($h,9); [void][M]::SetForegroundWindow($h); Start-Sleep -Milliseconds 700
$x = [int]($r.L + FXHERE * ($r.Rt - $r.L)); $y = [int]($r.T + FYHERE * ($r.B - $r.T))
[void][M]::SetCursorPos($x,$y); Start-Sleep -Milliseconds 300
[M]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [M]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 300; [void][M]::SetCursorPos($old.X,$old.Y)
'clic en ' + $x + ',' + $y
"""
    return ps(script.replace('PIDHERE', str(pid)).replace('FXHERE', str(fx)).replace('FYHERE', str(fy)))

def main():
    keep = '--keep' in sys.argv
    PKGS.mkdir(exist_ok=True)
    for name in ('pkg-1.0.2.zip', 'pkg-1.0.3.zip'):
        if not (PKGS / name).exists():
            sys.exit(f'Paquet de test manquant : {PKGS / name}')

    if '--real-zip' in sys.argv:
        url = 'https://dl.cotk.fr/launcher-next/COTK-Launcher-Next-1.0.2-win-x64.zip'
        data = urllib.request.urlopen(url, timeout=120).read()
        State.real_zip = (url, hashlib.sha256(data).hexdigest(), len(data))
        print(f'Paquet REEL servi par dl.cotk.fr : {len(data)} octets, sha256 {State.real_zip[1][:16]}...', flush=True)
        (PKGS / 'real-1.0.2.zip').write_bytes(data)

    # 0. Vrai launcher de production
    if not OLD_ZIP.exists():
        print('Telechargement de la 0.10.10 de production...', flush=True)
        urllib.request.urlretrieve(OLD_URL, OLD_ZIP)

    # Jamais de toucher a un vrai jeton : si une session est deja enregistree, on s'arrete.
    if cred('exists') == 'True':
        sys.exit('Une session COTK est enregistree dans le Gestionnaire d\'identification : deconnectez-vous du launcher avant ce test (aucun vrai jeton n\'est touche).')
    cred('write')
    try:
        run_e2e(keep)
    finally:
        cred('delete')
        print('Jeton factice retire du Gestionnaire d\'identification :', 'oui' if cred('exists') != 'True' else 'NON !', flush=True)


def run_e2e(keep):
    # 1. Aucun launcher ne doit tourner (mutex commun) ; le vrai launcher de l'utilisateur est ferme proprement.
    running = launchers()
    for row in running:
        print(f"Fermeture du launcher en cours (PID {row['pid']}, {row['version']}) pour liberer le verrou commun", flush=True)
        ps(f"$p = Get-Process -Id {row['pid']} -ErrorAction SilentlyContinue; if ($p) {{ [void]$p.CloseMainWindow(); if (-not $p.WaitForExit(8000)) {{ Stop-Process -Id $p.Id -Force }} }}")
    time.sleep(1)

    # 2. Bac a sable : installation "comme l'installateur" dans %LOCALAPPDATA%\COTK + donnees du joueur
    shutil.rmtree(SANDBOX, ignore_errors=True)
    install = SANDBOX / 'local' / 'COTK'
    install.mkdir(parents=True)
    subprocess.run(['powershell', '-NoProfile', '-Command', f'Expand-Archive -LiteralPath "{OLD_ZIP}" -DestinationPath "{install}" -Force'], check=True)
    (install / 'client').mkdir()
    (install / 'client' / 'H1Z1.exe').write_text('player-game')
    (install / 'logs').mkdir(exist_ok=True)
    (install / 'logs' / 'keep.log').write_text('player-log')
    (install / 'ToggleConsole').mkdir()
    (install / 'ToggleConsole' / 'ToggleConsole.exe').write_text('staff-console')
    exe = install / 'COTK.Launcher.exe'
    old_version = ps(f"(Get-Item '{exe}').VersionInfo.FileVersion")
    check(old_version.startswith('0.10.10'), f'ancien launcher de production en place (version {old_version})')

    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    State.base = f'http://127.0.0.1:{server.server_port}'
    threading.Thread(target=server.serve_forever, daemon=True).start()

    env = dict(os.environ)
    env.update({
        'LOCALAPPDATA': str(SANDBOX / 'local'), 'APPDATA': str(SANDBOX / 'roaming'),
        'COTK_SETTINGS_DIR': str(SANDBOX / 'settings'), 'COTK_LOG_DIR': str(SANDBOX / 'logs'),
        'COTK_API_URL': State.base,
    })
    before_dirs = updater_dirs()

    # 3. L'ancien launcher demarre, restaure la session, voit 1.0.2 et migre SEUL (aucune confirmation)
    print('Demarrage de la 0.10.10...', flush=True)
    subprocess.Popen([str(exe)], cwd=str(install), env=env)

    def migrated(version):
        for row in launchers():
            if row['path'].lower() == str(exe).lower() and row['version'].startswith(version):
                return row
        return None

    row = wait_for(lambda: migrated('1.0.2'), 150)
    check(row is not None, 'ancien launcher -> 1.0.2 sans clic (mise a jour automatique deja deployee)')
    if row is None:  # diagnostic
        print('  requetes vues par le faux serveur :', sorted(set(State.seen)))
        print('  launchers en cours :', launchers())
        print('  fenetres :', ps("Get-Process | Where-Object { $_.MainWindowTitle } | Where-Object { $_.ProcessName -match 'COTK|H1Z1' } | ForEach-Object { '{0} [{1}]' -f $_.MainWindowTitle,$_.ProcessName }"))
        for p in list((SANDBOX / 'logs').rglob('*')) + list(install.rglob('*.log'))[:3]:
            if p.is_file():
                print('  journal', p, ':', p.read_text(encoding='utf-8', errors='replace')[-600:])
    check('/api/v1/launcher/updates' in State.seen and (State.real_zip is not None or '/pkg-1.0.2.zip' in State.seen), 'manifeste lu et paquet 1.0.2 telecharge' + (' depuis dl.cotk.fr (HTTPS)' if State.real_zip else ''))
    check((install / 'client' / 'H1Z1.exe').read_text() == 'player-game', 'client du joueur intact')
    check((install / 'logs' / 'keep.log').read_text() == 'player-log', 'journaux du joueur intacts')
    check((install / 'ToggleConsole' / 'ToggleConsole.exe').read_text() == 'staff-console', 'console staff intacte')
    check(wait_for(lambda: updater_dirs() <= before_dirs, 30) is not None or updater_dirs() <= before_dirs, 'dossier temporaire de l\'installateur supprime (1re migration)')

    # 4. Le launcher 1.0.2 trouve 1.0.3 et s'installe SEUL : aucune fenetre, aucun clic.
    if row:
        print('Attente de la mise a jour automatique 1.0.3 (aucun clic)...', flush=True)
        titled = wait_for(lambda: (migrated('1.0.2') or {}).get('title') == 'COTK - Crown of the King', 40)
        check(bool(titled), 'fenetre du launcher 1.0.2 affichee (COTK - Crown of the King)')
        clicked = True
        if clicked:
            row2 = wait_for(lambda: migrated('1.0.3'), 150)
            check(row2 is not None, '1.0.2 -> 1.0.3 AUTOMATIQUE, sans aucun clic (telechargement, remplacement, relance)')
            check('/pkg-1.0.3.zip' in State.seen, 'paquet 1.0.3 telecharge')
            check((install / 'client' / 'H1Z1.exe').read_text() == 'player-game', 'client intact apres la 2e mise a jour')
            check((install / 'logs' / 'keep.log').read_text() == 'player-log', 'journaux intacts apres la 2e mise a jour')
            check((install / 'ToggleConsole' / 'ToggleConsole.exe').read_text() == 'staff-console', 'console staff intacte apres la 2e mise a jour')
            check(wait_for(lambda: updater_dirs() <= before_dirs, 40) is not None or updater_dirs() <= before_dirs, 'dossier temporaire de l\'installateur supprime (2e mise a jour)')
            check(not any(p.name.startswith('COTK.update-') or '.update-' in p.name or '.rollback-' in p.name for p in (SANDBOX / 'local').iterdir()), 'aucun dossier d\'etape ni de sauvegarde laisse')
            leftovers = [launchers()]
            check(len([r for r in launchers() if r['path'].lower() == str(exe).lower()]) == 1, 'un seul launcher en cours')
        # journal du launcher
        logs = list((SANDBOX / 'logs').glob('*.log'))
        text = '\n'.join(p.read_text(encoding='utf-8', errors='replace') for p in logs)
        check('1.0.2 d' in text and 'mise à jour du launcher disponible' in text, 'journal : 1.0.2 a vu la mise a jour disponible')

    # 5. Nettoyage (sauf --keep)
    for r in launchers():
        if r['path'].lower() == str(exe).lower():
            ps(f"Stop-Process -Id {r['pid']} -Force -ErrorAction SilentlyContinue")
    server.shutdown()
    time.sleep(1)
    if not keep:
        shutil.rmtree(SANDBOX, ignore_errors=True)
    print()
    print('RESULTAT :', 'TOUT EST VERT' if not FAILS else f'{len(FAILS)} ECHEC(S)')
    for f in FAILS:
        print('  -', f)
    sys.exit(1 if FAILS else 0)


if __name__ == '__main__':
    main()
