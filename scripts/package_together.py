"""Prepare a local playable release, and an own-code-only distributable archive.

The local runtime uses the separately cloned Squad build. No credentials, saves,
third-party binaries or game assets enter the Together archive.
"""
from pathlib import Path
import argparse
import json
import shlex
import shutil
import zipfile
from build import GAME, ROOT


def package(destination: Path):
    source = ROOT / 'work/CompanionMods'
    destination.mkdir(parents=True, exist_ok=True)
    playable = destination / '同行体验版'
    mods = playable / 'Mods'
    for name in ('Together', 'TheStardewSquad'):
        origin = source / name
        if not (origin / 'manifest.json').exists():
            raise SystemExit('Run python3 scripts/build_companion.py first')
        target = mods / name
        target.mkdir(parents=True, exist_ok=True)
        # Deliberate allowlist: never copy test data, diagnostics, config or secrets.
        for file in [origin / 'manifest.json', *origin.glob('*.dll')]:
            shutil.copyfile(file, target / file.name)
        for folder in ('assets', 'i18n'):
            if (origin / folder).exists():
                shutil.copytree(origin / folder, target / folder, dirs_exist_ok=True)
    config = mods / 'Together/config.json'
    settings = json.loads(config.read_text()) if config.exists() else {}
    settings.update(ApiKeyFile=str(ROOT / '.env'), EnableLab=False)
    settings.setdefault('Autonomy', True)
    settings.setdefault('Model', 'deepseek-flash')
    config.write_text(json.dumps(settings, ensure_ascii=False, indent=2))
    launcher = playable / '启动同行.command'
    launcher.write_text('#!/bin/zsh\nset -e\n'
                        'MODS_DIR="${0:A:h}/Mods"\n'
                        f'cd {shlex.quote(str(GAME))}\n'
                        'exec ./StardewModdingAPI --mods-path "$MODS_DIR"\n')
    launcher.chmod(0o755)
    readme = ROOT / 'docs/同行使用指南.md'
    if readme.exists():
        shutil.copyfile(readme, playable / '先读我.md')
    credits = ROOT / 'docs/同行来源说明.md'
    if credits.exists():
        shutil.copyfile(credits, playable / '来源说明.md')
    version = json.loads((source/'Together/manifest.json').read_text())['Version']
    archive = destination / f'Together-{version}-own-code.zip'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for name in ('Together.dll', 'manifest.json'):
            z.write(source / 'Together' / name, 'Together/' + name)
        z.writestr('Together/.env.example', 'DEEPSEEK_API_KEY=put_your_key_here\n')
        for path in (readme, credits):
            if path.exists(): z.write(path, 'Together/' + path.name)
    print('Local playable folder:', playable)
    print('Own-code archive (requires separately prepared Squad adapter):', archive)
    # Ensure the local key itself can never appear in packaging by accident.
    with zipfile.ZipFile(archive) as z:
        assert not any(n.endswith('/.env') or '/data/' in n or '/diagnostics/' in n or 'TheStardewSquad' in n for n in z.namelist())


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=ROOT / 'outputs')
    package(parser.parse_args().output.resolve())
