using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using TheStardewSquad.Framework;
using TheStardewSquad.Framework.Squad;

namespace TheStardewSquad;

public sealed partial class CompanionControl {
    public sealed class FarmPolicy {
        public bool Enabled {get;set;}=true;
        public bool FeedAnimals {get;set;}=true;
    public bool ClearDesignatedPlots {get;set;}
        public int DailyBudget {get;set;}
        public int KeepGold {get;set;}=500;
        public List<PlantingArea> Areas {get;set;}=new();
        public List<ShoppingOrder> Shopping {get;set;}=new();
    }
    public sealed class PlantingArea {
        public string Id {get;set;}="";
        public string Location {get;set;}="Farm";
        public int X {get;set;}
        public int Y {get;set;}
        public int Width {get;set;}=3;
        public int Height {get;set;}=3;
        public string Seed {get;set;}="(O)472";
        public bool Enabled {get;set;}=true;
    }
    public sealed class ShoppingOrder {
        public string Id {get;set;}="";
        public string Shop {get;set;}="SeedShop";
        public string Item {get;set;}="(O)472";
        public int Count {get;set;}=9;
        public int MaxUnitPrice {get;set;}=100;
        public bool Enabled {get;set;}=true;
    }
    private FarmPolicy farmPolicy=new(){Enabled=false};
    private sealed record Plot(string AreaId,string Seed,Point Tile);
    private readonly Dictionary<string,Plot> plotTargets=new();
    private Plot PlotTarget(PlantingArea area,Point tile) {
        string id=area.Id+":"+tile.X+":"+tile.Y+":"+area.Seed;
        if(!plotTargets.TryGetValue(id,out var p))plotTargets[id]=p=new(area.Id,area.Seed,tile);
        return p;
    }
    private bool CanGrow(GameLocation location,string qualified,Point tile) {
        // Resolve random mixed seeds only when planting. Planning uses deterministic seed data.
        string seed=qualified.Replace("(O)","");
        if(!Crop.TryGetData(seed,out var crop) || crop.Seasons.Count==0)return false;
        if(!location.CheckItemPlantRules(seed,false,location.GetData()?.CanPlantHere??location.IsFarm,out _)
            || !location.CanPlantSeedsHere(seed,tile.X,tile.Y,false,out _))return false;
        if(location.SeedsIgnoreSeasonsHere())return true;
        if(!crop.Seasons.Contains(location.GetSeason()))return false;
        int days=crop.DaysInPhase.Sum();
        // Conservative first harvest: don't rely on random growth or player professions.
        int remaining=28-Game1.dayOfMonth;
        var season=location.GetSeason();
        for(int offset=1;offset<4 && remaining<days;offset++) {
            var next=(Season)(((int)season+offset)%4);
            if(!crop.Seasons.Contains(next))break;
            remaining+=28;
        }
        return days<=remaining;
    }
    private ResourceWork? Ingredient(ISquadMate mate,string qualified,int count=1) {
        var pouch=Pouch(mate);
        var own=pouch.FirstOrDefault(i=>i!=null && i.QualifiedItemId==qualified && FreeCount(i)>=count);
        if(own!=null)return new(){PickedUp=true,Input=qualified,Takes=new(){new(own,count)},Quality=own.Quality};
        if(pouch.Count(i=>i!=null)>=12)return null;
        foreach(var chest in mate.Npc.currentLocation.objects.Values.OfType<Chest>().Where(c=>Role(c)=="supplies")) {
            var item=chest.GetItemsForPlayer(mate.RecruiterUniqueId).FirstOrDefault(i=>i!=null && i.QualifiedItemId==qualified && FreeCount(i)>=count);
            var stand=StandingSpot(mate,chest.TileLocation.ToPoint());
            if(item!=null && stand.HasValue)return new(){Chest=chest,PickupStand=stand.Value,Input=qualified,Quality=item.Quality,Takes=new(){new(item,count)}};
        }
        return null;
    }
    private IEnumerable<Candidate> ProductionCandidates(ISquadMate mate) {
        if(!farmPolicy.Enabled)yield break;
        var location=mate.Npc.currentLocation;
        foreach(var area in farmPolicy.Areas.Where(a=>a.Enabled && a.Location==location.NameOrUniqueName).Take(16)) {
            for(int y=area.Y;y<area.Y+Math.Clamp(area.Height,1,12);y++)for(int x=area.X;x<area.X+Math.Clamp(area.Width,1,12);x++) {
                var tile=new Point(x,y);var v=tile.ToVector2();
                if(location.objects.TryGetValue(v,out var litter)) {
                    if(farmPolicy.ClearDesignatedPlots && (litter.IsTwig() || litter.IsWeeds()) && Pouch(mate).Count(i=>i!=null)<10) {
                        var nearby=StandingSpot(mate,tile);if(nearby.HasValue)yield return new(TargetId(litter)+":clear","clear",tile,litter,nearby.Value);
                    }
                    continue;
                }
                if(!CanGrow(location,area.Seed,tile))continue;
                location.terrainFeatures.TryGetValue(v,out var feature);
                string skill;
                if(feature is HoeDirt dirt && dirt.crop==null)skill="plant";
                else if(feature==null && location.doesTileHaveProperty(x,y,"Diggable","Back")!=null && location.CanItemBePlacedHere(v,collisionMask:CollisionMask.All & ~(CollisionMask.Characters | CollisionMask.Farmers)))skill="till";
                else continue;
                if(Ingredient(mate,area.Seed)==null)continue;
                var stand=StandingSpot(mate,tile);if(!stand.HasValue)continue;
                var plot=PlotTarget(area,tile);yield return new(TargetId(plot)+":"+skill,skill,tile,plot,stand.Value);
            }
        }
        if(farmPolicy.FeedAnimals && location is AnimalHouse house) {
            int missing=Math.Max(0,house.animalsThatLiveHere.Count-house.objects.Pairs.Count(p=>p.Value.QualifiedItemId=="(O)178" && house.doesTileHaveProperty((int)p.Key.X,(int)p.Key.Y,"Trough","Back")!=null));
            int available=Game1.getFarm().piecesOfHay.Value+house.GetRootLocation().piecesOfHay.Value;
            if(available>0 || Ingredient(mate,"(O)178")!=null) {
                for(int y=0;y<house.Map.Layers[0].LayerHeight && missing>0;y++)for(int x=0;x<house.Map.Layers[0].LayerWidth && missing>0;x++) {
                    var tile=new Point(x,y);if(house.doesTileHaveProperty(x,y,"Trough","Back")==null || house.objects.ContainsKey(tile.ToVector2()))continue;
                    var stand=StandingSpot(mate,tile);if(!stand.HasValue)continue;
                    // Intern targets so competing actors lock the same trough.
                    var p=PlotTarget(new(){Id="trough:"+location.NameOrUniqueName,Seed="(O)178"},tile);
                    yield return new(TargetId(p)+":feed","feed",tile,p,stand.Value);missing--;
                }
            }
        }
        foreach(var animal in Game1.getFarm().getAllFarmAnimals().Where(a=>a.currentLocation==location && a.currentProduce.Value!=null)) {
            string tool=animal.GetAnimalData().HarvestTool??"";
            if(tool is not ("Milk Pail" or "Shears"))continue;
            var stand=StandingSpot(mate,animal.TilePoint);
            if(stand.HasValue && Pouch(mate).Count(i=>i!=null)<12)yield return new(TargetId(animal)+":tend","tend",animal.TilePoint,animal,stand.Value);
        }
        foreach(var pair in location.objects.Pairs.Where(p=>(p.Value.isForage() || p.Value.isAnimalProduct()) && !p.Value.bigCraftable.Value).Take(24)) {
            var stand=StandingSpot(mate,pair.Key.ToPoint());
            if(stand.HasValue && Pouch(mate).Count(i=>i!=null)<12)yield return new(TargetId(pair.Value)+":forage","forage",pair.Key.ToPoint(),pair.Value,stand.Value);
        }
    }
    private bool ProductionPending(Record r) {
        if(!farmPolicy.Enabled)return false;
        if(r.Skill=="clear")return farmPolicy.ClearDesignatedPlots && farmPolicy.Areas.Any(a=>a.Enabled && a.Location==r.Location.NameOrUniqueName && r.Target.X>=a.X && r.Target.X<a.X+a.Width && r.Target.Y>=a.Y && r.Target.Y<a.Y+a.Height) && r.Location.objects.TryGetValue(r.Target.ToVector2(),out var litter) && ReferenceEquals(litter,r.Source) && (litter.IsTwig() || litter.IsWeeds());
        if(r.Skill=="tend")return r.Source is FarmAnimal animal && animal.currentLocation==r.Location && animal.TilePoint==r.Target && animal.currentProduce.Value!=null;
        if(r.Skill=="forage")return r.Location.objects.TryGetValue(r.Target.ToVector2(),out var o) && ReferenceEquals(o,r.Source) && (o.isForage() || o.isAnimalProduct());
        if(r.Skill=="feed")return farmPolicy.FeedAnimals && r.Location is AnimalHouse h && h.doesTileHaveProperty(r.Target.X,r.Target.Y,"Trough","Back")!=null && !h.objects.ContainsKey(r.Target.ToVector2());
        if(r.Source is not Plot plot || !farmPolicy.Areas.Any(a=>a.Id==plot.AreaId && a.Enabled && a.Seed==plot.Seed && a.Location==r.Location.NameOrUniqueName
            && r.Target.X>=a.X && r.Target.X<a.X+a.Width && r.Target.Y>=a.Y && r.Target.Y<a.Y+a.Height) || !CanGrow(r.Location,plot.Seed,r.Target))return false;
        if(r.Location.objects.ContainsKey(r.Target.ToVector2()))return false;
        r.Location.terrainFeatures.TryGetValue(r.Target.ToVector2(),out var feature);
        return r.Skill=="till"?feature==null:feature is HoeDirt d && d.crop==null;
    }
    private void DriveProduction(Record r,bool slow,Farmer player) {
        var npc=r.Mate.Npc;if(r.Mate.IsOnCooldown())return;
        if(r.Resources?.PickedUp==false) {PickupIngredient(r,slow,player);return;}
        if(npc.TilePoint!=r.Stand){mod.FollowerManager.WalkAgent(r.Mate,r.Stand,slow,player);return;}
        r.Mate.Halt();npc.faceGeneralDirection(r.Target.ToVector2()*64+new Vector2(32));
        if(r.WorkSeconds==0) {if(r.Skill=="till")AnimateHoe(npc);else if(r.Skill=="clear")TaskManager.AnimateLumbering(npc);else npc.shake(350);}
        r.WorkSeconds+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;if(r.WorkSeconds<(r.Skill is "till" or "clear"?.3:.6))return;
        reservationTick=-1;
        if(!ProductionPending(r)){Finish(r,"failed","production_permission_or_target_changed");return;}
        if(r.Skill=="clear") {
            var litter=(StardewValley.Object)r.Source!;var before=r.Location.debris.ToHashSet();
            using(var output=OwnOutput(r.Mate)) {
                bool removed=litter.performToolAction(new StardewValley.Tools.Axe{lastUser=player});
                foreach(var drop in r.Location.debris.Where(d=>!before.Contains(d)).ToArray()) {
                    var item=drop.item;
                    if(item!=null && CargoAccept(item,true)==true){r.Location.debris.Remove(drop);r.Catches.Add(item.QualifiedItemId);r.ResourceChanges[item.QualifiedItemId+":"+item.Quality]=item.Stack;}
                }
                if(!removed){r.WorkSeconds=0;r.Mate.ActionCooldown=24;return;}
                r.Location.objects.Remove(r.Target.ToVector2());
            }
        } else if(r.Skill=="till") {
            if(!r.Location.makeHoeDirt(r.Target.ToVector2())){Finish(r,"failed","soil_not_tillable");return;}
            r.Location.playSound("hoeHit");
        } else if(r.Skill=="plant") {
            var plot=(Plot)r.Source!;var item=Pouch(r.Mate).FirstOrDefault(i=>i!=null && i.QualifiedItemId==plot.Seed && FreeCount(i)>0);
            if(item==null){Finish(r,"failed","seed_missing_or_reserved");return;}
            var dirt=(HoeDirt)r.Location.terrainFeatures[r.Target.ToVector2()];
            // Native Crop growth, terrain callbacks, fertilizer and paddy rules. No player teleport,
            // no falsely incremented player SeedsSown/experience for the companion's work.
            dirt.crop=new Crop(item.ItemId,r.Target.X,r.Target.Y,r.Location);
            dirt.applySpeedIncreases(new Farmer());
            dirt.nearWaterForPaddy.Value=-1;
            if(dirt.hasPaddyCrop() && dirt.paddyWaterCheck()){dirt.state.Value=1;dirt.updateNeighbors();}
            Consume(r,item,1);r.Location.playSound("dirtyHit");
        } else if(r.Skill=="feed") {
            var hay=Pouch(r.Mate).FirstOrDefault(i=>i!=null && i.QualifiedItemId=="(O)178" && FreeCount(i)>0);
            StardewValley.Object? placed;
            if(hay!=null){placed=(StardewValley.Object)hay.getOne();Consume(r,hay,1);}
            else {placed=GameLocation.GetHayFromAnySilo(r.Location.GetRootLocation());if(placed!=null)r.ResourceChanges["silo:(O)178"]=-1;}
            if(placed==null){Finish(r,"failed","hay_unavailable");return;}
            placed.TileLocation=r.Target.ToVector2();r.Location.objects.Add(r.Target.ToVector2(),placed);r.Location.playSound("shwip");
        } else if(r.Skill=="tend") {
            var animal=(FarmAnimal)r.Source!;
            var produce=ItemRegistry.Create(animal.currentProduce.Value);produce.Quality=animal.produceQuality.Value;
            if(animal.hasEatenAnimalCracker.Value)produce.Stack=2;
            using(var scope=OwnOutput(r.Mate)) {
                if(CargoAccept(produce,false)!=true){Finish(r,"failed","pouch_full");return;}
                if(animal.GetAnimalData().HarvestTool=="Milk Pail")TaskManager.ExecuteMilkingTask(r.Mate,r.Target);
                else TaskManager.ExecuteShearingTask(r.Mate,r.Target);
            }
            if(animal.currentProduce.Value!=null){Finish(r,"failed","animal_not_ready");return;}
            r.Catches.Add(produce.QualifiedItemId);
        } else {
            var item=(StardewValley.Object)r.Source!;
            if(Pouch(r.Mate).Count(i=>i!=null)>=12){Finish(r,"failed","pouch_full");return;}
            var copy=item.getOne();copy.Stack=item.Stack;Pouch(r.Mate).Add(copy);
            r.Location.objects.Remove(r.Target.ToVector2());r.Catches.Add(item.QualifiedItemId);r.ResourceChanges[item.QualifiedItemId+":"+item.Quality]=copy.Stack;
        }
        r.EffectByActor=true;r.Mate.ActionCooldown=24;
    }
    private void Consume(Record r,Item item,int count) {
        r.ResourceChanges[item.QualifiedItemId+":"+item.Quality]=-count;
        item.Stack-=count;if(item.Stack<=0)Pouch(r.Mate).Remove(item);
    }
    private void PickupIngredient(Record r,bool slow,Farmer player) {
        var w=r.Resources!;var npc=r.Mate.Npc;
        if(npc.TilePoint!=w.PickupStand){mod.FollowerManager.WalkAgent(r.Mate,w.PickupStand,slow,player);return;}
        r.Mate.Halt();npc.faceGeneralDirection(w.Chest!.TileLocation*64);
        r.WorkSeconds+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;if(r.WorkSeconds<.5)return;r.WorkSeconds=0;
        var inventory=w.Chest.GetItemsForPlayer(r.Mate.RecruiterUniqueId);
        if(Role(w.Chest)!="supplies" || !r.Location.objects.Values.Contains(w.Chest) || Pouch(r.Mate).Count(i=>i!=null)>=12
            || w.Takes.Any(t=>!inventory.Contains(t.Item) || FreeCount(t.Item)<t.Count)) {Finish(r,"failed","ingredients_changed");return;}
        foreach(var take in w.Takes) {
            var copy=take.Item.getOne();copy.Stack=take.Count;Pouch(r.Mate).Add(copy);
            take.Item.Stack-=take.Count;if(take.Item.Stack<=0)inventory.Remove(take.Item);
        }
        reservationTick=-1;w.PickedUp=true;r.PickupTile=Tile(npc.TilePoint);
    }
}
