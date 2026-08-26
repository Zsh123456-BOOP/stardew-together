using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private int donationBundle,donationArea,donationBudget,donationKeep;
    private bool donationClosing;
    private Point? donationNote;
    private readonly HashSet<int> donationBlocked=new();
    private void StartBundle(JsonElement args) {
        donationBundle=AgentToolRegistry.Number(args,"bundle",-1);donationBudget=AgentToolRegistry.Number(args,"budget",0);donationKeep=AgentToolRegistry.Number(args,"keep_gold",500);
        if(donationBudget<0||donationKeep<0)throw new InvalidOperationException("invalid_bundle_budget");
        string? key=Game1.netWorldState.Value.BundleData.Keys.FirstOrDefault(k=>k.Split('/').Last()==donationBundle.ToString());
        if(key==null)throw new InvalidOperationException("native_bundle_not_found");
        donationArea=CommunityCenter.getAreaNumberFromName(key.Split('/')[0]);
        if(donationArea is <0 or >6)throw new InvalidOperationException("special_bundle_location_requires_adapter");
        destination=donationArea==6?"AbandonedJojaMart":"CommunityCenter";donationClosing=false;donationNote=null;donationBlocked.Clear();Current!.phase="bundle_travel";
        if(Game1.activeClickableMenu is JunimoNoteMenu menu&&(menu.fromGameMenu||menu.fromThisMenu||menu.whichArea!=donationArea||menu.heldItem!=null||menu.partialDonationItem!=null))throw new InvalidOperationException("physical_matching_bundle_menu_required");
    }
    private void TickBundle() {
        if(Game1.activeClickableMenu is JunimoNoteMenu menu) {TickBundleMenu(menu);return;}
        if(donationClosing&&Game1.activeClickableMenu is ItemGrabMenu {context:JunimoNoteMenu origin} rewards&&origin.whichArea==donationArea) {
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(200);
            var ccRewards=Game1.RequireLocation<CommunityCenter>("CommunityCenter");
            var pending=rewards.ItemsToGrabMenu.actualInventory.Where(i=>i!=null).Select(i=>i.SpecialVariable).Distinct().Where(id=>ccRewards.bundleRewards.TryGetValue(id,out bool available)&&available).ToArray();
            var step=NativeRewards.Step(rewards);Current!.effects.Add(step.Evidence);
            foreach(int id in pending.Where(id=>!ccRewards.bundleRewards[id]))Current.effects.Add(new{kind="native_bundle_reward_claimed",bundle=id});
            return;
        }
        if(donationClosing) {
            if(Game1.activeClickableMenu==null){Finish("succeeded");return;}
            throw new InvalidOperationException("bundle_exit_requires_review");
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("bundle_route_menu_requires_review");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        var cc=Game1.currentLocation;
        if(donationNote==null) {
            Point note;
            if(donationArea==6) {
                var layer=cc.Map.GetLayer("Buildings");Point? found=null;
                for(int y=0;y<layer.LayerHeight&&found==null;y++)for(int x=0;x<layer.LayerWidth;x++) {
                    int index=cc.getTileIndexAt(x,y,"Buildings");if(index==1799||index is >=1824 and <=1833){found=new Point(x,y);break;}
                }
                note=found??throw new InvalidOperationException("missing_bundle_note_not_visible");
            } else {
                var method=typeof(CommunityCenter).GetMethod("getNotePosition",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)??throw new InvalidOperationException("native_note_schema_changed");
                note=(Point)method.Invoke(cc,new object[]{donationArea})!;
            }
            int tile=cc.getTileIndexAt(note.X,note.Y,"Buildings");
            if(tile!=1799&&(tile<1824||tile>1833))throw new InvalidOperationException("bundle_note_not_visible_unlock_or_claim_rewards");
            bool reached=false;foreach(var stand in new[]{new Point(note.X,note.Y+1),new Point(note.X-1,note.Y),new Point(note.X+1,note.Y),new Point(note.X,note.Y-1)}) {
                if(!Passable(cc,stand))continue;try{Walk(stand);reached=true;break;}catch(InvalidOperationException){}
            }
            if(!reached)throw new InvalidOperationException("bundle_note_unreachable");donationNote=note;Current!.phase="bundle_walk";
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Face(donationNote.Value);Adjacent(donationNote.Value);
        if(Current!.phase=="bundle_opening")return;
        if(!Game1.tryToCheckAt(donationNote.Value.ToVector2(),Game1.player))throw new InvalidOperationException("native_bundle_note_rejected");Current.phase="bundle_opening";
    }
    private void TickBundleMenu(JunimoNoteMenu menu) {
        if(menu.fromGameMenu||menu.fromThisMenu||menu.scrambledText||menu.whichArea!=donationArea)throw new InvalidOperationException("bundle_location_or_translation_unavailable");
        if(!JunimoNoteMenu.canClick||JunimoNoteMenu.screenSwipe!=null||DateTime.UtcNow<nextInteraction)return;
        nextInteraction=DateTime.UtcNow.AddMilliseconds(200);
        if(donationClosing) {
            if(menu.heldItem!=null){menu.heldItem=menu.inventory.tryToAddItem(menu.heldItem);if(menu.heldItem!=null)throw new InvalidOperationException("bundle_remainder_inventory_full");}
            if(menu.partialDonationItem!=null)throw new InvalidOperationException("bundle_partial_items_require_recovery");
            if(menu.specificBundlePage){var back=menu.backButton?.bounds??menu.upperRightCloseButton.bounds;menu.receiveLeftClick(back.Center.X,back.Center.Y);return;}
            if(menu.presentButton!=null) {var bounds=menu.presentButton.bounds;menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);return;}
            if(menu.readyToClose())menu.exitThisMenu();return;
        }
        if(!menu.specificBundlePage) {
            var bundle=menu.bundles.FirstOrDefault(b=>b.bundleIndex==donationBundle)??throw new InvalidOperationException("bundle_not_in_observed_area");
            if(bundle.complete){Current!.effects.Add(new{kind="bundle_already_complete",bundle=donationBundle});donationClosing=true;return;}
            if(!bundle.canBeClicked())return;var bounds=bundle.bounds;menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);return;
        }
        var b=menu.currentPageBundle;
        if(b.bundleIndex!=donationBundle)throw new InvalidOperationException("wrong_bundle_page");
        if(b.complete){donationClosing=true;return;}if(b.completionTimer>0)return;
        var cc=Game1.RequireLocation<CommunityCenter>("CommunityCenter");
        if(menu.purchaseButton!=null) {
            int price=b.ingredients.Last().stack;
            if(price>donationBudget||Game1.player.Money-price<donationKeep)throw new InvalidOperationException("vault_budget_or_reserve_insufficient");
            int before=Game1.player.Money;var bounds=menu.purchaseButton.bounds;menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
            if(before-Game1.player.Money!=price||!cc.bundles[donationBundle][0])throw new InvalidOperationException("native_vault_purchase_not_verified");
            Current!.effects.Add(new{kind="native_vault_bundle",bundle=donationBundle,spent=price});Current.completed++;donationClosing=true;return;
        }
        var slot=menu.ingredientSlots.FirstOrDefault(s=>s.item==null);if(slot==null){donationClosing=true;return;}
        for(int ingredient=0;ingredient<b.ingredients.Count;ingredient++) {
            var need=b.ingredients[ingredient];if(need.completed||donationBlocked.Contains(ingredient))continue;
            var choices=Game1.player.Items.Select((item,index)=>(item,index)).Where(x=>x.item!=null&&b.GetBundleIngredientDescriptionIndexForItem(x.item)==ingredient).OrderBy(x=>x.item.Quality).ToArray();
            if(choices.Sum(x=>x.item.Stack)<need.stack)continue;
            int remaining=need.stack;var take=new Dictionary<Item,int>();
            foreach(var choice in choices){int n=Math.Min(remaining,choice.item.Stack);take[choice.item]=n;remaining-=n;if(remaining==0)break;}
            try{ValidateConsumption?.Invoke(take,"","bundle:"+donationBundle);}
            catch(InvalidOperationException e){donationBlocked.Add(ingredient);Current!.effects.Add(new{kind="bundle_ingredient_reserved",ingredient,error=e.Message});continue;}
            var before=take.Keys.Select(i=>i.QualifiedItemId).Distinct().ToDictionary(id=>id,id=>Game1.player.Items.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack));
            foreach(var choice in choices.Where(c=>take.ContainsKey(c.item))) {
                if(menu.heldItem!=null)throw new InvalidOperationException("bundle_hand_must_be_empty");
                var itemBounds=menu.inventory.inventory[choice.index].bounds;menu.receiveLeftClick(itemBounds.Center.X,itemBounds.Center.Y);
                if(menu.heldItem==null)throw new InvalidOperationException("bundle_native_pickup_rejected");
                var bounds=slot.bounds;menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
                if(menu.heldItem!=null){menu.heldItem=menu.inventory.tryToAddItem(menu.heldItem);if(menu.heldItem!=null)throw new InvalidOperationException("bundle_remainder_inventory_full");}
                if(cc.bundles[donationBundle][ingredient])break;
            }
            var consumed=before.Select(p=>new{item=p.Key,count=p.Value-Game1.player.Items.Where(i=>i?.QualifiedItemId==p.Key).Sum(i=>i.Stack)}).ToArray();
            if(!cc.bundles[donationBundle][ingredient]||menu.partialDonationItem!=null||consumed.Sum(x=>x.count)!=need.stack)throw new InvalidOperationException("native_bundle_donation_not_verified");
            Current!.effects.Add(new{kind="native_bundle_ingredient",bundle=donationBundle,ingredient,quality=need.quality,consumed});Current.completed++;return;
        }
        Current!.effects.Add(new{kind="bundle_batch_finished",bundle=donationBundle,complete=b.complete,remaining=b.ingredients.Where(i=>!i.completed).Select(i=>new{i.id,i.category,i.stack,i.quality})});donationClosing=true;
    }
}
