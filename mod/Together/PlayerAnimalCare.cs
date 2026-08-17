using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;

namespace Together;

public sealed partial class PlayerExecutor {
    private string careMode="";
    private FarmAnimal? careAnimal;
    private readonly HashSet<long> caredAnimals=new();
    private int careCount,careSlot;
    private string? careProduce;
    private int careInventory;
    private void StartAnimalCare(JsonElement args) {
        careMode=AgentToolRegistry.Text(args,"mode","pet");careCount=AgentToolRegistry.Number(args,"count",0);
        if(careMode is not ("pet" or "milk" or "shear" or "feed")||careCount is <0 or >100)throw new InvalidOperationException("invalid_animal_care");
        caredAnimals.Clear();careAnimal=null;feedTarget=null;destination="";careSlot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>careMode switch {
            "milk"=>Game1.player.Items[i] is MilkPail,"shear"=>Game1.player.Items[i] is Shears,_=>Game1.player.Items[i] is null or Tool
        },-1);
        if(careSlot<0)throw new InvalidOperationException("animal_care_tool_or_empty_slot_required");
        Current!.phase="care_select";
    }
    private void TickAnimalCare() {
        if(careMode=="feed"){TickFeeding();return;}
        var p=Game1.player;
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("animal_care_menu_requires_review");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!p.CanMove||p.UsingTool)return;
        if(Current!.phase=="care_verify"&&careAnimal!=null) {
            bool complete=careMode=="pet"?careAnimal.wasPet.Value:careAnimal.currentProduce.Value!=careProduce&&p.Items.Where(i=>i?.QualifiedItemId=="(O)"+careProduce).Sum(i=>i.Stack)>careInventory;
            Current.effects.Add(new{kind="native_animal_care",animal=careAnimal.myID.Value,mode=careMode,pet=careAnimal.wasPet.Value,produce_before=careProduce,produce_after=careAnimal.currentProduce.Value,verified=complete});
            if(!complete)throw new InvalidOperationException("native_animal_care_not_verified");
            caredAnimals.Add(careAnimal.myID.Value);Current.completed++;careAnimal=null;Current.phase="care_select";
        }
        if(careCount>0&&Current.completed>=careCount){Finish("succeeded");return;}
        if(careAnimal==null) {
            careAnimal=Game1.getFarm().getAllFarmAnimals().Where(a=>!caredAnimals.Contains(a.myID.Value)&&a.currentLocation!=null&&(careMode=="pet"?!a.wasPet.Value:!string.IsNullOrEmpty(a.currentProduce.Value)&&!a.isBaby()&&a.CanGetProduceWithTool(Game1.player.Items[careSlot] as Tool)))
                .OrderBy(a=>a.currentLocation==Game1.currentLocation?0:1).ThenBy(a=>Vector2.DistanceSquared(a.Position,p.Position)).FirstOrDefault();
            if(careAnimal==null){Finish(careCount==0?"succeeded":"failed",careCount==0?null:"eligible_animals_exhausted");return;}
            destination=careAnimal.currentLocation.NameOrUniqueName;edge=null;
        }
        if(Game1.timeOfDay>=1900)throw new InvalidOperationException("animals_sleeping_finish_next_day");
        if(careAnimal.currentLocation.NameOrUniqueName!=destination){destination=careAnimal.currentLocation.NameOrUniqueName;edge=null;StopWalk();}
        if(Game1.currentLocation.NameOrUniqueName!=destination){Current.phase="care_travel";Travel();return;}
        if(Math.Abs(p.TilePoint.X-careAnimal.TilePoint.X)+Math.Abs(p.TilePoint.Y-careAnimal.TilePoint.Y)>1) {
            if(DateTime.UtcNow>=nextInteraction){nextInteraction=DateTime.UtcNow.AddMilliseconds(500);Walk(Approach(careAnimal.TilePoint,true));}else if(ownedController!=null)MonitorWalk();Current.phase="care_approach";return;
        }
        StopWalk();p.CurrentToolIndex=careSlot;Face(careAnimal.TilePoint);careProduce=careAnimal.currentProduce.Value;careInventory=p.Items.Where(i=>i?.QualifiedItemId=="(O)"+careProduce).Sum(i=>i.Stack);
        if(careMode=="pet")careAnimal.pet(p);
        else {
            if(p.Stamina<20||p.Items.Count(i=>i==null)<1)throw new InvalidOperationException("animal_harvest_supply_required");
            p.lastClick=careAnimal.Position+new Vector2(32);p.BeginUsingTool();if(!p.UsingTool)throw new InvalidOperationException("native_animal_tool_did_not_start");
        }
        Current.phase="care_verify";
    }
}
