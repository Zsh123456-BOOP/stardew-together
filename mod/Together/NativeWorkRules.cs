using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;
using StardewValley.TerrainFeatures;
namespace Together;
public sealed partial class PlayerExecutor {
    public Func<GameLocation,Point,bool>? WorkProtected {get;set;}
    internal static float SwingEnergy(Tool? tool) {
        if(tool==null||tool.isScythe()||tool.isEfficient.Value)return 0;
        int level=tool is Axe?Game1.player.ForagingLevel:tool is Pickaxe?Game1.player.MiningLevel:Game1.player.FarmingLevel;
        return Math.Max(0,2-level*.1f); // single, uncharged native swing
    }
    internal static float TreeEnergy(Tree tree,Tool? tool) {
        float damage=tool?.UpgradeLevel switch {0=>1f,1=>1.25f,2=>1.67f,3=>2.5f,4=>5f,_=>Math.Max(1,(tool?.UpgradeLevel??0)+1)};
        return ((float)Math.Ceiling(Math.Max(0,tree.health.Value)/damage)+(tree.stump.Value?0:(float)Math.Ceiling(5/damage)))*SwingEnergy(tool);
    }
    internal static float ResourceEnergy(StardewValley.Object obj,Tool? tool)=>obj.IsWeeds()?0:
        Math.Max(1,obj.IsTwig()?1:(float)Math.Ceiling(Math.Max(1,obj.MinutesUntilReady)/(double)Math.Max(1,(tool?.UpgradeLevel??0)+1)))*SwingEnergy(tool);
    internal static Rectangle StandingBox(Point tile) {
        var box=Game1.player.GetBoundingBox();box.Offset(tile.X*64+32-box.Center.X,tile.Y*64+40-box.Center.Y);return box;
    }
    private bool SafeSweep(GameLocation location,Point target,Point stand,Rectangle box) {
        if(Game1.player.Items[workSlot] is not MeleeWeapon scythe||!scythe.isScythe())return true;
        int facing=target.X>stand.X?1:target.X<stand.X?3:target.Y>stand.Y?2:0;
        int x=facing==1?box.Right+48:facing==3?box.Left-48:box.Center.X;
        int y=facing==0?box.Top-48:facing==2?box.Bottom+48:box.Center.Y;
        for(int frame=0;frame<6;frame++) {
            var area=NativeScytheGeometry.Area(x,y,facing,box,frame,scythe.addedAreaOfEffect.Value,scythe.type.Value);
            foreach(var tile in Utility.getListOfTileLocationsForBordersOfNonTileRectangle(area)) {
                if(WorkProtected?.Invoke(location,tile.ToPoint())==true)return false;
                if(location.objects.TryGetValue(tile,out var item)&&!item.IsWeeds()&&!item.IsTwig()&&item.BaseName!="Stone")return false;
                if(location.terrainFeatures.TryGetValue(tile,out var f)&&f is not Grass&&f is not HoeDirt {crop:null}&&f is not HoeDirt {crop.dead.Value:true})return false;
            }
        }
        return true;
    }
    private Point SafeWorkStand(Point target,Point preferred) {
        // Bare-hand harvest/forage has no tool slot; it must never index Items[-1].
        if(workSkill is "harvest" or "forage")return preferred;
        if(workSlot<0||workSlot>=Game1.player.Items.Count)throw new InvalidOperationException("work_tool_slot_invalid");
        if(Game1.player.Items[workSlot] is not Tool { } tool||!tool.isScythe())return preferred;
        foreach(var stand in new[]{preferred,new Point(target.X,target.Y+1),new(target.X-1,target.Y),new(target.X+1,target.Y),new(target.X,target.Y-1)}.Distinct())
            if(Passable(Game1.currentLocation,stand)&&SafeSweep(Game1.currentLocation,target,stand,StandingBox(stand))&&(stand==Game1.player.TilePoint||PreviewPath(Game1.currentLocation,stand)!=null))return stand;
        // Single-target axe is only a last resort for weeds with protected collateral.
        int axe=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is Axe,-1);
        if(workSkill=="clear"&&axe>=0){workSlot=axe;if(workSteps.Count>workIndex)workSteps[workIndex]=(workSkill,axe);return preferred;}
        throw new InvalidOperationException("protected_scythe_sweep_no_safe_stand");
    }
}
