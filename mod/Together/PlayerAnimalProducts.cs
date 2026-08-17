using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace Together;
public sealed partial class PlayerExecutor {
    private Point? careCollectTile;
    private Chest? careCollectChest;
    private readonly HashSet<string> careCollected=new();
    private void TickAnimalProducts() {
        if(careCollectChest!=null&&Game1.activeClickableMenu is ItemGrabMenu menu) {
            if(!ReferenceEquals(menu.ItemsToGrabMenu.actualInventory,careCollectChest.GetItemsForPlayer()))throw new InvalidOperationException("auto_grabber_inventory_changed");
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(180);
            if(menu.heldItem!=null){menu.heldItem=Game1.player.addItemToInventory(menu.heldItem);if(menu.heldItem!=null)throw new InvalidOperationException("animal_products_inventory_full");}
            var source=menu.ItemsToGrabMenu.actualInventory;
            int slot=Enumerable.Range(0,Math.Min(source.Count,menu.ItemsToGrabMenu.inventory.Count)).FirstOrDefault(i=>source[i]!=null,-1);
            if(slot<0||careCount>0&&Current!.completed>=careCount) {
                if(menu.readyToClose()){menu.exitThisMenu();careCollected.Add(destination+":"+careCollectTile);careCollectTile=null;careCollectChest=null;}
                return;
            }
            var item=source[slot];if(!Game1.player.couldInventoryAcceptThisItem(item))throw new InvalidOperationException("animal_products_inventory_full");
            string id=item.QualifiedItemId;int beforeSource=source.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack),before=Game1.player.Items.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack);
            var bounds=menu.ItemsToGrabMenu.inventory[slot].bounds;menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
            int gained=Game1.player.Items.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack)-before,removed=beforeSource-source.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack);
            if(gained<=0||removed!=gained)throw new InvalidOperationException("auto_grabber_transfer_not_verified");
            Current!.effects.Add(new{kind="native_auto_grabber_collection",location=destination,tile=careCollectTile,item=id,gained});Current.completed++;return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("animal_products_menu_requires_review");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(careCount>0&&Current!.completed>=careCount){Finish("succeeded");return;}
        if(careCollectTile==null) {
            var farm=Game1.getFarm();var locations=new[]{(GameLocation)farm}.Concat(farm.buildings.Select(b=>b.GetIndoors()).Where(l=>l is AnimalHouse));
            var produce=farm.getAllFarmAnimals().Select(a=>a.GetAnimalData()).Where(d=>d!=null).SelectMany(d=>(d.ProduceItemIds??new()).Concat(d.DeluxeProduceItemIds??new())).Select(p=>ItemRegistry.QualifyItemId(p.ItemId)).ToHashSet();
            var candidate=locations.SelectMany(l=>l.objects.Pairs.Select(o=>(Location:l,Tile:o.Key,Object:o.Value))).Where(x=>!careCollected.Contains(x.Location.NameOrUniqueName+":"+x.Tile.ToPoint())&&
                (x.Object.QualifiedItemId=="(BC)165"&&x.Object.heldObject.Value is Chest c&&!c.isEmpty()||!x.Object.bigCraftable.Value&&x.Object.IsSpawnedObject&&produce.Contains(x.Object.QualifiedItemId)))
                .OrderBy(x=>x.Location==Game1.currentLocation?0:1).ThenBy(x=>Vector2.DistanceSquared(x.Tile,Game1.player.Tile)).FirstOrDefault();
            if(candidate.Object==null){Finish(careCount==0?"succeeded":"failed",careCount==0?null:"eligible_animal_products_exhausted");return;}
            careCollectTile=candidate.Tile.ToPoint();careCollectChest=candidate.Object.QualifiedItemId=="(BC)165"?candidate.Object.heldObject.Value as Chest:null;destination=candidate.Location.NameOrUniqueName;Current!.phase="animal_products_travel";
        }
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        var at=careCollectTile.Value;
        if(Current!.phase!="animal_products_walk"&&Current.phase!="animal_products_opening"){Walk(Approach(at,true));Current.phase="animal_products_walk";}
        if(Game1.player.TilePoint!=target){MonitorWalk();return;}StopWalk();Face(at);Adjacent(at);
        if(Current.phase=="animal_products_opening")return;
        if(!Game1.currentLocation.objects.TryGetValue(at.ToVector2(),out var product))throw new InvalidOperationException("animal_product_changed");
        if(careCollectChest==null&&!Game1.player.couldInventoryAcceptThisItem(product))throw new InvalidOperationException("animal_products_inventory_full");
        Game1.player.CurrentToolIndex=careSlot;string qid=product.QualifiedItemId;int beforeBag=Game1.player.Items.Where(i=>i?.QualifiedItemId==qid).Sum(i=>i.Stack);
        if(!Game1.tryToCheckAt(at.ToVector2(),Game1.player))throw new InvalidOperationException("native_animal_product_interaction_rejected");
        if(careCollectChest!=null){Current.phase="animal_products_opening";return;}
        int added=Game1.player.Items.Where(i=>i?.QualifiedItemId==qid).Sum(i=>i.Stack)-beforeBag;
        if(added<=0||Game1.currentLocation.objects.ContainsKey(at.ToVector2()))throw new InvalidOperationException("native_animal_product_pickup_not_verified");
        Current.effects.Add(new{kind="native_animal_floor_product",location=destination,tile=at,item=qid,gained=added});Current.completed++;careCollected.Add(destination+":"+at);careCollectTile=null;
    }
}
