"""Build pinned Farmtronics with a small control API, plus our SMAPI bridge."""
from pathlib import Path
import json
import os
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
GAME = Path(os.environ.get("STARDEW_GAME_PATH", str(Path.home() / "Library/Application Support/Steam/steamapps/common/Stardew Valley/Contents/MacOS")))
DOTNET = os.environ.get("DOTNET") or shutil.which("dotnet") or str(ROOT / "work/dotnet/dotnet")

def build():
    if not (GAME / "StardewModdingAPI.dll").exists():
        raise SystemExit("Set STARDEW_GAME_PATH to the SMAPI game directory")
    source = ROOT / "vendor/Farmtronics/Farmtronics"
    stage = ROOT / "work/build/Farmtronics"
    stage.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source, stage, dirs_exist_ok=True, ignore=shutil.ignore_patterns("bin", "obj"))
    entry = stage / "ModEntry.cs"
    text = entry.read_text(encoding="utf-8-sig")
    anchor = "public class ModEntry : Mod {"
    assert text.count(anchor) == 1
    entry.write_text(text.replace(anchor, anchor + "\n\t\tpublic override object GetApi() => new AgentControl();"))
    bot = stage / "Bot/BotObject.cs"
    text = bot.read_text(encoding="utf-8-sig")
    anchor = "BotFarmer farmer;"
    assert text.count(anchor) == 1
    bot.write_text(text.replace(anchor, anchor + "\n\t\tinternal Farmer AgentFarmer => farmer;"))
    shutil.copyfile(ROOT / "mod/FarmtronicsAdapter/AgentControl.cs", stage / "AgentControl.cs")
    mods = ROOT / "work/Mods"
    mods.mkdir(parents=True, exist_ok=True)
    for project, dest in [(stage / "Farmtronics.csproj", "Farmtronics"), (ROOT / "mod/AgentBridge/AgentBridge.csproj", "AgentBridge")]:
        subprocess.run([DOTNET, "build", str(project), "-c", "Release", "--nologo", f"-p:GamePath={GAME}", "-p:EnableModDeploy=false", "-p:EnableModZip=false"], check=True, cwd=ROOT)
        output = mods / dest
        output.mkdir(exist_ok=True)
        for dll in (project.parent / "bin/Release/net6.0").glob("*.dll"):
            shutil.copyfile(dll, output / dll.name)
        shutil.copyfile(project.parent / "manifest.json", output / "manifest.json")
        for directory in ("assets", "i18n"):
            if (project.parent / directory).exists():
                shutil.copytree(project.parent / directory, output / directory, dirs_exist_ok=True)
    # Development console only; existing user Mods are not loaded or modified.
    shutil.copytree(GAME / "Mods/ConsoleCommands", mods / "ConsoleCommands", dirs_exist_ok=True)
    print("Built isolated Mods:", mods)

if __name__ == "__main__":
    build()
