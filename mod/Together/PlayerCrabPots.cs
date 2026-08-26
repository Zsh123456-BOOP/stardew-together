using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace Together;
public sealed partial class PlayerExecutor {
    private string crabMode="",crabFish="",crabBait="";
    private int crabCount,crabUnreachable;
    private Point? crabTile;
    private readonly HashSet<Point> crabVisited=new();
    internal static bool IsTrapFish(string item)=>DataLoader.Fish(Game1.content).TryGetValue(item.StartsWith("(O)")?item[3..]:item,out var raw)&&KnowledgeRules.Field(raw.Split('/'),1)=="trap";
    internal static bool CrabWaterMatches(GameLocation l,Vector2 tile,string fish) {
        if(fish.Length==0)return true;
        if(!DataLoader.Fish(Game1.content).TryGetValue(fish.StartsWith("(O)")?fish[3..]:fish,out var raw)||!IsTrapFish(fish))return false;
        return KnowledgeRules.Field(raw.Split('/'),4).Split(' ',StringSplitOptions.RemoveEmptyEntries).Intersect(l.GetCrabPotFishForTile(tile)).Any();
    }
    internal static IEnumerable<(GameLocation Location,CrabPot Pot)> CrabPots()=>Game1.locations.Concat(Game1.getFarm().buildings.Select(b=>b.GetIndoors()).Where(l=>l!=null)).Distinct().SelectMany(l=>l.objects.Values.OfType<CrabPot>().Where(p=>p.owner.Value==Game1.player.UniqueMultiplayerID).Select(p=>(l,p)));
    internal static object ReadCrabPots()=>new{pots=CrabPots().Select(t=>new{location=t.Location.NameOrUniqueName,tile=t.Pot.TileLocation,ready=t.Pot.readyForHarvest.Value,output=t.Pot.heldObject.Value?.QualifiedItemId,bait=t.Pot.bait.Value?.QualifiedItemId,needs_bait=t.Pot.NeedsBait(Game1.player),water_types=t.Location.GetCrabPotFishForTile(t.Pot.TileLocation)}),note="原生过夜产出；钓获统计须由玩家实际收取，不以放置/投饵计为捕获"};
    private void StartCrabPots(JsonElement args) {
        crabMode=AgentToolRegistry.Text(args,"mode","tend");crabFish=AgentToolRegistry.Text(args,"item");crabBait=AgentToolRegistry.Text(args,"bait","(O)685");crabCount=AgentToolRegistry.Number(args,"count",crabMode=="place"?1:0);
        if(crabMode is not ("place" or "tend")||crabCount is <0 or >40||crabMode=="place"&&crabCount==0||crabFish.Length>0&&!IsTrapFish(crabFish))throw new InvalidOperationException("invalid_crab_pot_job");
        destination=AgentToolRegistry.Text(args,"location",origin);if(LoadedLocation(destination)==null)throw new InvalidOperationException("crab_pot_location_unavailable");
        crabTile=null;crabVisited.Clear();crabUnreachable=0;Current!.phase="crab_pot_travel";
    }
    private void TickCrabPots() {
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("crab_pot_menu_interrupted");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(crabCount>0&&Current!.completed>=crabCount){Finish("succeeded");return;}
        var l=Game1.currentLocation;var p=Game1.player;
        if(crabTile==null) {
            IEnumerable<Point> candidates;
            if(crabMode=="place") {
                var layer=l.Map.Layers[0];var sites=new List<Point>();
                for(int y=1;y<layer.LayerHeight-1;y++)for(int x=1;x<layer.LayerWidth-1;x++)if(CrabPot.IsValidCrabPotLocationTile(l,x,y)&&CrabWaterMatches(l,new Vector2(x,y),crabFish)&&PlacementProtected?.Invoke(destination,new(x,y))!=true)sites.Add(new(x,y));
                candidates=sites;
            } else candidates=l.objects.Pairs.Where(t=>t.Value is CrabPot pot&&pot.owner.Value==p.UniqueMultiplayerID&&CrabWaterMatches(l,t.Key,crabFish)&&(pot.readyForHarvest.Value||pot.NeedsBait(p))).Select(t=>t.Key.ToPoint());
            foreach(var tile in candidates.Where(t=>!crabVisited.Contains(t)).OrderBy(t=>Vector2.DistanceSquared(t.ToVector2(),p.Tile))) {
                crabVisited.Add(tile);try{Walk(Approach(tile,true));crabTile=tile;Current!.phase="crab_pot_walk";break;}catch(InvalidOperationException){crabUnreachable++;}
            }
            if(crabTile==null){Finish(crabCount==0&&crabUnreachable==0?"succeeded":"failed",crabCount==0&&crabUnreachable==0?null:"no_reachable_eligible_crab_pot_target");return;}
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();var at=crabTile.Value;Adjacent(at);Face(at);
        if(crabMode=="place") {
            int slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]?.QualifiedItemId=="(O)710",-1);if(slot<0)throw new InvalidOperationException("crab_pot_item_missing");
            var item=(StardewValley.Object)p.Items[slot];ValidateConsumption?.Invoke(new Dictionary<Item,int>{{item,1}},"","place:(O)710");p.CurrentToolIndex=slot;p.netItemStowed.Value=false;
            int before=p.Items.Where(i=>i?.QualifiedItemId=="(O)710").Sum(i=>i.Stack);
            if(!CrabPot.IsValidCrabPotLocationTile(l,at.X,at.Y)||!Utility.tryToPlaceItem(l,item,at.X*64,at.Y*64)||!l.objects.TryGetValue(at.ToVector2(),out var placed)||placed is not CrabPot native||native.owner.Value!=p.UniqueMultiplayerID||before-p.Items.Where(i=>i?.QualifiedItemId=="(O)710").Sum(i=>i.Stack)!=1)throw new InvalidOperationException("native_crab_pot_placement_not_verified");
            Current!.effects.Add(new{kind="native_crab_pot_placed",location=destination,tile=at});
        } else {
            if(!l.objects.TryGetValue(at.ToVector2(),out var o)||o is not CrabPot pot||pot.owner.Value!=p.UniqueMultiplayerID)throw new InvalidOperationException("crab_pot_changed");
            if(pot.readyForHarvest.Value&&pot.heldObject.Value is {} output) {
                if(!p.couldInventoryAcceptThisItem(output))throw new InvalidOperationException("crab_pot_inventory_full");
                string item=output.QualifiedItemId;int before=p.Items.Where(i=>i?.QualifiedItemId==item).Sum(i=>i.Stack),caught=FishingRules.Caught(item);
                if(!pot.checkForAction(p)||pot.heldObject.Value!=null||p.Items.Where(i=>i?.QualifiedItemId==item).Sum(i=>i.Stack)<=before)throw new InvalidOperationException("native_crab_pot_collection_not_verified");
                int nativeCatches=FishingRules.Caught(item)-caught;if(IsTrapFish(item)&&nativeCatches<=0)throw new InvalidOperationException("native_trap_fish_record_not_verified");
                Current!.effects.Add(new{kind="native_crab_pot_catch",item,caught=nativeCatches,tile=at});
                // Keep this target while its native harvest animation finishes.
                return;
            }
            if(pot.NeedsBait(p)) {
                int slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]?.QualifiedItemId==crabBait&&p.Items[i].Category==-21,-1);
                if(slot<0)throw new InvalidOperationException("crab_pot_bait_missing");
                var bait=p.Items[slot];ValidateConsumption?.Invoke(new Dictionary<Item,int>{{bait,1}},"","crab_pot_bait");int before=p.Items.Where(i=>i?.QualifiedItemId==crabBait).Sum(i=>i.Stack);
                p.CurrentToolIndex=slot;p.netItemStowed.Value=false;NativeMenuInput.InteractWorld(at);
                if(pot.bait.Value?.QualifiedItemId!=crabBait||before-p.Items.Where(i=>i?.QualifiedItemId==crabBait).Sum(i=>i.Stack)!=1)throw new InvalidOperationException("native_crab_pot_bait_not_verified");
                Current!.effects.Add(new{kind="native_crab_pot_bait",item=crabBait,tile=at,consumed=1});
            }
        }
        Current!.completed++;crabTile=null;Current.phase="crab_pot_select";
    }
}
