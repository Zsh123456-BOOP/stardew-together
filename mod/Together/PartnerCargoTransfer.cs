using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private NPC? AvailableCargoPartner(SemanticJob? owner=null) {
        if(!Data.Partner.Enabled)return null;
        var npc=FindCharacter(PartnerName);if(npc?.currentLocation==null)return null;
        // Preserve the net-production baseline of active companion work.
        var actor=World().GetProperty("actors").EnumerateArray().FirstOrDefault(a=>a.GetProperty("name").GetString()==PartnerName);
        if(actor.ValueKind!=System.Text.Json.JsonValueKind.Object)return null;
        string id=actor.GetProperty("id").GetString()!;
        bool WaitingForThisDelivery(SemanticJob j)=>owner!=null&&j.command_id==owner.StorageSupportFor&&(j.Storing||j.goal=="store")&&j.child_id==null;
        if(semanticJobs.Values.Any(j=>j!=owner&&j.status=="running"&&(j.actor==id||j.CargoActor==id)&&!WaitingForThisDelivery(j)))return null;
        if(owner!=null)owner.CargoActor=id;
        return npc;
    }
    private int PartnerCargoCount(string item)=>AvailableCargoPartner()==null?0:Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{PartnerName}").Where(i=>i?.QualifiedItemId==item).Sum(i=>i.Stack);
    private bool ReceivePartnerCargo(SemanticJob job,string itemId,int wanted,bool withdrawal=false) {
        var npc=AvailableCargoPartner(job);if(npc==null||wanted<=0){job.CargoActor="";return false;}
        var p=Game1.player;var bag=p.team.GetOrCreateGlobalInventory($"Together_Pouch_{p.UniqueMultiplayerID}_{PartnerName}");
        var items=bag.Where(i=>i?.QualifiedItemId==itemId&&i.Quality>=job.MinimumQuality).ToArray();if(items.Length==0){job.CargoActor="";return false;}
        if(!items.Any(i=>p.couldInventoryAcceptThisItem(i))){job.CargoActor="";return false;}
        if(Game1.currentLocation!=npc.currentLocation){WorkChild(job,"player.travel",new{location=npc.currentLocation.NameOrUniqueName},"cargo_handoff_travel");return true;}
        if(Math.Abs(p.TilePoint.X-npc.TilePoint.X)+Math.Abs(p.TilePoint.Y-npc.TilePoint.Y)>1) {
            var stand=WorkStand(npc.currentLocation,npc.TilePoint);if(!stand.HasValue){job.CargoActor="";return false;}
            WorkChild(job,"player.move",new{x=stand.Value.X,y=stand.Value.Y},"cargo_handoff_move");return true;
        }
        int before=items.Sum(i=>i.Stack),playerBefore=p.Items.Where(i=>i?.QualifiedItemId==itemId&&i.Quality>=job.MinimumQuality).Sum(i=>i.Stack),moved=0;
        foreach(var item in items) {
            int n=Math.Min(item.Stack,wanted-moved);if(n<=0)break;
            var copy=item.getOne();copy.Stack=n;int accepted=n-(p.addItemToInventory(copy)?.Stack??0);
            item.Stack-=accepted;moved+=accepted;if(item.Stack<=0)bag.Remove(item);
        }
        int after=bag.Where(i=>i?.QualifiedItemId==itemId&&i.Quality>=job.MinimumQuality).Sum(i=>i.Stack),playerAfter=p.Items.Where(i=>i?.QualifiedItemId==itemId&&i.Quality>=job.MinimumQuality).Sum(i=>i.Stack);
        if(before-after!=moved||playerAfter-playerBefore!=moved)throw new InvalidOperationException("partner_handoff_conservation_failed");
        job.evidence.Add(new{kind="adjacent_partner_handoff",partner=PartnerName,item=itemId,moved,pouch_before=before,pouch_after=after,player_before=playerBefore,player_after=playerAfter,location=npc.currentLocation.NameOrUniqueName,tile=new[]{npc.TilePoint.X,npc.TilePoint.Y}});
        if(withdrawal)job.gained+=moved;
        job.CargoActor="";
        return moved>0;
    }
    private bool QueueInitialStorage() {
        if(SharedStorage().Any()||!Data.Storage.AutoExpand||Data.Storage.MaxSharedChests<1||Game1.timeOfDay>=1900)return false;
        var p=Game1.player;bool chest=p.Items.Any(i=>i?.QualifiedItemId=="(BC)130");
        if(!chest&&!p.craftingRecipes.ContainsKey("Chest"))return false;
        int cost=chest?0:new CraftingRecipe("Chest",false).recipeList.GetValueOrDefault("388");
        if(!chest&&(cost<=0||Data.Storage.WoodBudgetPerDay-(Data.Storage.BudgetDay==Game1.Date.TotalDays?Data.Storage.WoodReserved:0)<cost))return false;
        int wood=p.Items.Where(i=>i?.QualifiedItemId=="(O)388").Sum(i=>i.Stack),cargo=PartnerCargoCount("(O)388");
        int missing=Math.Max(0,cost-wood-cargo);
        // Preserve pending crop care and the actual slot required by crafting.
        if(!chest&&(wood+cargo==0||p.freeSpotsInInventory()<(wood==0?2:1)||p.Stamina-missing*4<20+Facts.DryCrops*4))return false;
        var actions=new List<(string Tool,object Args)>();
        if(missing>0)actions.Add(("work.run",new{goal="wood",location="Farm",count=missing,reserve_stamina=Math.Max(20,20+Facts.DryCrops*4)}));
        actions.Add(("work.run",new{goal="storage_expand",until=2100}));
        return QueueBusiness("initial_storage",actions,"汇合真实材料、原生制作首个仓库，避免重复采木与满包后才准备仓储");
    }
}
