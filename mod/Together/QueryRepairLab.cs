using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
namespace Together;
public sealed partial class ModEntry {
    private Task<ModelReply>? queryLabReply;
    private object QueryRepairFixture(string mode,JsonElement args) {
        // Caller enforces all three AgentLab gates. No fixture is an acceptance run.
        if(mode=="read")return new{actual_spent=NativePurchaseSpent(),pending=PendingQueries(),diary=DiaryContext(),schedule=AgentPlanRead(),snapshot=AgentSnapshot(),inventory=AgentToolRegistry.Inventory(),memory=AgentMemoryContext(),planting=PlantingExecutionFacts(),plans=farmPlantPlans.Values.Select(p=>new{p.Id,p.Seed,p.PossibleSeeds,verified_tiles=p.NativeCrops.Select(t=>new{tile=t.Key,crop=t.Value})}),queries=Data.Autoplay.Memory.Queries.Select(q=>new{q.Id,q.Tool,q.Delivered})};
        if(mode=="route"){var r=PlayerExecutor.ResolveRoute(Game1.currentLocation,AgentToolRegistry.Text(args,"destination","Farm"));return new{r.Reachable,r.Reason,r.Transitions,r.Evidence,first=r.First==null?null:new{r.First.X,r.First.Y,r.First.TargetName,r.First.TargetX,r.First.TargetY}};}
        if(mode=="route_site") {PauseAutoplay("lab_route_site");Game1.activeClickableMenu=null;Game1.warpFarmer("Town",13,85,false);return new{positioning=true};}
        if(mode=="collision_probe") {
            var l=Game1.currentLocation;var p=Game1.player;var origin=p.GetBoundingBox();
            var samples=Enumerable.Range(-24,49).SelectMany(y=>Enumerable.Range(-24,49).Select(x=>{
                var b=origin;b.Offset(x*4,y*4);return new{x=x*4,y=y*4,clear=!l.isCollidingPosition(b,Game1.viewport,true,0,false,p,false,false,false,true)};
            })).ToArray();
            return new{origin=origin.ToString(),position=p.Position.ToString(),escape=PlayerRouteController.EscapeTile()?.ToString(),samples,
                tiles=Enumerable.Range(11,6).SelectMany(x=>Enumerable.Range(83,6).Select(y=>new{x,y,action=l.doesTileHaveProperty(x,y,"Action","Buildings"),building=l.Map.GetLayer("Buildings").Tiles[x,y]?.TileIndex,back=l.Map.GetLayer("Back").Tiles[x,y]?.TileIndex}))};
        }
        if(mode=="stale_route_probe") {
            var before=Game1.player.Position;var other=Game1.getLocationFromName("FarmHouse");
            if(other==Game1.currentLocation)throw new InvalidOperationException("probe_requires_other_map");
            var controller=new PlayerRouteController(new Stack<Point>(new[]{new Point(4,4)}),other,Game1.player,new Point(4,4));
            bool ended=controller.update(Game1.currentGameTime);
            return new{ended,position_unchanged=Game1.player.Position==before,before=before.ToString(),after=Game1.player.Position.ToString()};
        }
        if(mode=="night_setup") {
            Game1.activeClickableMenu=null;Game1.timeOfDay=2300;Game1.player.addItemToInventory(ItemRegistry.Create("(O)131",1));
            Data.Business.Enabled=true;Data.Business.Automation=false;
            Data.Autoplay.TrialTargetDay=Game1.Date.TotalDays+1;Data.Autoplay.TrialTargetSleeps=Data.Autoplay.SleepDays+1;
            return new{day=Game1.Date.TotalDays,Data.Autoplay.SleepDays,inventory=AgentToolRegistry.Inventory(),shipping=Game1.getFarm().getShippingBin(Game1.player).Select(i=>i.QualifiedItemId).ToArray()};
        }
        if(mode=="shop_site") {PauseAutoplay("lab_shop_site");Game1.activeClickableMenu=null;Game1.timeOfDay=850;Game1.warpFarmer("Town",43,58,false);return new{positioning=true};}
        if(mode=="craft_setup") {PauseAutoplay("lab_known_recipe");Game1.activeClickableMenu=null;Game1.player.addItemToInventory(ItemRegistry.Create("(O)388",50));return new{setup_only=true,inventory=AgentToolRegistry.Inventory()};}
        if(mode=="seed_setup") {
            PauseAutoplay("lab_seed_setup");Game1.activeClickableMenu=null;
            if(!Game1.player.Items.Any(i=>i?.QualifiedItemId=="(O)770"))Game1.player.addItemToInventory(ItemRegistry.Create("(O)770",3));
            Game1.warpFarmer("Farm",64,18,false);
            return new{setup_only=true,inventory=AgentToolRegistry.Inventory()};
        }
        if(mode=="queries") {
            var calls=new[]{"location:SeedShop","craft:Chest","(O)472","location:FishShop","craft:Scarecrow","(O)770"}.Select((id,n)=>new AgentCall{id="read-"+n,tool="knowledge.search",args=JsonSerializer.SerializeToElement(new{id,purpose="核对对象后规划"})}).ToList();
            foreach(var call in calls)RecordToolAttempt(call,JsonSerializer.SerializeToElement(agentTools.Execute(call.tool,call.args),AgentJson.Options));
            return new{pending=PendingQueries(),snapshot=AgentSnapshot()};
        }
        if(mode=="model_query") {
            if(queryLabReply!=null&&!queryLabReply.IsCompleted)throw new InvalidOperationException("query_probe_busy");
            var delivered=JsonSerializer.SerializeToElement(PendingQueries(),AgentJson.Options);
            queryRequestIds=delivered.EnumerateArray().Select(q=>q.GetProperty("result_id").GetString()!).ToArray();
            string context=AgentJson.Encode(new{goal="这是隔离工具验证，请根据本批查询/终态观察解释下一步，调用一个world.read；不发劳动任务。",pending_queries=delivered,inventory=AgentToolRegistry.Inventory(),task_card=TaskCard(),active_actors=new[]{"player"}});
            string trace=Path.Combine(Helper.DirectoryPath,"logs",Game1.uniqueIDForThisGame.ToString(),agentSaveEpoch,"query-combination-model.jsonl");
            string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile),model=Settings.Model;
            queryLabReply=Task.Run(()=>AutoplayModel.Ask(file,model,context,CancellationToken.None,trace,Settings.AutoplayInputTokenBudget));return new{started=true,trace};
        }
        if(mode=="model_result") {
            if(queryLabReply==null||!queryLabReply.IsCompleted)return new{status="running"};
            var reply=queryLabReply.GetAwaiter().GetResult();AcknowledgeQueries();return new{status="succeeded",reply.Json,reply.Tokens};
        }
        throw new InvalidOperationException("unknown_query_repair_fixture");
    }
}
