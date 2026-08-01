"""Launch only this project's Mods. Existing Mods and saves remain untouched."""
from pathlib import Path
import argparse
import json
import os
import secrets
import subprocess
from build import GAME, ROOT

p = argparse.ArgumentParser()
p.add_argument('--lab', action='store_true', help='Enable fixture commands restricted to AgentLab save')
args = p.parse_args()
mods = ROOT / 'work/Mods'
if not (mods / 'AgentBridge/AgentBridge.dll').exists():
    raise SystemExit('Run python3 scripts/build.py first')
token = secrets.token_urlsafe(32)
config = {'Port': 18765, 'Token': token, 'EnableLab': args.lab}
for path, value in [(mods / 'AgentBridge/config.json', config), (ROOT / '.local.json', {'url': 'http://127.0.0.1:18765', 'token': token})]:
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, 'w') as f:
        json.dump(value, f)
print('Launching isolated Farmtronics + AgentBridge. Use a new farmer named AgentLab for tests.', flush=True)
subprocess.run([str(GAME / 'StardewModdingAPI'), '--mods-path', str(mods)], cwd=GAME, check=True)
