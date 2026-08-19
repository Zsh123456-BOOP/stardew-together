using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;

namespace Together;
public sealed partial class PlayerExecutor {
    public Func<string,Point,bool>? PlacementProtected {get;set;}
    private string facilityItem="",facilityGoal="";
    private Point? facilitySite;
    private void StartFacilityPlacement(JsonElement args) {
        facilityItem=AgentToolRegistry.Text(args,"item");facilityGoal=AgentToolRegistry.Text(args,"goal_id");destination=AgentToolRegistry.Text(args,"location","Farm");
        if(Game1.getLocationFromName(destination)==null)throw new InvalidOperationException("unknown_facility_location");
        if(!Game1.player.Items.Any(i=>i is StardewValley.Object {bigCraftable.Value:true}&&i.QualifiedItemId==facilityItem))throw new InvalidOperationException("carried_placeable_facility_required");
        facilitySite=null;Current!.phase="facility_travel";
    }
    private void TickFacilityPlacement() {
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("facility_menu_interrupted");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        var location=Game1.currentLocation;
        int slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i]?.QualifiedItemId==facilityItem,-1);
        if(slot<0)throw new InvalidOperationException("facility_inventory_changed");
        var item=Game1.player.Items[slot] as StardewValley.Object??throw new InvalidOperationException("facility_item_type_changed");
        if(facilitySite==null) {
            var anchors=Exits(location).Select(e=>new FarmCell(e.X,e.Y)).ToList();
            foreach(var b in location.buildings)if(b.humanDoor.Value.X>=0)anchors.Add(new(b.tileX.Value+b.humanDoor.Value.X,b.tileY.Value+b.humanDoor.Value.Y+1));
            foreach(var entry in location.objects.Pairs.Where(e=>e.Value.bigCraftable.Value))try{var stand=Approach(entry.Key.ToPoint(),true);anchors.Add(new(stand.X,stand.Y));}catch(InvalidOperationException){}
            var grid=new List<LayoutCell>();for(int y=0;y<location.Map.Layers[0].LayerHeight;y++)for(int x=0;x<location.Map.Layers[0].LayerWidth;x++) {
                var tile=new Point(x,y);bool passable=Passable(location,tile);grid.Add(new(new(x,y),false,passable,false,false,false,false,0));
                if(PlacementProtected?.Invoke(destination,tile)==true&&passable)anchors.Add(new(x,y));
            }
            var start=new FarmCell(Game1.player.TilePoint.X,Game1.player.TilePoint.Y);
            foreach(var cell in grid.Where(c=>c.Passable&&c.Tile!=start).OrderBy(c=>Math.Abs(c.Tile.X-start.X)+Math.Abs(c.Tile.Y-start.Y)).ThenBy(c=>c.Tile.Y).ThenBy(c=>c.Tile.X)) {
                var tile=new Point(cell.Tile.X,cell.Tile.Y);var vector=tile.ToVector2();
                if(anchors.Contains(cell.Tile)||PlacementProtected?.Invoke(destination,tile)==true||location.objects.ContainsKey(vector)||location.terrainFeatures.ContainsKey(vector)||location.doesTileHaveProperty(tile.X,tile.Y,"Action","Buildings")!=null||location.doesTileHaveProperty(tile.X,tile.Y,"TouchAction","Back")!=null||!item.canBePlacedHere(location,vector))continue;
                try {
                    Point stand=Approach(tile,true);
                    if(!FarmLayout.KeepsAccess(grid,start,anchors,new[]{cell.Tile},new[]{new FarmCell(stand.X,stand.Y)}))continue;
                    Walk(stand);facilitySite=tile;Current!.phase="facility_walk";break;
                }catch(InvalidOperationException){}
            }
            if(facilitySite==null)throw new InvalidOperationException("no_clear_facility_site_preserving_access");
        }
        if(Game1.player.TilePoint!=target){MonitorWalk();return;}
        StopWalk();var selected=facilitySite.Value;var at=selected.ToVector2();Adjacent(selected);Face(selected);
        if(location.objects.ContainsKey(at)||location.terrainFeatures.ContainsKey(at)||PlacementProtected?.Invoke(destination,selected)==true||!item.canBePlacedHere(location,at))throw new InvalidOperationException("facility_site_changed");
        ValidateConsumption?.Invoke(new Dictionary<Item,int>{{item,1}},facilityGoal,facilityItem);Game1.player.CurrentToolIndex=slot;Game1.player.netItemStowed.Value=false;
        int before=Game1.player.Items.Where(i=>i?.QualifiedItemId==facilityItem).Sum(i=>i.Stack);
        if(!Utility.tryToPlaceItem(location,item,selected.X*64,selected.Y*64)||!location.objects.TryGetValue(at,out var placed)||placed.QualifiedItemId!=facilityItem||before-Game1.player.Items.Where(i=>i?.QualifiedItemId==facilityItem).Sum(i=>i.Stack)!=1)throw new InvalidOperationException("native_facility_placement_not_verified");
        Current!.effects.Add(new{kind="native_facility_placed",item=facilityItem,location=destination,x=selected.X,y=selected.Y,consumed=1});Current.completed=1;Finish("succeeded");
    }
}
