"""Launch only this project's Mods. Existing Mods and saves remain untouched."""
from pathlib import Path
import argparse
import fcntl
import json
import os
import secrets
import subprocess
import sys
import threading
from build import GAME, ROOT

p = argparse.ArgumentParser()
p.add_argument('--keep-window', action='store_true', help='Keep native stdin open after supervisor exits')
p.add_argument('--lab', action='store_true', help='Enable fixture commands restricted to AgentLab save')
p.add_argument('--companion', action='store_true', help='Use the isolated Squad companion build')
p.add_argument('--mods-dir', help='Independent runtime directory for this project')
p.add_argument('--port', type=int, default=18765)
args = p.parse_args()
mods = Path(args.mods_dir).resolve() if args.mods_dir else ROOT / ('work/CompanionMods' if args.companion else 'work/Mods')
if not 1024 <= args.port <= 65535:p.error('invalid port')
runtime_lock = None
if args.companion:
    runtime_lock = (ROOT / 'work' / ('companion-runtime.lock' if not args.mods_dir else mods.name+'-runtime.lock')).open('a')
    try:
        fcntl.flock(runtime_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        raise SystemExit('The companion runtime is already running or being built.')
if not (mods / 'AgentBridge/AgentBridge.dll').exists():
    raise SystemExit('Run python3 scripts/build.py first')
token = secrets.token_urlsafe(32)
config = {'Port': args.port, 'Token': token, 'EnableLab': args.lab, 'Backend': 'squad' if args.companion else 'farmtronics'}
for path, value in [(mods / 'AgentBridge/config.json', config), (ROOT / '.local.json', {'url': f'http://127.0.0.1:{args.port}', 'token': token})]:
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, 'w') as f:
        json.dump(value, f)
if args.companion and (mods / 'Together').exists():
    path=mods/'Together/config.json'
    settings=json.loads(path.read_text()) if path.exists() else {}
    settings.update(ApiKeyFile=str(ROOT/'.env'),EnableLab=args.lab)
    path.write_text(json.dumps(settings,ensure_ascii=False,indent=2))
print('Launching isolated ' + config['Backend'] + ' + AgentBridge. Use AgentLab for tests.', flush=True)
command=[str(GAME / 'StardewModdingAPI'), '--mods-path', str(mods)]
if args.keep_window:
    game=subprocess.Popen(command,cwd=GAME,stdin=subprocess.PIPE,text=True)
    def forward_console():
        try:
            for line in sys.stdin:
                if game.poll() is not None:return
                game.stdin.write(line);game.stdin.flush()
        except (BrokenPipeError,OSError):pass
        # Keep the owned pipe alive: upstream EOF must not terminate the game console.
    threading.Thread(target=forward_console,daemon=True).start()
    raise SystemExit(game.wait())
subprocess.run(command,cwd=GAME,check=True)
