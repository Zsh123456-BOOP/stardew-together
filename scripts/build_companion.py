"""Build the pinned Squad with additive adapter hooks into isolated CompanionMods."""
from pathlib import Path
import json
import fcntl
import os
import shutil
import subprocess
from build import ROOT, GAME, DOTNET


def replace_once(path, old, new):
    text = path.read_text(encoding='utf-8-sig')
    if text.count(old) != 1:
        raise RuntimeError(f'Upstream anchor changed: {path.name}')
    path.write_text(text.replace(old, new))


def build():
    lock_path = ROOT / 'work/companion-runtime.lock'
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    runtime_lock = lock_path.open('a')
    try:
        fcntl.flock(runtime_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        raise SystemExit('Close the isolated companion game before replacing its runtime.')
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
        'if (mate.StuckCounter > 20)\n            {\n                if (CompanionControl.BlockRecoveryWarp(mate)) return;')
    replace_once(follower, 'bool isHostBusy = !Context.IsPlayerFree || !Game1.game1.IsActive;',
        'bool isHostBusy = !Context.IsPlayerFree || (!Game1.game1.IsActive && Game1.options.pauseWhenOutOfFocus);')
    replace_once(follower, '            HandleLocationAndSpeed(mate, player);',
        '            if (CompanionControl.DriveOwned(mate, player, isFastTick, isSlowTick)) return;\n            HandleLocationAndSpeed(mate, player);')
    replace_once(follower, '                if (mate.RecruiterUniqueId != recruiter.UniqueMultiplayerID) continue;',
        '                if (CompanionControl.IsManaged(mate) || mate.RecruiterUniqueId != recruiter.UniqueMultiplayerID) continue;')
    replace_once(follower, '        private void ExecutePathMovement(ISquadMate mate)',
        '        public void DriveAgentTask(ISquadMate mate, bool slow, Farmer player) => HandleTaskExecution(mate, slow, player);\n'
        '        public void WalkAgent(ISquadMate mate, Point tile, bool slow, Farmer player) => GenerateAndFollowPath(mate, tile, slow, player);\n'
        '        private void ExecutePathMovement(ISquadMate mate)')
    replace_once(follower, '            npc.Position += velocity;',
        '            if (CompanionControl.IsManaged(mate) && ((npc.getStandingPosition() + velocity) / 64f).ToPoint() != npc.TilePoint && !AStarPathfinder.IsTilePassableForFollower(npc.currentLocation, ((npc.getStandingPosition() + velocity) / 64f).ToPoint(), npc)) { mate.Path.Clear(); mate.Halt(); return; }\n            npc.Position += velocity;')
    unified = stage / 'Framework/Tasks/UnifiedTaskManager.cs' 
    replace_once(unified, '// Get the set of spots claimed by *other* NPCs\n',
        'if (CompanionControl.IsManaged(mate)) return null;\n            // Get the set of spots claimed by *other* NPCs\n')
    replace_once(unified, 'TaskMode attackingMode = TaskPriorityManager.GetTaskMode(_config, TaskType.Attacking);',
        'TaskMode attackingMode = CompanionControl.IsManaged(mate) ? TaskMode.Autonomous : TaskPriorityManager.GetTaskMode(_config, TaskType.Attacking);')
    # Independent fishing owns its own catch cadence; player fishing cannot reward or stop it.
    text=follower.read_text()
    text=text.replace('if (mate.RecruiterUniqueId != who.UniqueMultiplayerID)',
        'if (CompanionControl.IsIndependentFishing(mate) || mate.RecruiterUniqueId != who.UniqueMultiplayerID)')
    text=text.replace('if (mate.HasTask() && IsMimickingTask(mate.Task.Type)',
        'if (!CompanionControl.IsIndependentFishing(mate) && mate.HasTask() && IsMimickingTask(mate.Task.Type)')
    follower.write_text(text)
    behavior=stage/'Framework/Behaviors/NpcTaskBehavior.cs'
    replace_once(behavior,'public bool ExecuteTask(ISquadMate mate)',
        'public bool ExecuteTask(ISquadMate mate) { if (CompanionControl.WaitForImpact(mate)) return false; bool pending = CompanionControl.BeforeTask(mate); bool result = ExecuteTaskCore(mate); CompanionControl.AfterTask(mate, pending); return result; }\n\n        private bool ExecuteTaskCore(ISquadMate mate)')
    task = stage / 'Framework/TaskManager.cs'
    replace_once(task, '                AnimateMining(npc);', '                if (!CompanionControl.IsManaged(mate)) AnimateMining(npc);')
    replace_once(task, '                AnimateWatering(npc);', '                if (!CompanionControl.IsManaged(mate)) AnimateWatering(npc);')
    replace_once(task, '.FirstOrDefault(a => a.TilePoint == tile);',
        '.FirstOrDefault(a => a.currentLocation == location && a.TilePoint == tile);')
    replace_once(task, 'if (Game1.random.Next(10) == 0)\n                {\n                    mate.Communicate(TaskType.Mining.ToString());',
        'CompanionControl.ObserveMine(mate, tile, rock, location);\n\n                if (Game1.random.Next(10) == 0)\n                {\n                    mate.Communicate(TaskType.Mining.ToString());')
    replace_once(task,'// Play sound and show fish icon',
        'CompanionControl.ObserveFish(mate, fishCopy);\n\n            // Play sound and show fish icon')
    for adapter in (ROOT / 'mod/SquadAdapter').glob('*.cs'):
        shutil.copyfile(adapter,stage / adapter.name)
    # Drop the separate upstream AfterBuild deployment target in the staging copy.
    # EnableModDeploy=false alone does not control this custom target.
    project = stage / 'TheStardewSquad.csproj'
    xml = project.read_text()
    start = xml.index('  <Target Name="DeployMods"')
    end = xml.index('</Target>',start) + len('</Target>')
    project.write_text(xml[:start] + xml[end:])
    mods = ROOT / 'work/CompanionMods'
    for path, name in [(project,'TheStardewSquad'),(ROOT/'mod/AgentBridge/AgentBridge.csproj','AgentBridge'),(ROOT/'mod/Together/Together.csproj','Together')]:
        subprocess.run([DOTNET,'build',str(path),'-c','Release','--nologo',f'-p:GamePath={GAME}',
                        '-p:EnableModDeploy=false','-p:EnableModZip=false'],check=True,cwd=ROOT)
        dest = mods / name; dest.mkdir(parents=True,exist_ok=True)
        for dll in (path.parent/'bin/Release/net6.0').glob('*.dll'):
            temporary = dest / (dll.name + '.next')
            shutil.copyfile(dll,temporary)
            os.replace(temporary,dest/dll.name)
        shutil.copyfile(path.parent/'manifest.json',dest/'manifest.json')
        for folder in ('assets','i18n'):
            if (path.parent/folder).exists(): shutil.copytree(path.parent/folder,dest/folder,dirs_exist_ok=True)
    shutil.copytree(GAME/'Mods/ConsoleCommands',mods/'ConsoleCommands',dirs_exist_ok=True)
    print('Built isolated companion Mods:', mods)


if __name__ == '__main__':
    build()
