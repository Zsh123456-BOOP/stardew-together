using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Tools;

namespace Together;
public sealed partial class ModEntry {
    private void TickMineTrip(SemanticJob job) {
        var p=Game1.player;
        if(job.NativeQuest!=null&&(job.NativeQuest.completed.Value||NativeQuestIdentity.Count(job.NativeQuest).Current>=NativeQuestIdentity.Count(job.NativeQuest).Required))job.MineReturnReason="native_quest_objective_reached";
        if((job.Attempts>=512||(DateTime.UtcNow-job.Started).TotalMinutes>25)&&job.MineReturnReason.Length==0)job.MineReturnReason="mine_execution_budget_return";
        if(Game1.timeOfDay>=job.Until&&job.MineReturnReason.Length==0)job.MineReturnReason="mine_time_reserve_return";
        if((p.health<45||p.Stamina<job.Reserve+6)&&job.MineReturnReason.Length==0) {
            if(TryWorkFood(job))return;job.MineReturnReason="mine_supply_reserve_return";
        }
        if(p.Items.Count(i=>i==null)<2&&job.MineReturnReason.Length==0)job.MineReturnReason="mine_inventory_return";
        if(Game1.currentLocation is MineShaft mine) {
            if((mine.mineLevel>120)!=(job.MineRegion=="skull")){job.MineReturnReason="mine_region_changed";WorkChild(job,"player.mine_access",new{mode="leave"},"mine_exit");return;}
            int depth=mine.mineLevel-(job.MineRegion=="skull"?120:0);job.completed=Math.Max(job.completed,depth);
            if(job.MineReturnReason.Length==0&&PlayerExecutor.TreasureChests(mine).Any()){WorkChild(job,"player.treasure",new{},"mine_treasure");return;}
            if(job.MineReturnReason.Length==0&&job.NativeQuest is StardewValley.Quests.SlayMonsterQuest quest&&mine.characters.OfType<Monster>().Any(m=>m.Health>0&&quest.OnMonsterSlain(mine,m,false,false,true))) {
                WorkChild(job,"player.combat",new{count=1,quest_id=job.quest_id,min_health=40},"mine_combat");return;
            }
            if(job.MineReturnReason.Length==0&&job.NativeQuest is StardewValley.Quests.ResourceCollectionQuest resource) {
                var ore=mine.objects.Pairs.Where(o=>!job.Excluded.Contains(o.Key.X+","+o.Key.Y)&&ResourceRules.Nodes.GetValueOrDefault(o.Value.ItemId)==resource.ItemId.Value).OrderBy(o=>Vector2.DistanceSquared(o.Key,p.Tile)).FirstOrDefault(o=>WorkStand(mine,o.Key.ToPoint())!=null);
                int pick=WorkSlot(i=>i is Pickaxe);
                if(ore.Value!=null&&pick>=0&&p.Stamina-Math.Max(4,ore.Value.MinutesUntilReady*2+2)>=job.Reserve){WorkChild(job,"player.work",new{skill="clear",slot=pick,tiles=new[]{new{x=(int)ore.Key.X,y=(int)ore.Key.Y}}},"mine_stone",ore.Key.X+","+ore.Key.Y);return;}
            }
            if(depth>=job.MineTarget&&job.MineReturnReason.Length==0)job.MineReturnReason="mine_target_depth_reached";
            if(job.MineReturnReason.Length>0){WorkChild(job,"player.mine_access",new{mode="leave"},"mine_exit");return;}
            if(job.MineFloor!=mine.mineLevel){job.MineFloor=mine.mineLevel;job.Excluded.Clear();}
            if(mine.characters.OfType<Monster>().Any(m=>m.Health>0&&Vector2.DistanceSquared(m.Position,p.Position)<320*320)) {
                WorkChild(job,"player.combat",new{count=1,min_health=40},"mine_combat");return;
            }
            var ladders=new List<Point>();
            for(int y=0;y<mine.Map.Layers[0].LayerHeight;y++)for(int x=0;x<mine.Map.Layers[0].LayerWidth;x++)if(mine.getTileIndexAt(x,y,"Buildings")==173)ladders.Add(new(x,y));
            if(ladders.Any(at=>WorkStand(mine,at).HasValue)){WorkChild(job,"player.mine_descend",new{},"mine_descend");return;}
            int slot=WorkSlot(i=>i is Pickaxe);if(slot<0){job.MineReturnReason="mine_pickaxe_missing";return;}
            foreach(var pair in mine.objects.Pairs.Where(o=>o.Value.IsBreakableStone()).OrderBy(o=>Vector2.DistanceSquared(o.Key,p.Tile))) {
                string key=pair.Key.X+","+pair.Key.Y;if(job.Excluded.Contains(key)||AgentTileBusy(mine.NameOrUniqueName,(int)pair.Key.X,(int)pair.Key.Y))continue;
                if(WorkStand(mine,pair.Key.ToPoint())==null){job.Excluded.Add(key);continue;}
                if(p.Stamina-Math.Max(4,pair.Value.MinutesUntilReady*2+2)<job.Reserve){if(TryWorkFood(job))return;job.MineReturnReason="mine_energy_before_next_stone";return;}
                WorkChild(job,"player.work",new{skill="clear",slot,tiles=new[]{new{x=(int)pair.Key.X,y=(int)pair.Key.Y}}},"mine_stone",key);return;
            }
            if(mine.characters.OfType<Monster>().Any(m=>m.Health>0)){WorkChild(job,"player.combat",new{count=1,min_health=40},"mine_combat");return;}
            job.MineReturnReason="mine_no_reachable_progress_route";return;
        }
        if(job.MineReturnReason.Length>0){StopSemanticWork(job,job.MineReturnReason,job.MineReturnReason is "mine_target_depth_reached" or "native_quest_objective_reached");return;}
        if(job.MineRegion=="skull") {
            if(!p.hasSkullKey&&!Utility.IsPassiveFestivalDay("DesertFestival")){StopSemanticWork(job,"native_skull_key_required_collect_floor_120_chest");return;}
            if(Game1.currentLocation.NameOrUniqueName is not ("Desert" or "SkullCave")) {
                if(job.MineTravelBudget<=0){StopSemanticWork(job,"desert_trip_requires_explicit_travel_budget_or_existing_arrival");return;}
                WorkChild(job,"player.transport",new{route="desert",budget=job.MineTravelBudget,keep_gold=job.MineKeepGold},"mine_bus");job.MineTravelBudget=0;return;
            }
            WorkChild(job,"player.mine_access",new{mode="skull"},"mine_enter");return;
        }
        int stop=job.MineStartLevel>=0?job.MineStartLevel:Math.Min(MineShaft.lowestLevelReached,job.MineTarget-1)/5*5;
        WorkChild(job,"player.mine_access",new{mode=stop>=5?"elevator":"enter",level=stop>=5?stop:1},"mine_enter");
    }
}
