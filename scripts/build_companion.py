"""Build the pinned Squad with additive adapter hooks into isolated CompanionMods."""
from pathlib import Path
import json
import shutil
import subprocess
from build import ROOT, GAME, DOTNET


def replace_once(path, old, new):
    text = path.read_text(encoding='utf-8-sig')
    if text.count(old) != 1:
        raise RuntimeError(f'Upstream anchor changed: {path.name}')
    path.write_text(text.replace(old, new))


def build():
    source = ROOT / 'external/the-stardew-squad'
    locked = json.loads((ROOT / 'configs/companion-upstreams.json').read_text())['repositories'][0]['commit']
    actual = subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD'],text=True).strip()
    if actual != locked:
        raise SystemExit('Squad revision differs from audited revision')
    stage = ROOT / 'work/build/Squad'
    stage.mkdir(parents=True,exist_ok=True)
    shutil.copytree(source / 'TheStardewSquad', stage, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns('bin','obj'))
    replace_once(stage / 'ModEntry.cs', 'public class ModEntry : Mod\n    {',
        'public class ModEntry : Mod\n    {\n        private CompanionControl? agentControl;\n        public override object GetApi() => agentControl ??= new CompanionControl(this);')
    follower = stage / 'Framework/FollowerManager.cs'
    replace_once(follower, 'private void AssignTaskToMate(ISquadMate mate, SquadTask newTask)',
        'public void AssignAgentTask(ISquadMate mate, SquadTask task) => AssignTaskToMate(mate, task);\n\n        private void AssignTaskToMate(ISquadMate mate, SquadTask newTask)')
    replace_once(follower, 'if (mate.StuckCounter > 20)\n            {',
        'if (mate.StuckCounter > 20)\n            {\n                CompanionControl.BeforeWarp(mate);')
    unified = stage / 'Framework/Tasks/UnifiedTaskManager.cs'
    replace_once(unified, '// Get the set of spots claimed by *other* NPCs\n',
        'if (CompanionControl.IsManaged(mate)) return null;\n            // Get the set of spots claimed by *other* NPCs\n')
    task = stage / 'Framework/TaskManager.cs'
    replace_once(task, 'if (Game1.random.Next(10) == 0)\n                {\n                    mate.Communicate(TaskType.Mining.ToString());',
        'CompanionControl.ObserveMine(mate, tile, rock, location);\n\n                if (Game1.random.Next(10) == 0)\n                {\n                    mate.Communicate(TaskType.Mining.ToString());')
    shutil.copyfile(ROOT / 'mod/SquadAdapter/CompanionControl.cs',stage / 'CompanionControl.cs')
    # Drop the separate upstream AfterBuild deployment target in the staging copy.
    # EnableModDeploy=false alone does not control this custom target.
    project = stage / 'TheStardewSquad.csproj'
    xml = project.read_text()
    start = xml.index('  <Target Name="DeployMods"')
    end = xml.index('</Target>',start) + len('</Target>')
    project.write_text(xml[:start] + xml[end:])
    mods = ROOT / 'work/CompanionMods'
    for path, name in [(project,'TheStardewSquad'),(ROOT/'mod/AgentBridge/AgentBridge.csproj','AgentBridge')]:
        subprocess.run([DOTNET,'build',str(path),'-c','Release','--nologo',f'-p:GamePath={GAME}',
                        '-p:EnableModDeploy=false','-p:EnableModZip=false'],check=True,cwd=ROOT)
        dest = mods / name; dest.mkdir(parents=True,exist_ok=True)
        for dll in (path.parent/'bin/Release/net6.0').glob('*.dll'):
            shutil.copyfile(dll,dest/dll.name)
        shutil.copyfile(path.parent/'manifest.json',dest/'manifest.json')
        for folder in ('assets','i18n'):
            if (path.parent/folder).exists(): shutil.copytree(path.parent/folder,dest/folder,dirs_exist_ok=True)
    shutil.copytree(GAME/'Mods/ConsoleCommands',mods/'ConsoleCommands',dirs_exist_ok=True)
    print('Built isolated companion Mods:', mods)


if __name__ == '__main__':
    build()
