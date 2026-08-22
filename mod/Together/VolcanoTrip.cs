using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Tools;

namespace Together;
public sealed partial class ModEntry {
    private void TickVolcanoTrip(SemanticJob job) {
        var p=Game1.player;
        if(Game1.currentLocation is Caldera){job.completed=10;StopSemanticWork(job,"native_caldera_reached",true);return;}
        if((Game1.timeOfDay>=job.Until||job.Attempts>=1000||(DateTime.UtcNow-job.Started).TotalMinutes>35)&&job.MineReturnReason.Length==0)job.MineReturnReason="volcano_time_or_execution_budget_return";
        if((p.health<65||p.Stamina<job.Reserve+10)&&job.MineReturnReason.Length==0){if(TryWorkFood(job))return;job.MineReturnReason="volcano_supply_reserve_return";}
        if(p.Items.Count(i=>i==null)<2&&job.MineReturnReason.Length==0)job.MineReturnReason="volcano_inventory_return";
        if(Game1.currentLocation is VolcanoDungeon volcano) {
            int level=volcano.level.Value;job.completed=Math.Max(job.completed,level);
            if(job.MineFloor!=level){job.MineFloor=level;job.RefillTile=null;job.VolcanoFailures=0;}
            if(level>=job.MineTarget&&job.MineReturnReason.Length==0){StopSemanticWork(job,"native_volcano_target_floor_reached",true);return;}
            var nearby=volcano.characters.OfType<Monster>().Any(m=>m.Health>0&&Vector2.DistanceSquared(m.Position,p.Position)<256*256);
            if(nearby&&p.health>=50){WorkChild(job,"player.combat",new{count=1,min_health=40},"volcano_combat");return;}
            if(job.MineReturnReason.Length>0){WorkChild(job,"player.volcano_step",new{mode="retreat"},"volcano_retreat");return;}
            int canSlot=WorkSlot(i=>i is WateringCan);
            if(canSlot<0){job.MineReturnReason="volcano_missing_watering_can";return;}
            var can=(WateringCan)p.Items[canSlot];
            if(level is 0 or 5&&can.WaterLeft<can.waterCanMax){RefillWork(job,canSlot,can);return;}
            if(can.WaterLeft==0){job.MineReturnReason="volcano_water_exhausted_return_over_cooled_path";return;}
            if(PlayerExecutor.TreasureChests(volcano).Any(c=>!c.Chest.dropContents.Value)){WorkChild(job,"player.treasure",new{},"volcano_treasure");return;}
            WorkChild(job,"player.volcano_step",new{mode="advance"},"volcano_step");return;
        }
        if(job.MineReturnReason.Length>0){StopSemanticWork(job,job.MineReturnReason);return;}
        if(Game1.currentLocation is not IslandLocation) {
            if(job.MineTravelBudget<=0){StopSemanticWork(job,"island_trip_requires_explicit_ticket_budget_or_arrival");return;}
            WorkChild(job,"player.transport",new{route="island",budget=job.MineTravelBudget,keep_gold=job.MineKeepGold},"volcano_boat");job.MineTravelBudget=0;return;
        }
        WorkChild(job,"player.volcano_step",new{mode="enter"},"volcano_enter");
    }
}
