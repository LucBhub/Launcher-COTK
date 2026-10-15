"""GitHub push -> the isolated Windows publisher. The signing key stays in the current user's CNG store.

Private deployment helpers are supplied by COTK_PUBLISHER_HOME, not by GitHub secrets.
No user development directory is overwritten. Jobs only run for pushes to the trusted release branch.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import urllib.request

REPO = 'LucBhub/Launcher-COTK'
BRANCH = 'launcher-next'
PRIVATE_FILES = ('tools/release.py', 'tools/publish-launcher-migration.py',
                 'tools/publish-signed-manifest.py', 'tests/test_release.py')
VERSION_FILES = ('COTK.Launcher.Next.csproj', 'src/AssemblyInfo.cs', 'docs/download-faq.md')


def version(value):
    if not re.fullmatch(r'\d+\.\d+\.\d+', value):
        raise ValueError('Expected a version x.y.z.')
    parts = tuple(map(int, value.split('.')))
    if any(part > 65534 for part in parts):
        raise ValueError('Version exceeds .NET assembly limits.')
    return parts


def next_version(declared, published, reserved=()):
    """Honour an explicit higher version, otherwise allocate the next unused patch version."""
    current = max([version(published), *(version(v) for v in reserved)])
    if version(declared) > current:
        return declared
    parts = list(current)
    for index in (2, 1, 0):
        if parts[index] < 65534:
            parts[index] += 1
            parts[index + 1:] = [0] * (2 - index)
            return '.'.join(map(str, parts))
    raise ValueError('No further .NET assembly version available.')


def trusted_event(env):
    return (env.get('GITHUB_REPOSITORY') == REPO and env.get('GITHUB_REF') == f'refs/heads/{BRANCH}'
            and env.get('GITHUB_EVENT_NAME') == 'push' and re.fullmatch(r'[0-9a-f]{40}', env.get('GITHUB_SHA', '')) is not None)


def run(command, cwd, capture=False, check=True):
    result = subprocess.run(command, cwd=cwd, capture_output=capture, text=True, encoding='utf-8', errors='replace')
    if result.returncode and check:
        if capture:
            print(result.stdout, result.stderr)
        raise RuntimeError(f'Command failed with exit code {result.returncode}: {command[0]}')
    return result


def remote_head(workspace):
    return run(['git', 'ls-remote', 'origin', f'refs/heads/{BRANCH}'], workspace, True).stdout.split()[0]


def production():
    with urllib.request.urlopen('https://api.cotk.fr/api/v1/launcher/updates', timeout=30) as response:
        return json.load(response)


def copy_checkout(workspace, source):
    # Do not use "publish*": it also matches tools/publisher.ps1, which is required for signing.
    shutil.copytree(workspace, source, ignore=shutil.ignore_patterns('.git', '__pycache__', 'out', 'bin', 'obj', 'release'))


def write_result(state, artifact_dir):
    artifact_dir.mkdir(parents=True, exist_ok=True)
    (artifact_dir / 'result.json').write_text(json.dumps(state, indent=2), encoding='utf-8')
    summary = os.environ.get('GITHUB_STEP_SUMMARY')
    if summary:
        with open(summary, 'a', encoding='utf-8') as report:
            report.write(f"## Launcher Next\n\nStatus: **{state['status']}**\n\nVersion: `{state.get('version', '-')}`\n\nSource: `{state.get('commit', '-')}`\n")


def finish_git(workspace, source, sha, release_version):
    """Commit only version metadata, as a fast-forward. Never overlay a newer developer push."""
    if remote_head(workspace) != sha:
        print('Production is updated; a newer push exists. Its sources will not be overwritten.')
        return
    for relative in VERSION_FILES:
        shutil.copy2(source / relative, workspace / relative)
    git = ['git', '-c', 'user.name=github-actions[bot]',
           '-c', 'user.email=41898282+github-actions[bot]@users.noreply.github.com', '-c', 'core.autocrlf=false']
    run(['git', 'status', '--short'], workspace)
    run(['git', 'diff', '--', *VERSION_FILES], workspace)
    run(['git', 'log', '--oneline', '-10'], workspace)
    run(git + ['add', '--', *VERSION_FILES], workspace)
    run(['git', 'diff', '--cached', '--check'], workspace)
    changed = run(['git', 'diff', '--cached', '--quiet'], workspace, check=False).returncode
    if changed == 1:
        # GitHub's skip directive avoids a self-triggering release loop.
        run(git + ['commit', '-m', f'release(launcher-next): {release_version} [skip ci]'], workspace)
    elif changed != 0:
        raise RuntimeError('Cannot inspect staged version metadata.')
    tag = f'next-v{release_version}'
    if run(['git', 'ls-remote', 'origin', f'refs/tags/{tag}'], workspace, True).stdout.strip():
        raise RuntimeError(f'Tag already exists: {tag}; not replaced.')
    run(git + ['tag', tag], workspace)
    run(['git', 'push', '--atomic', 'origin', f'HEAD:refs/heads/{BRANCH}', f'refs/tags/{tag}'], workspace)


def main():
    if not trusted_event(os.environ):
        raise RuntimeError('Publisher accepts only pushes to the trusted release branch.')
    os.environ['GIT_TERMINAL_PROMPT'] = '0'
    os.environ['GCM_INTERACTIVE'] = 'Never'
    os.environ['COTK_CI_KEEP_SESSION'] = '1'
    workspace = Path(os.environ['GITHUB_WORKSPACE']).resolve()
    home = Path(os.environ['COTK_PUBLISHER_HOME']).resolve()
    sha = os.environ['GITHUB_SHA']
    if run(['git', 'rev-parse', 'HEAD'], workspace, True).stdout.strip() != sha:
        raise RuntimeError('Checkout does not match the triggering commit.')
    if remote_head(workspace) != sha:
        print('Obsolete queued push: a newer commit is already present, publication skipped.')
        return

    state_root = Path(os.environ['LOCALAPPDATA']) / 'COTK-ReleaseRunner' / 'releases'
    job_root = state_root / sha
    source = job_root / 'source'
    artifact_dir = Path(os.environ['RUNNER_TEMP']) / 'cotk-release-result'
    shutil.rmtree(artifact_dir, ignore_errors=True)
    if not source.exists():
        source.parent.mkdir(parents=True, exist_ok=True)
        copy_checkout(workspace, source)
    for relative in PRIVATE_FILES:
        destination = source / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(home / relative, destination)
    for relative in ('tools/publisher.ps1', 'tools/ReleasePublisher.cs', *PRIVATE_FILES):
        if not (source / relative).is_file():
            raise RuntimeError(f'Required publisher file missing: {relative}')

    state_file = job_root / 'state.json'
    state = json.loads(state_file.read_text(encoding='utf-8')) if state_file.exists() else {'commit': sha, 'status': 'preparing'}
    live = production()['launcher']
    if 'version' not in state:
        declared = re.search(r'<Version>([^<]+)</Version>', (source / 'COTK.Launcher.Next.csproj').read_text(encoding='utf-8-sig')).group(1)
        reserved = [json.loads(p.read_text(encoding='utf-8'))['version'] for p in state_root.glob('*/state.json') if p != state_file]
        state['version'] = next_version(declared, live['version'], reserved)
        state_file.write_text(json.dumps(state, indent=2), encoding='utf-8')
    release_version = state['version']
    archive = source / 'release' / f'COTK-Launcher-Next-{release_version}-win-x64.zip'

    # Ensure local developer/testing environment overrides never reach a production CI run.
    for name in ('COTK_API_URL', 'COTK_SETTINGS_DIR', 'COTK_LOG_DIR', 'COTK_GAME_SERVER', 'COTK_PREVIEW'):
        os.environ.pop(name, None)
    print(f'Commit {sha} -> Launcher {release_version}', flush=True)
    try:
        already_live = live['version'] == release_version
        if already_live:
            if not archive.exists() or hashlib.sha256(archive.read_bytes()).hexdigest() != live['sha256']:
                raise RuntimeError('This version is live with different content; refusing another publication.')
            print('This commit was already published. Resuming Git metadata only.')
        else:
            if version(live['version']) >= version(release_version):
                raise RuntimeError('A newer version is already in production.')
            # A failed test may leave an unvalidated local zip; only job-owned files with no successful receipt are reset.
            if archive.exists() and not Path(str(archive) + '.prepared.json').exists():
                archive.unlink()
                Path(str(archive) + '.sha256').unlink(missing_ok=True)
            run([sys.executable, '-m', 'unittest', 'discover', '-s', 'tests', '-p', 'test*release.py'], source)
            run([sys.executable, 'tools/release.py', release_version], source)
            if remote_head(workspace) != sha:
                state['status'] = 'superseded-before-live'
                print('A newer push arrived during testing. Package staged but not announced.')
                return
            run([sys.executable, 'tools/release.py', release_version, '--go-live', '--yes', '--no-git'], source)
        state['status'] = 'live'
        state['sha256'] = hashlib.sha256(archive.read_bytes()).hexdigest()
        state_file.write_text(json.dumps(state, indent=2), encoding='utf-8')
        finish_git(workspace, source, sha, release_version)
    except BaseException:
        state['status'] = 'failed-check-production-before-retry'
        raise
    finally:
        state_file.write_text(json.dumps(state, indent=2), encoding='utf-8')
        write_result(state, artifact_dir)
        if archive.exists():
            for path in archive.parent.glob(archive.name + '*'):
                if path.name.endswith(('.zip', '.sha256', '.prepared.json')):
                    shutil.copy2(path, artifact_dir / path.name)
    for relative in ('out', 'bin', 'obj', f'publish-{release_version}'):
        shutil.rmtree(source / relative, ignore_errors=True)


if __name__ == '__main__':
    main()
