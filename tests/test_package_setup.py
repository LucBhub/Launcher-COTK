"""Checks de politique pour l'empaquetage setup Inno du Launcher Next ; aucune compilation ni publication."""
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / 'tools' / 'package-setup.ps1'
ISS = ROOT / 'installer' / 'cotk-next.iss'

SETUP_NAME = 'COTK-Launcher-Next-Setup-{version}.exe'


def run_ps(publish_dir, version, output_dir, check_only=True):
    command = ['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(SCRIPT),
               '-PublishDir', str(publish_dir), '-Version', version, '-OutputDir', str(output_dir)]
    if check_only:
        command.append('-CheckOnly')
    return subprocess.run(command, capture_output=True, text=True, encoding='utf-8', errors='replace')


def make_stage(root, with_launcher=True, with_runtime=True, leak=False):
    stage = Path(root) / 'stage'
    stage.mkdir(parents=True, exist_ok=True)
    if with_launcher:
        (stage / 'COTK.Launcher.exe').write_bytes(b'MZ')
    if with_runtime:
        (stage / 'coreclr.dll').write_bytes(b'runtime')
    if leak:
        (stage / 'ToggleConsole.exe').write_bytes(b'staff')
    return stage


class SetupPackagingChecks(unittest.TestCase):
    def test_plan_is_emitted_for_a_valid_stage(self):
        with tempfile.TemporaryDirectory(prefix='cotk-setup-plan-') as root:
            stage = make_stage(root)
            output = Path(root) / 'release'
            result = run_ps(stage, '9.9.9', output)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn(SETUP_NAME.format(version='9.9.9'), result.stdout)

    def test_stage_without_compat_executable_is_refused(self):
        with tempfile.TemporaryDirectory(prefix='cotk-setup-noexe-') as root:
            stage = make_stage(root, with_launcher=False)
            result = run_ps(stage, '9.9.9', Path(root) / 'release')
            self.assertNotEqual(result.returncode, 0)

    def test_stage_without_embedded_runtime_is_refused(self):
        with tempfile.TemporaryDirectory(prefix='cotk-setup-nort-') as root:
            stage = make_stage(root, with_runtime=False)
            result = run_ps(stage, '9.9.9', Path(root) / 'release')
            self.assertNotEqual(result.returncode, 0)

    def test_staff_console_leak_is_refused(self):
        with tempfile.TemporaryDirectory(prefix='cotk-setup-leak-') as root:
            stage = make_stage(root, leak=True)
            result = run_ps(stage, '9.9.9', Path(root) / 'release')
            self.assertNotEqual(result.returncode, 0)

    def test_bad_version_is_refused(self):
        with tempfile.TemporaryDirectory(prefix='cotk-setup-ver-') as root:
            stage = make_stage(root)
            result = run_ps(stage, '1.2.3-beta', Path(root) / 'release')
            self.assertNotEqual(result.returncode, 0)

    def test_published_setup_is_never_overwritten(self):
        with tempfile.TemporaryDirectory(prefix='cotk-setup-exists-') as root:
            stage = make_stage(root)
            output = Path(root) / 'release'
            output.mkdir()
            (output / SETUP_NAME.format(version='9.9.9')).write_bytes(b'dejapublie')
            result = run_ps(stage, '9.9.9', output)
            self.assertNotEqual(result.returncode, 0)


class InstallerScriptInvariants(unittest.TestCase):
    def test_appid_matches_the_historical_setup(self):
        # Meme AppId que le setup 0.10.x : mise a niveau in place pour les joueurs deja installes.
        self.assertIn('AppId={{7E2A9F41-6C3D-4B58-9A0E-52C4F10D81A2}', ISS.read_text(encoding='utf-8'))

    def test_output_name_is_immutable_and_versioned(self):
        self.assertIn('OutputBaseFilename=COTK-Launcher-Next-Setup-{#AppVersion}', ISS.read_text(encoding='utf-8'))

    def test_installs_the_compat_executable_per_user_without_admin(self):
        text = ISS.read_text(encoding='utf-8')
        self.assertIn('PrivilegesRequired=lowest', text)
        self.assertIn('DefaultDirName={localappdata}\\COTK', text)
        self.assertIn('Filename: "{app}\\COTK.Launcher.exe"', text)

    def test_uninstaller_keeps_the_canonical_location(self):
        self.assertIn('UninstallFilesDir={localappdata}\\COTK\\unins', ISS.read_text(encoding='utf-8'))

    def test_stage_is_taken_from_the_publish_define(self):
        self.assertIn('Source: "{#StageDir}\\*"', ISS.read_text(encoding='utf-8'))


if __name__ == '__main__':
    unittest.main()
