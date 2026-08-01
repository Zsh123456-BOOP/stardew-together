"""Clone pinned companion sources; optionally remove the requested Squad tests.

Only external/ is written. Existing checkouts must already match the pinned
revision. Pruning refuses changed test files or unexpected directory contents.
"""
from pathlib import Path
import argparse
import json
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def git(repo, *args):
    return subprocess.check_output(['git', '-C', str(repo), *args])


def prepare_repository(spec):
    repo = ROOT / 'external' / spec['directory']
    if not repo.exists():
        repo.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', 'clone', '--depth', '1', '--no-checkout',
                        spec['url'], str(repo)], check=True)
        if git(repo, 'rev-parse', 'HEAD').decode().strip() != spec['commit']:
            subprocess.run(['git', '-C', str(repo), 'fetch', '--depth', '1',
                            'origin', spec['commit']], check=True)
        subprocess.run(['git', '-C', str(repo), 'checkout', '--detach',
                        spec['commit']], check=True)
    if repo.is_symlink() or repo.resolve().parent != (ROOT / 'external').resolve():
        raise RuntimeError(f'Unexpected checkout path: {repo}')
    if git(repo, 'rev-parse', 'HEAD').decode().strip() != spec['commit']:
        raise RuntimeError(f'{repo.name}: revision differs; existing checkout left intact')
    if git(repo, 'remote', 'get-url', 'origin').decode().strip() != spec['url']:
        raise RuntimeError(f'{repo.name}: origin differs; existing checkout left intact')
    print(f'{repo.name}: {spec["commit"]}')
    return repo


def prune_squad_tests(repo, spec):
    relative = spec['directory']
    tests = repo / relative
    solution = repo / spec['solution']
    original = git(repo, 'show', 'HEAD:' + spec['solution'])
    lines = original.decode('utf-8-sig').splitlines(keepends=True)
    kept, skip_end = [], False
    for line in lines:
        if line.startswith('Project(') and spec['project_guid'] in line:
            skip_end = True
            continue
        if skip_end:
            if line.strip() != 'EndProject':
                raise RuntimeError('Unexpected test solution block')
            skip_end = False
            continue
        if spec['project_guid'] not in line:
            kept.append(line)
    encoding = 'utf-8-sig' if original.startswith(b'\xef\xbb\xbf') else 'utf-8'
    updated = ''.join(kept).encode(encoding)
    if updated == original or spec['project_guid'] in updated.decode(encoding):
        raise RuntimeError('Expected test solution references were not found')
    if solution.is_symlink() or solution.read_bytes() not in (original, updated):
        raise RuntimeError('Solution has local changes; refusing to overwrite')

    paths = [p.decode() for p in git(repo, 'ls-tree', '-r', '--name-only', '-z',
                                    'HEAD', '--', relative).split(b'\0') if p]
    if not paths:
        raise RuntimeError('Pinned test directory is missing from upstream')
    if tests.is_symlink():
        raise RuntimeError('Test directory is a symlink; refusing deletion')
    if tests.exists():
        actual = {str(p.relative_to(repo)) for p in tests.rglob('*') if p.is_file()}
        if actual != set(paths) or any(p.is_symlink() for p in tests.rglob('*')):
            raise RuntimeError('Test directory has additions or deletions; left intact')
        sources = []
        for name in paths:
            content = (repo / name).read_bytes()
            if content != git(repo, 'show', 'HEAD:' + name):
                raise RuntimeError(f'{name}: local changes; refusing deletion')
            if name.endswith('.cs'):
                sources.append(content.decode('utf-8-sig').splitlines())
        counts = (len(sources), sum(len(s) for s in sources),
                  sum(bool(line.strip()) for s in sources for line in s))
        expected = (spec['csharp_files'], spec['physical_lines'], spec['nonblank_lines'])
        if counts != expected:
            raise RuntimeError(f'Test source count changed: {counts}')
        shutil.rmtree(tests)
        print(f'Removed Squad tests: {counts[0]} C# files, {counts[2]} nonblank lines')
    else:
        print('Squad test directory already removed')
    if solution.read_bytes() != updated:
        solution.write_bytes(updated)
    print('Squad solution contains only the runtime project')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--remove-squad-tests', action='store_true',
                        help='Remove the pinned upstream test directory and solution entries')
    args = parser.parse_args()
    lock = json.loads((ROOT / 'configs/companion-upstreams.json').read_text())
    repositories = {s['directory']: prepare_repository(s) for s in lock['repositories']}
    if args.remove_squad_tests:
        prune_squad_tests(repositories['the-stardew-squad'], lock['squad_test_pruning'])


if __name__ == '__main__':
    main()
