using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class PlayerExecutor {
    private string tappingOutput="",tappingItem="",tappingGoal="";
    private Point? tappingTile;
    internal static bool TreeCanProduce(Tree tree,string output)=>DataLoader.WildTrees(Game1.content).TryGetValue(tree.treeType.Value,out var data)&&data.TapItems?.Any(i=>(ItemRegistry.QualifyItemId(i.ItemId)??i.ItemId)==output)==true;
    private void StartTapTree(JsonElement args) {
        tappingOutput=AgentToolRegistry.Text(args,"output");tappingItem=AgentToolRegistry.Text(args,"tapper","(BC)105");tappingGoal=AgentToolRegistry.Text(args,"goal_id");
        destination=AgentToolRegistry.Text(args,"location","Farm");tappingTile=null;Current!.phase="tapper_travel";
        if(ItemRegistry.GetDataOrErrorItem(tappingOutput).IsErrorItem)throw new InvalidOperationException("known_tree_product_required");
    }
    private void TickTapTree() {
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("tapper_menu_interrupted");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        var l=Game1.currentLocation;
        if(tappingTile==null) {
            foreach(var pair in l.terrainFeatures.Pairs.Where(p=>p.Value is Tree tree&&tree.growthStage.Value>=5&&!tree.stump.Value&&TreeCanProduce(tree,tappingOutput)).OrderBy(p=>Vector2.DistanceSquared(p.Key,Game1.player.Tile))) {
                bool ready=l.objects.TryGetValue(pair.Key,out var tap)&&tap.readyForHarvest.Value&&tap.heldObject.Value?.QualifiedItemId==tappingOutput;
                if(!ready&&l.objects.ContainsKey(pair.Key)||PlacementProtected?.Invoke(destination,pair.Key.ToPoint())==true)continue;
                try{Walk(Approach(pair.Key.ToPoint(),true));tappingTile=pair.Key.ToPoint();break;}catch(InvalidOperationException){}
            }
            if(tappingTile==null)throw new InvalidOperationException("tree_product_growing_or_no_reachable_mature_tree");
        }
        if(Game1.player.TilePoint!=target){MonitorWalk();return;}StopWalk();var tile=tappingTile.Value;Adjacent(tile);Face(tile);
        if(l.terrainFeatures.GetValueOrDefault(tile.ToVector2()) is not Tree treeNow||!TreeCanProduce(treeNow,tappingOutput))throw new InvalidOperationException("tapper_tree_changed");
        if(l.objects.TryGetValue(tile.ToVector2(),out var machine)) {
            var output=machine.heldObject.Value;
            if(!machine.readyForHarvest.Value||output?.QualifiedItemId!=tappingOutput)throw new InvalidOperationException("tapper_not_ready");
            CapacityAdapter.RequireReceive(Game1.player,output);
            int before=Game1.player.Items.Where(i=>i?.QualifiedItemId==tappingOutput).Sum(i=>i.Stack),expected=output.Stack;
            machine.checkForAction(Game1.player);
            int gained=Game1.player.Items.Where(i=>i?.QualifiedItemId==tappingOutput).Sum(i=>i.Stack)-before;
            if(gained!=expected)throw new InvalidOperationException("native_tapper_collection_not_verified");
            Current!.effects.Add(new{kind="native_tree_product_collected",output=tappingOutput,gained});
        }else {
            var p=Game1.player;int slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]?.QualifiedItemId==tappingItem,-1);
            if(slot<0||p.Items[slot] is not StardewValley.Object tapper)throw new InvalidOperationException("tapper_supply_missing");
            ValidateConsumption?.Invoke(new Dictionary<Item,int>{{tapper,1}},tappingGoal,tappingItem);
            int before=p.Items.Where(i=>i?.QualifiedItemId==tappingItem).Sum(i=>i.Stack);PlayerSelection.Set(p,slot);p.netItemStowed.Value=false;
            Utility.tryToPlaceItem(l,tapper,tile.X*64,tile.Y*64);
            if(!treeNow.tapped.Value||!l.objects.TryGetValue(tile.ToVector2(),out var placed)||placed.QualifiedItemId!=tappingItem||before-p.Items.Where(i=>i?.QualifiedItemId==tappingItem).Sum(i=>i.Stack)!=1)throw new InvalidOperationException("native_tapper_install_not_verified");
            Current!.effects.Add(new{kind="native_tapper_installed",tapper=tappingItem,desired_output=tappingOutput,minutes=placed.MinutesUntilReady,note="安装完成不等于已有树脂，等待原生计时"});
        }
        Current!.completed=1;Finish("succeeded");
    }
}
public sealed partial class ModEntry {
    private IEnumerable<(GameLocation Location,Vector2 Tile,Tree Tree)> TappingSources(string item)=>MaterialLocations().SelectMany(l=>l.terrainFeatures.Pairs.Where(p=>p.Value is Tree t&&t.growthStage.Value>=5&&!t.stump.Value&&PlayerExecutor.TreeCanProduce(t,item)).Select(p=>(l,p.Key,(Tree)p.Value)));
    private bool HasTappedSource(string item)=>TappingSources(item).Any(t=>t.Tree.tapped.Value&&t.Location.objects.ContainsKey(t.Tile));
    private bool PrepareTappedMaterial(GoalNode node,Action<string,object,string> add,out string reason) {
        reason="";var sources=TappingSources(node.Item).Where(t=>t.Tree.tapped.Value&&t.Location.objects.ContainsKey(t.Tile)).ToArray();
        if(sources.Length==0)return false;
        var ready=sources.FirstOrDefault(t=>t.Location.objects[t.Tile].readyForHarvest.Value&&t.Location.objects[t.Tile].heldObject.Value?.QualifiedItemId==node.Item);
        if(ready.Location!=null)add("player.tap_tree",new{output=node.Item,location=ready.Location.NameOrUniqueName},"从原生树液器收取实际产物");
        else reason="tree_product_waiting_for_native_timer";
        return true;
    }
    private bool PrepareBusinessTreeSource(string item,int count) {
        var preview=new SharedGoal{AllowNewFacilities=true,Entity=item,Item=item,Count=count};
        GoalPlanner.Rebuild(preview,goalRecipes,new GoalLedger(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category})),Game1.Date.TotalDays,id=>id);
        foreach(var need in preview.Nodes.Where(n=>n.Kind=="gather"&&n.ToPrepare>0)) {
            if(HasTappedSource(need.Item))continue;
            var tree=TappingSources(need.Item).FirstOrDefault(t=>!t.Tree.tapped.Value&&!t.Location.objects.ContainsKey(t.Tile));if(tree.Location==null)continue;
            var actions=new List<(string Tool,object Args)>();
            if(!BusinessMaterials(new(){["(BC)105"]=1},actions))return Data.Business.Tasks.Count>0;
            actions.Add(("player.tap_tree",new{output=need.Item,tapper="(BC)105",location=tree.Location.NameOrUniqueName}));
            return QueueBusiness("tapper:"+need.Item,actions,"先建立设备所需树脂供给，保留树木并等待原生生产");
        }
        return false;
    }
}
