using Microsoft.Xna.Framework;
using StardewValley;

namespace Together;
public sealed partial class PlayerExecutor {
    private Point? feedTarget;
    private bool feedHopper;
    private void TickFeeding() {
        var p=Game1.player;
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("feeding_menu_requires_review");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!p.CanMove||p.UsingTool)return;
        if(careCount>0&&Current!.completed>=careCount){Finish("succeeded");return;}
        int Missing(AnimalHouse h)=>Math.Max(0,h.animalsThatLiveHere.Count-h.objects.Pairs.Count(o=>o.Value.QualifiedItemId=="(O)178"&&h.doesTileHaveProperty((int)o.Key.X,(int)o.Key.Y,"Trough","Back")!=null));
        var house=Game1.getFarm().buildings.Select(b=>b.GetIndoors()).OfType<AnimalHouse>().Where(h=>Missing(h)>0).OrderBy(h=>h==Game1.currentLocation?0:1).FirstOrDefault();
        if(house==null){Finish(careCount==0?"succeeded":"failed",careCount==0?null:"all_resident_animals_already_have_feed");return;}
        if(destination!=house.NameOrUniqueName){destination=house.NameOrUniqueName;edge=null;feedTarget=null;StopWalk();}
        if(Game1.currentLocation!=house){Current!.phase="feeding_travel";Travel();return;}
        if(feedTarget.HasValue) {
            if(!AtWalkTarget){MonitorWalk();return;}StopWalk();var at=feedTarget.Value;Adjacent(at);Face(at);
            if(feedHopper) {
                p.CurrentToolIndex=careSlot;int before=p.Items.Where(i=>i?.QualifiedItemId=="(O)178").Sum(i=>i.Stack),silo=house.GetRootLocation().piecesOfHay.Value;
                if(!house.objects.TryGetValue(at.ToVector2(),out var hopper)||hopper.QualifiedItemId!="(BC)99"||silo<=0)throw new InvalidOperationException("feed_hopper_or_silo_unavailable");
                hopper.checkForAction(p);
                int gained=p.Items.Where(i=>i?.QualifiedItemId=="(O)178").Sum(i=>i.Stack)-before;
                Current!.effects.Add(new{kind="native_hopper_withdraw",gained,silo_before=silo,silo_after=house.GetRootLocation().piecesOfHay.Value});
                if(gained<=0)throw new InvalidOperationException("hopper_withdraw_not_verified");
            } else {
                int slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]?.QualifiedItemId=="(O)178",-1);
                if(slot<0)throw new InvalidOperationException("hay_disappeared");p.CurrentToolIndex=slot;var hay=p.ActiveObject!;int before=hay.Stack;
                ValidateConsumption?.Invoke(new Dictionary<Item,int>{{hay,1}},"","");
                Game1.tryToCheckAt(at.ToVector2(),p);
                bool placed=house.objects.TryGetValue(at.ToVector2(),out var item)&&item.QualifiedItemId=="(O)178"&&before-(p.Items[slot]?.QualifiedItemId=="(O)178"?p.Items[slot].Stack:0)==1;
                Current!.effects.Add(new{kind="native_trough_feed",location=destination,tile=at,verified=placed});
                if(!placed)throw new InvalidOperationException("trough_feeding_not_verified");Current.completed++;
            }
            feedTarget=null;Current!.phase="feeding_select";return;
        }
        var candidates=new List<Point>();feedHopper=!p.Items.Any(i=>i?.QualifiedItemId=="(O)178");
        if(feedHopper) {
            if(house.GetRootLocation().piecesOfHay.Value<=0)throw new InvalidOperationException("hay_supply_empty_purchase_or_cut_grass");
            if(p.freeSpotsInInventory()==0)throw new InvalidOperationException("hay_inventory_space_required");
            candidates.AddRange(house.objects.Pairs.Where(o=>o.Value.QualifiedItemId=="(BC)99").Select(o=>o.Key.ToPoint()));
        } else for(int y=0;y<house.Map.Layers[0].LayerHeight;y++)for(int x=0;x<house.Map.Layers[0].LayerWidth;x++)
            if(house.doesTileHaveProperty(x,y,"Trough","Back")!=null&&!house.objects.ContainsKey(new Vector2(x,y)))candidates.Add(new(x,y));
        foreach(var at in candidates.OrderBy(t=>Vector2.DistanceSquared(t.ToVector2(),p.Tile))) {
            try{var stand=Approach(at,true);feedTarget=at;Walk(stand);Current!.phase="feeding_walk";return;}catch(InvalidOperationException){ }
        }
        throw new InvalidOperationException("no_reachable_hopper_or_trough");
    }
}
