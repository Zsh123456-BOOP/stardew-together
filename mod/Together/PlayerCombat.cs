using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Monsters;
using StardewValley.Tools;

namespace Together;
public sealed partial class PlayerExecutor {
    private int combatRequested,combatMinHealth,combatWeaponSlot,combatSwings;
    private uint combatBaseline;
    private StardewValley.Quests.SlayMonsterQuest? combatQuest;
    private int combatQuestBefore;
    private StardewValley.SpecialOrders.Objectives.SlayObjective? combatOrder;
    private int combatOrderBefore;
    private Monster? combatTarget;
    private int combatTargetHealth;
    private Point combatTargetTile;
    private void StartCombat(JsonElement args) {
        string questId=AgentToolRegistry.Text(args,"quest_id");combatQuest=null;
        if(questId.Length>0){combatQuest=NativeQuestIdentity.Find(questId) as StardewValley.Quests.SlayMonsterQuest??throw new InvalidOperationException("active_slay_quest_required");combatQuestBefore=combatQuest.numberKilled.Value;}
        string orderId=AgentToolRegistry.Text(args,"order_id");combatOrder=null;
        if(orderId.Length>0) {
            var order=OrderRules.Find(orderId)??throw new InvalidOperationException("active_combat_order_required");int index=AgentToolRegistry.Number(args,"objective",-1);
            if(questId.Length>0||index<0||index>=order.objectives.Count||order.objectives[index] is not StardewValley.SpecialOrders.Objectives.SlayObjective slay||slay.failOnCompletion.Value)throw new InvalidOperationException("valid_slay_order_index_required");
            combatOrder=slay;combatOrderBefore=slay.GetCount();
        }
        combatRequested=AgentToolRegistry.Number(args,"count",0);combatMinHealth=AgentToolRegistry.Number(args,"min_health",35);
        if(combatRequested is <0 or >50||combatMinHealth is <20 or >200)throw new InvalidOperationException("invalid_combat_limits");
        combatWeaponSlot=Enumerable.Range(0,Game1.player.Items.Count).Where(i=>Game1.player.Items[i] is MeleeWeapon w&&!w.isScythe()).OrderByDescending(i=>((MeleeWeapon)Game1.player.Items[i]).maxDamage.Value).FirstOrDefault(-1);
        if(combatWeaponSlot<0)throw new InvalidOperationException("melee_weapon_required");
        PlayerSelection.Set(Game1.player,combatWeaponSlot);combatBaseline=Game1.player.stats.MonstersKilled;combatTarget=null;combatSwings=0;Current!.phase="combat_select";
    }
    private void TickCombat() {
        if(Game1.currentLocation.NameOrUniqueName!=origin)throw new InvalidOperationException("combat_location_changed");
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("combat_interrupted_by_menu");
        var p=Game1.player;
        Current!.completed=combatOrder!=null?Math.Max(0,combatOrder.GetCount()-combatOrderBefore):combatQuest!=null?Math.Max(0,combatQuest.numberKilled.Value-combatQuestBefore):(int)Math.Max(0,(long)p.stats.MonstersKilled-combatBaseline);
        if(combatQuest?.completed.Value==true){Current.effects.Add(new{kind="native_quest_completed",quest=NativeQuestIdentity.Id(combatQuest)});Finish("succeeded");return;}
        if(p.health<combatMinHealth||Game1.timeOfDay>=2300)throw new InvalidOperationException("combat_health_or_time_reserve_retreat_required");
        if(p.UsingTool||!p.CanMove)return;
        if(Current.phase=="combat_swing") {
            int after=combatTarget?.Health??0;
            Current.effects.Add(new{kind="native_melee",target=combatTarget?.Name,health_before=combatTargetHealth,health_after=after,farmer_kills=p.stats.MonstersKilled});
            if(Current.effects.Count>160)Current.effects.RemoveAt(0);
            if(combatTarget!=null&&after>=combatTargetHealth&&++combatSwings>=12)throw new InvalidOperationException("combat_no_damage_change_tactics");
            if(after<combatTargetHealth)combatSwings=0;Current.phase="combat_select";
        }
        if(combatRequested>0&&Current.completed>=combatRequested){Finish("succeeded");return;}
        var monsters=Game1.currentLocation.characters.OfType<Monster>().Where(m=>m.Health>0).OrderBy(m=>Vector2.DistanceSquared(m.Position,p.Position)<128*128?0:(combatQuest?.OnMonsterSlain(Game1.currentLocation,m,false,false,true)==true||combatOrder!=null&&OrderRules.Matches(combatOrder,m))?1:2).ThenBy(m=>Vector2.DistanceSquared(m.Position,p.Position)).ToArray();
        if(monsters.Length==0){Finish(combatRequested==0?"succeeded":"failed",combatRequested==0?null:"current_area_clear_before_requested_kills");return;}
        if(combatTarget==null||!monsters.Contains(combatTarget)){combatTarget=monsters[0];combatSwings=0;StopWalk();}
        if(Math.Abs(combatTarget.TilePoint.X-p.TilePoint.X)+Math.Abs(combatTarget.TilePoint.Y-p.TilePoint.Y)>1) {
            if(DateTime.UtcNow<nextInteraction){if(ownedController!=null)MonitorWalk();return;}
            nextInteraction=DateTime.UtcNow.AddMilliseconds(350);
            if(ownedController==null||combatTargetTile!=combatTarget.TilePoint){combatTargetTile=combatTarget.TilePoint;Walk(Approach(combatTargetTile,true));}
            else MonitorWalk();Current.phase="combat_approach";return;
        }
        StopWalk();PlayerSelection.Set(p,combatWeaponSlot);
        if(p.CurrentTool is not MeleeWeapon weapon||weapon.isScythe())throw new InvalidOperationException("combat_weapon_changed");
        p.faceGeneralDirection(combatTarget.Position);p.lastClick=combatTarget.Position+new Vector2(32);
        combatTargetHealth=combatTarget.Health;p.BeginUsingTool();
        if(!p.UsingTool)throw new InvalidOperationException("native_melee_did_not_start");
        Current.phase="combat_swing";
    }
}
