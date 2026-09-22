using Microsoft.Xna.Framework;
using StardewValley;
using StardewModdingAPI;
namespace Together;
public sealed partial class ModEntry {
    private NPC? labNavigationNpc;
    private Vector2 labNavigationNpcPosition;
    private GameLocation? labNavigationNpcLocation;
    private object? labNavigationNpcController;
    private string LabNavigationProbe(string mode) {
        if(!Settings.EnableLab||!Context.IsWorldReady||Game1.player.Name!="AgentLab"||Context.IsMultiplayer)throw new InvalidOperationException("isolated_lab_required");
        if(mode=="enter")Game1.warpFarmer("Town",45,73,false);
        if(mode=="setup") {
            PauseAutoplay("lab_navigation_fixture");AutoplayPauseClock.Release();Game1.exitActiveMenu();
            if(Game1.currentLocation.NameOrUniqueName!="Town"||Game1.fadeToBlack)throw new InvalidOperationException("wait_for_native_town_entry");
            var originalBody=Game1.player.GetBoundingBox();Game1.player.Position+=new Vector2(2900-originalBody.X,4699-originalBody.Y);
            labNavigationNpc=Game1.getCharacterFromName("Evelyn");labNavigationNpcPosition=labNavigationNpc.Position;labNavigationNpcLocation=labNavigationNpc.currentLocation;labNavigationNpcController=labNavigationNpc.controller;
            if(labNavigationNpc.currentLocation!=Game1.currentLocation){labNavigationNpc.currentLocation.characters.Remove(labNavigationNpc);Game1.currentLocation.characters.Add(labNavigationNpc);labNavigationNpc.currentLocation=Game1.currentLocation;}
            labNavigationNpc.controller=null;labNavigationNpc.Halt();
            var body=Game1.player.GetBoundingBox();var box=labNavigationNpc.GetBoundingBox();labNavigationNpc.Position+=new Vector2(body.Left-box.Width-1-box.X,body.Y-box.Y);
        }
        if(mode=="release_npc"||mode=="restore") {
            if(labNavigationNpc!=null&&labNavigationNpcLocation!=null){labNavigationNpc.currentLocation.characters.Remove(labNavigationNpc);labNavigationNpcLocation.characters.Add(labNavigationNpc);labNavigationNpc.currentLocation=labNavigationNpcLocation;labNavigationNpc.Position=labNavigationNpcPosition;labNavigationNpc.controller=(StardewValley.Pathfinding.PathFindController?)labNavigationNpcController;labNavigationNpc=null;}
        }
        if(mode=="fault") {
            Data.Autoplay.Status="running";PauseAutoplay("labyrinth_route_probe_fault");
        }
        if(mode=="release_clock")AutoplayPauseClock.Release();
        return AgentJson.Encode(new{position=Game1.player.Position.ToString(),body=Game1.player.GetBoundingBox().ToString(),npc=labNavigationNpc?.GetBoundingBox().ToString(),held=AutoplayPauseClock.Held,time=Game1.timeOfDay,should_pass=Game1.shouldTimePass(),action=playerExecutor.Current});
    }
}
