using System.Text.Json;
using StardewValley;
using StardewValley.Objects;

namespace Together;
public sealed partial class ModEntry {
    // Returns the next physical operation, or a real material prerequisite. Waiting
    // for the native overnight catch is not a request to end the rest of the day.
    private (string Item,int Count,string Wait) PlanCrabFish(string fish,List<(string Tool,object Args)> actions) {
        var p=Game1.player;
        var pots=PlayerExecutor.CrabPots().Where(t=>PlayerExecutor.CrabWaterMatches(t.Location,t.Pot.TileLocation,fish)).ToArray();
        if(pots.Any()) {
            var pending=pots.Where(t=>t.Pot.readyForHarvest.Value||t.Pot.NeedsBait(p)).GroupBy(t=>t.Location).OrderByDescending(g=>g.Any(t=>t.Pot.readyForHarvest.Value)).FirstOrDefault();
            if(pending==null)return ("",0,"蟹笼已布置并满足鱼饵条件，等待原生过夜产出，今天继续其它工作");
            if(p.Items.Count(i=>i==null)<2){actions.Add(("work.run",new{goal="store"}));return ("",0,"");}
            int bait=p.professions.Contains(11)?0:pending.Count();
            if(p.Items.Where(i=>i?.QualifiedItemId=="(O)685").Sum(i=>i.Stack)<bait)return ("(O)685",bait,"");
            actions.Add(("player.crab_pots",new{mode="tend",location=pending.Key.NameOrUniqueName,item=fish,count=0}));return ("",0,"");
        }
        if(!p.Items.Any(i=>i?.QualifiedItemId=="(O)710"))return ("(O)710",1,"");
        foreach(var l in Game1.locations.OrderBy(l=>l==Game1.currentLocation?0:1)) {
            if(l!=Game1.currentLocation&&PlayerExecutor.NextExit(Game1.currentLocation,l.NameOrUniqueName)==null)continue;
            var layer=l.Map.Layers[0];bool valid=false;
            for(int y=1;y<layer.LayerHeight-1&&!valid;y++)for(int x=1;x<layer.LayerWidth-1;x++)if(CrabPot.IsValidCrabPotLocationTile(l,x,y)&&PlayerExecutor.CrabWaterMatches(l,new(x,y),fish)){valid=true;break;}
            if(!valid)continue;
            actions.Add(("player.crab_pots",new{mode="place",location=l.NameOrUniqueName,item=fish,count=1}));return ("",0,"");
        }
        return ("",0,"当前没有可达的匹配海水/淡水蟹笼区域");
    }
    private bool PursueTrapFish(ProgressPursuit pursuit,string item) {
        var actions=new List<(string Tool,object Args)>();var requirement=PlanCrabFish(item,actions);
        if(requirement.Wait.Length>0){PursuitState(pursuit,"waiting",requirement.Wait);return true;}
        if(requirement.Item.Length>0&&!PursuitMaterials(pursuit,new[]{(requirement.Item,requirement.Count,0)},actions))return true;
        if(actions.Count>0)QueuePursuit(pursuit,actions);
        return true;
    }
}
