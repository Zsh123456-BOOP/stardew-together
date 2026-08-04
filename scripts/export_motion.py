"""Encode captured game frames using their real write timestamps, without speed-up."""
from pathlib import Path
import argparse,statistics,subprocess,json
p=argparse.ArgumentParser();p.add_argument('frames',type=Path);p.add_argument('output',type=Path);args=p.parse_args()
frames=sorted(args.frames.glob('frame*.png'))
if len(frames)!=120:raise SystemExit('Expected the completed 120-frame capture')
times=[f.stat().st_mtime for f in frames];deltas=[b-a for a,b in zip(times,times[1:])]
if min(deltas)<=0:raise SystemExit('Capture contains old or non-monotonic frames')
last=statistics.median(deltas);listing=args.output.with_suffix('.ffconcat');listing.parent.mkdir(parents=True,exist_ok=True)
def quote(path):return str(path.resolve()).replace("'","'\\''")
listing.write_text('ffconcat version 1.0\n'+''.join("file '"+quote(f)+"'\nduration "+str(d)+"\n" for f,d in zip(frames,deltas+[last]))+"file '"+quote(frames[-1])+"'\n")
subprocess.run(['/opt/homebrew/bin/ffmpeg','-y','-loglevel','error','-safe','0','-i',str(listing),'-fps_mode','vfr','-c:v','libx264','-pix_fmt','yuv420p','-movflags','+faststart',str(args.output)],check=True)
args.output.with_suffix('.json').write_text(json.dumps(dict(frames=len(frames),real_seconds=times[-1]-times[0]+last,median_interval=last,method='actual PNG write timestamp intervals; sampled capture, not a game FPS benchmark'),indent=2))
listing.unlink();print(args.output)
