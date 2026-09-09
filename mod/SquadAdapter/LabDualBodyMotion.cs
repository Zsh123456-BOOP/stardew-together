using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
namespace TheStardewSquad;
public sealed partial class CompanionControl {
    private readonly Dictionary<NPC,Point> labBodyWalk=new();
    private readonly Dictionary<NPC,(Vector2 Center,Point Offset)> labCollectWalk=new();
    private void RequireDualBodyLab() {
        using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(mod.Helper.DirectoryPath,"..","Together","config.json")));
        if(!config.RootElement.TryGetProperty("EnableLab",out var enabled)||!enabled.GetBoolean()||!Context.IsWorldReady||Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
    }
    public void LabDualBodyWalk(string name,int x,int y,bool active) {
        RequireDualBodyLab();
        var mate=Members.Single(m=>m.Npc.Name==name);
        mod.FollowerManager.ClearMateTaskAndReset(mate);stay.Add(Id(mate));managed.Add(Id(mate));
        labCollectWalk.Remove(mate.Npc);
        if(active)labBodyWalk[mate.Npc]=new Point(x,y);else labBodyWalk.Remove(mate.Npc);
    }
    public void LabDualBodyCollectWalk(string name,float x,float y,int offsetX,int offsetY) {
        RequireDualBodyLab();
        // Reuse the gated navigation entry, but retain the path while a chunk settles.
        var mate=Members.Single(m=>m.Npc.Name==name);
        if(!labCollectWalk.ContainsKey(mate.Npc))LabDualBodyWalk(name,0,0,true);
        if(!Context.IsWorldReady||Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
        labCollectWalk[mate.Npc]=(new Vector2(x,y),new Point(offsetX,offsetY));
    }
}
