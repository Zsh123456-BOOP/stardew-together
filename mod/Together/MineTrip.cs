using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Tools;

namespace Together;
public sealed partial class ModEntry {
    private void TickMineTrip(SemanticJob job) {
        var p=Game1.player;
        if((job.Attempts>=512||(DateTime.UtcNow-job.Started).TotalMinutes>25)&&job.MineReturnReason.Length==0)job.MineReturnReason="mine_execution_budget_return";
        if(Game1.timeOfDay>=job.Until&&job.MineReturnReason.Length==0)job.MineReturnReason="mine_time_reserve_return";
        if((p.health<45||p.Stamina<job.Reserve+6)&&job.MineReturnReason.Length==0) {
            if(TryWorkFood(job))return;job.MineReturnReason="mine_supply_reserve_return";
        }
        if(p.Items.Count(i=>i==null)<2&&job.MineReturnReason.Length==0)job.MineReturnReason="mine_inventory_return";
        if(Game1.currentLocation is MineShaft mine) {
            job.completed=Math.Max(job.completed,mine.mineLevel);
            if(mine.mineLevel>=job.MineTarget&&job.MineReturnReason.Length==0)job.MineReturnReason="mine_target_depth_reached";
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
        if(job.MineReturnReason.Length>0){StopSemanticWork(job,job.MineReturnReason,job.MineReturnReason=="mine_target_depth_reached");return;}
        int stop=Math.Min(MineShaft.lowestLevelReached,job.MineTarget-1)/5*5;
        WorkChild(job,"player.mine_access",new{mode=stop>=5?"elevator":"enter",level=stop>=5?stop:1},"mine_enter");
    }
}
