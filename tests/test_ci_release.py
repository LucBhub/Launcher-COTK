"""Pure policy/Git-metadata checks for the push publisher; no publication or token is used."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import sys

spec = importlib.util.spec_from_file_location('ci_release', Path(__file__).parents[1] / 'tools' / 'ci-release.py')
ci = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)
update_spec = importlib.util.spec_from_file_location('e2e_update_policy', Path(__file__).parents[1] / 'tools' / 'e2e-update.py')
update = importlib.util.module_from_spec(update_spec)
update_spec.loader.exec_module(update)


class PushPublisherChecks(unittest.TestCase):
    def test_publisher_files_survive_build_artifact_exclusions(self):
        with tempfile.TemporaryDirectory(prefix='cotk-ci-copy-') as root:
            workspace, source = Path(root) / 'checkout', Path(root) / 'source'
            (workspace / 'tools').mkdir(parents=True)
            (workspace / 'out').mkdir()
            (workspace / 'tools' / 'publisher.ps1').write_text('publisher', encoding='utf-8')
            (workspace / 'tools' / 'ReleasePublisher.cs').write_text('publisher code', encoding='utf-8')
            (workspace / 'out' / 'local.dll').write_text('not a source', encoding='utf-8')
            ci.copy_checkout(workspace, source)
            self.assertTrue((source / 'tools' / 'publisher.ps1').exists())
            self.assertTrue((source / 'tools' / 'ReleasePublisher.cs').exists())
            self.assertFalse((source / 'out').exists())

    def test_existing_player_session_is_never_written_or_deleted(self):
        update.e2e.FAILS.clear()
        args = ['e2e-update.py', '--to-zip', 'fake.zip', '--to-version', '1.0.4', '--keep-existing-session']
        with patch.object(sys, 'argv', args), patch.object(update, 'run') as execute, \
             patch.object(update.e2e, 'ps'), patch.object(update.e2e, 'cred', side_effect=lambda action: 'True' if action == 'exists' else 'A' * 64) as credentials:
            with self.assertRaises(SystemExit) as result:
                update.main()
        self.assertEqual(result.exception.code, 0)
        execute.assert_called_once()
        self.assertTrue(all(call.args[0] in ('exists', 'fingerprint') for call in credentials.call_args_list))

    def test_session_is_untouched_even_if_update_test_throws(self):
        update.e2e.FAILS.clear()
        args = ['e2e-update.py', '--to-zip', 'fake.zip', '--to-version', '1.0.4', '--keep-existing-session']
        with patch.object(sys, 'argv', args), patch.object(update, 'run', side_effect=RuntimeError('test stopped')), \
             patch.object(update.e2e, 'ps'), patch.object(update.e2e, 'cred', side_effect=lambda action: 'True' if action == 'exists' else 'A' * 64) as credentials:
            with self.assertRaises(RuntimeError):
                update.main()
        self.assertTrue(all(call.args[0] in ('exists', 'fingerprint') for call in credentials.call_args_list))

    def test_unreadable_session_prevents_running_the_update_test(self):
        args = ['e2e-update.py', '--to-zip', 'fake.zip', '--to-version', '1.0.4', '--keep-existing-session']
        with patch.object(sys, 'argv', args), patch.object(update, 'run') as execute, \
             patch.object(update.e2e, 'cred', side_effect=lambda action: 'True' if action == 'exists' else '') as credentials:
            with self.assertRaises(SystemExit):
                update.main()
        execute.assert_not_called()
        self.assertTrue(all(call.args[0] in ('exists', 'fingerprint') for call in credentials.call_args_list))

    def test_patch_is_allocated_automatically(self):
        self.assertEqual(ci.next_version('1.0.2', '1.0.2'), '1.0.3')
        self.assertEqual(ci.next_version('1.0.1', '1.0.7'), '1.0.8')

    def test_explicit_new_minor_is_preserved(self):
        self.assertEqual(ci.next_version('1.1.0', '1.0.2'), '1.1.0')

    def test_unannounced_packages_are_not_overwritten(self):
        self.assertEqual(ci.next_version('1.0.2', '1.0.2', ['1.0.3', '1.0.4']), '1.0.5')

    def test_version_limits(self):
        self.assertEqual(ci.next_version('1.0.1', '1.0.65534'), '1.1.0')
        with self.assertRaises(ValueError):
            ci.next_version('1.0.0', '65534.65534.65534')
        for value in ('1.2', '1.2.3-beta', '1.2.99999', '../bad'):
            with self.assertRaises(ValueError):
                ci.version(value)

    def test_only_trusted_push_is_accepted(self):
        good = {'GITHUB_REPOSITORY': ci.REPO, 'GITHUB_REF': 'refs/heads/launcher-next',
                'GITHUB_EVENT_NAME': 'push', 'GITHUB_SHA': 'a' * 40}
        self.assertTrue(ci.trusted_event(good))
        for key, value in (('GITHUB_REPOSITORY', 'fork/Launcher-COTK'), ('GITHUB_REF', 'refs/heads/main'),
                           ('GITHUB_EVENT_NAME', 'pull_request'), ('GITHUB_SHA', 'not-a-commit')):
            self.assertFalse(ci.trusted_event({**good, key: value}))

    def test_newer_developer_push_is_not_overwritten(self):
        with patch.object(ci, 'remote_head', return_value='newer'), patch.object(ci, 'run') as git:
            ci.finish_git(Path('workspace'), Path('source'), 'old', '1.0.3')
        git.assert_not_called()

    def test_metadata_commit_is_atomic_and_does_not_retrigger(self):
        with tempfile.TemporaryDirectory(prefix='cotk-ci-policy-') as root:
            workspace, source = Path(root) / 'checkout', Path(root) / 'source'
            for relative in ci.VERSION_FILES:
                for parent in (workspace, source):
                    (parent / relative).parent.mkdir(parents=True, exist_ok=True)
                    (parent / relative).write_text('version metadata', encoding='utf-8')

            def invoke(command, *args, **kwargs):
                changed = command[:4] == ['git', 'diff', '--cached', '--quiet']
                return subprocess.CompletedProcess(command, 1 if changed else 0, '', '')

            with patch.object(ci, 'remote_head', return_value='expected'), patch.object(ci, 'run', side_effect=invoke) as git:
                ci.finish_git(workspace, source, 'expected', '1.0.3')
            commands = [call.args[0] for call in git.call_args_list]
            commit = next(command for command in commands if 'commit' in command)
            self.assertIn('[skip ci]', commit[-1])
            push = next(command for command in commands if 'push' in command)
            self.assertIn('--atomic', push)
            self.assertNotIn('--force', push)
            staged = next(command for command in commands if 'add' in command)
            self.assertEqual(staged[-3:], list(ci.VERSION_FILES))

    def test_existing_tag_is_not_replaced(self):
        with tempfile.TemporaryDirectory(prefix='cotk-ci-policy-') as root:
            workspace, source = Path(root) / 'checkout', Path(root) / 'source'
            for relative in ci.VERSION_FILES:
                for parent in (workspace, source):
                    (parent / relative).parent.mkdir(parents=True, exist_ok=True)
                    (parent / relative).write_text('version metadata', encoding='utf-8')

            def invoke(command, *args, **kwargs):
                return subprocess.CompletedProcess(command, 0, 'existing' if 'ls-remote' in command else '', '')

            with patch.object(ci, 'remote_head', return_value='expected'), patch.object(ci, 'run', side_effect=invoke) as git:
                with self.assertRaises(RuntimeError):
                    ci.finish_git(workspace, source, 'expected', '1.0.3')
            self.assertFalse(any('push' in call.args[0] for call in git.call_args_list))


if __name__ == '__main__':
    unittest.main()
