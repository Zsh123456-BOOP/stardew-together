using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.GameData.Machines;

namespace Together;

public sealed partial class PlayerExecutor {
    private string machineMode="",machineKind="",machineInput="",machineGoal="",machineOutput="";
    private int machineCount;
    private bool machineUnreachable;
    private Point? machineTile;
    private readonly HashSet<Point> machinesVisited=new();
    private void StartMachines(JsonElement args) {
        machineMode=AgentToolRegistry.Text(args,"mode","collect");machineKind=AgentToolRegistry.Text(args,"machine","");machineInput=AgentToolRegistry.Text(args,"item","");
        machineGoal=AgentToolRegistry.Text(args,"goal_id","");machineOutput=AgentToolRegistry.Text(args,"output","");machineCount=AgentToolRegistry.Number(args,"count",0);
        if(machineMode is not ("collect" or "load")||machineCount is <0 or >100||machineMode=="load"&&machineInput.Length==0)throw new InvalidOperationException("invalid_machine_work");
        destination=AgentToolRegistry.Text(args,"location",origin);if(Game1.getLocationFromName(destination)==null)throw new InvalidOperationException("unknown_machine_location");
        machinesVisited.Clear();machineTile=null;machineUnreachable=false;Current!.phase="machine_select";
    }
    private void TickMachines() {
        var p=Game1.player;
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("machine_menu_requires_review");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!p.CanMove||p.UsingTool)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Current!.phase="machine_travel";Travel();return;}
        if(machineCount>0&&Current!.completed>=machineCount){Finish("succeeded");return;}
        if(machineTile==null) {
            var candidates=Game1.currentLocation.objects.Pairs.Where(pair=>!machinesVisited.Contains(pair.Key.ToPoint())&&pair.Value.GetMachineData()!=null&&(machineKind.Length==0||pair.Value.QualifiedItemId==machineKind)&&
                (machineMode=="collect"?pair.Value.readyForHarvest.Value&&pair.Value.heldObject.Value!=null:pair.Value.heldObject.Value==null)).OrderBy(pair=>Vector2.DistanceSquared(pair.Key,p.Tile));
            foreach(var pair in candidates) {
                try{var stand=Approach(pair.Key.ToPoint(),true);machineTile=pair.Key.ToPoint();Walk(stand);Current!.phase="machine_walk";break;}catch(InvalidOperationException){machineUnreachable=true;machinesVisited.Add(pair.Key.ToPoint());}
            }
            if(machineTile==null){bool complete=machineCount==0&&!machineUnreachable;Finish(complete?"succeeded":"failed",complete?null:machineUnreachable?"some_machine_targets_unreachable":"eligible_machines_exhausted");return;}
        }
        if(p.TilePoint!=target){MonitorWalk();return;}StopWalk();var tile=machineTile.Value;Adjacent(tile);Face(tile);
        if(!Game1.currentLocation.objects.TryGetValue(tile.ToVector2(),out var machine)||machine.GetMachineData() is not {} data)throw new InvalidOperationException("machine_changed");
        if(machineMode=="collect") {
            var output=machine.heldObject.Value;
            if(output==null||!machine.readyForHarvest.Value)throw new InvalidOperationException("machine_output_changed");
            if(!p.couldInventoryAcceptThisItem(output))throw new InvalidOperationException("machine_output_inventory_full");
            string id=output.QualifiedItemId;int before=p.Items.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack);
            machine.checkForAction(p);int gained=p.Items.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack)-before;
            Current!.effects.Add(new{kind="native_machine_collection",location=destination,tile,item=id,gained});
            if(gained<=0)throw new InvalidOperationException("machine_collection_not_verified");
        } else {
            var input=p.Items.FirstOrDefault(i=>i?.QualifiedItemId==machineInput)??throw new InvalidOperationException("machine_input_missing");
            if(!MachineDataUtility.TryGetMachineOutputRule(machine,data,MachineOutputTrigger.ItemPlacedInMachine,input,p,Game1.currentLocation,out var rule,out var trigger,out _,out _))throw new InvalidOperationException("machine_input_rule_or_count_unavailable");
            if(!MachineDataUtility.HasAdditionalRequirements(p.Items,data.AdditionalConsumedItems,out _))throw new InvalidOperationException("machine_fuel_or_additional_ingredients_missing");
            var consumption=new Dictionary<Item,int>();
            if(trigger.RequiredCount>0)consumption[input]=trigger.RequiredCount;
            foreach(var extra in data.AdditionalConsumedItems??new()) {
                int need=extra.RequiredCount;
                foreach(var item in p.Items.Where(i=>i!=null&&(i.ItemId==extra.ItemId||i.QualifiedItemId==extra.ItemId))) {
                    int take=Math.Min(need,item.Stack-consumption.GetValueOrDefault(item));if(take<=0)continue;consumption[item]=consumption.GetValueOrDefault(item)+take;need-=take;if(need==0)break;
                }
                if(need>0)throw new InvalidOperationException("machine_material_requirements_overlap");
            }
            ValidateConsumption?.Invoke(consumption,machineGoal,machineOutput);
            var before=consumption.ToDictionary(x=>x.Key,x=>x.Key.Stack);
            // The native machine method consumes its own inputs/fuel and starts its
            // real timer. No remote chest injection and no predicted output grant.
            if(!machine.performObjectDropInAction(input,false,p))throw new InvalidOperationException("native_machine_load_rejected");
            bool paid=consumption.All(x=>before[x.Key]-Math.Max(0,x.Key.Stack)>=x.Value);
            Current!.effects.Add(new{kind="native_machine_loaded",location=destination,tile,input=machineInput,rule=machine.lastOutputRuleId.Value,minutes=machine.MinutesUntilReady,output=machine.heldObject.Value?.QualifiedItemId,consumption_verified=paid,product_ready=machine.readyForHarvest.Value});
            if(!paid||machine.heldObject.Value==null)throw new InvalidOperationException("machine_start_not_verified");
        }
        machinesVisited.Add(tile);machineTile=null;Current!.completed++;Current.phase="machine_select";
    }
}
