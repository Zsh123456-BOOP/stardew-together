using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Characters;
using System.Text.Json;

namespace Together;
public sealed partial class ModEntry {
    private const string PartnerName="Together_Partner";
    private DateTime partnerCheck;
    internal bool SinglePlayerMode=>Settings.SinglePlayerAutoplay||Data.Autoplay.Survival.NativePlayerOnly;
    private IEnumerable<string> ActiveActors=>SinglePlayerMode?new[]{"player"}:agentKnownActors;
    private void SuspendCustomPartner() {
        var npc=api?.GetCharacter(PartnerName);if(npc==null)return;
        SaveCustomPartner();Data.Partner.SuspendedData=npc.modData.Pairs.ToDictionary(p=>p.Key,p=>p.Value);
        if(api?.SuspendCustomCompanion(PartnerName)!=true)throw new InvalidOperationException("custom_partner_suspend_unverified");
        bubbles.Remove(PartnerName);agentKnownActors.RemoveWhere(a=>a.EndsWith(":"+PartnerName));
    }
    private void SetupCustomPartner() {
        Helper.Events.Content.AssetRequested+=(_,e)=>{
            if(e.NameWithoutLocale.IsEquivalentTo("Data/Characters"))e.Edit(asset=>{
                asset.AsDictionary<string,CharacterData>().Data[PartnerName]=new CharacterData {
                    DisplayName=Data.Partner.DisplayName,TextureName=Data.Partner.Appearance,
                    HomeRegion="Other",SpawnIfMissing=false,CanBeRomanced=false,CanReceiveGifts=false,
                    IntroductionsQuest=false,PerfectionScore=false,CanVisitIsland="FALSE",ItemDeliveryQuests="FALSE",
                    Home=new(){new(){Id="farm",Location="Farm",Tile=new Point(Data.Partner.X,Data.Partner.Y),Direction="down"}}
                };
            });
            if(e.NameWithoutLocale.IsEquivalentTo("Characters/Dialogue/"+PartnerName))e.LoadFrom(()=>new Dictionary<string,string>{{"Introduction","我是小禾，以后一起照料这座农场吧。"},{"Mon","今天想先忙些什么？"}},StardewModdingAPI.Events.AssetLoadPriority.Exclusive);
            if(e.NameWithoutLocale.IsEquivalentTo("Characters/schedules/"+PartnerName))e.LoadFrom(()=>new Dictionary<string,string>(),StardewModdingAPI.Events.AssetLoadPriority.Exclusive);
        };
    }
    internal object ConfigurePartner(JsonElement args) {
        var p=Data.Partner;
        if(args.TryGetProperty("enabled",out var enabled))p.Enabled=enabled.GetBoolean();
        string name=AgentToolRegistry.Text(args,"name",p.DisplayName),appearance=AgentToolRegistry.Text(args,"appearance",p.Appearance);
        if(name.Length is <1 or >24||!new[]{"Leah","Alex","Sam","Maru","Sebastian","Abigail"}.Contains(appearance))throw new InvalidOperationException("invalid_partner_name_or_appearance");
        p.DisplayName=name;p.Appearance=appearance;Helper.GameContent.InvalidateCache("Data/Characters");
        if(p.Enabled)EnsureCustomPartner();
        return new{partner=p,identity=PartnerName,note="独立自定义NPC；外观暂引用原生素材，不招募对应村民。关闭停止自动入队，不删除角色或货袋。"};
    }
    private void EnsureCustomPartner(bool restore=false) {
        if(Context.IsWorldReady&&SinglePlayerMode){SuspendCustomPartner();return;}
        if(!Context.IsWorldReady||Context.IsMultiplayer||!Data.Partner.Enabled||api==null)return;
        var p=Data.Partner;
        var npc=Game1.getCharacterFromName(PartnerName);
        if(npc==null) {
            var farm=Game1.getFarm();Point at=new(p.X,p.Y);
            if(!p.Created||!PlayerExecutor.Passable(farm,at)) {
                var home=farm.buildings.FirstOrDefault(b=>b.buildingType.Value=="Farmhouse");
                var origin=home==null?new Point(64,16):new Point(home.tileX.Value+home.humanDoor.Value.X,home.tileY.Value+home.humanDoor.Value.Y+2);
                var options=Enumerable.Range(-5,11).SelectMany(x=>Enumerable.Range(-5,11).Select(y=>new Point(origin.X+x,origin.Y+y))).Where(t=>PlayerExecutor.Passable(farm,t)).OrderBy(t=>Math.Abs(t.X-origin.X)+Math.Abs(t.Y-origin.Y)).ToArray();
                if(options.Length==0)throw new InvalidOperationException("partner_spawn_no_free_tile");at=options[0];
            }
            npc=new NPC(new AnimatedSprite("Characters/"+p.Appearance,0,16,32),at.ToVector2()*64,"Farm",2,PartnerName,false,Game1.content.Load<Texture2D>("Portraits/"+p.Appearance));
            npc.modData["stardewagent.together/custom-partner"]="1";farm.characters.Add(npc);p.Created=true;
            foreach(var pair in p.SuspendedData)npc.modData[pair.Key]=pair.Value;
        }
        // Restore only at save load, never to recover a failed runtime route.
        if(restore&&Game1.getLocationFromName(p.Location) is {} saved&&PlayerExecutor.Passable(saved,new(p.X,p.Y))) {
            if(npc.currentLocation!=saved){npc.currentLocation?.characters.Remove(npc);saved.characters.Add(npc);npc.currentLocation=saved;}
            npc.Position=new Vector2(p.X,p.Y)*64;
        }
        npc.displayName=p.DisplayName;
        if(!Game1.player.friendshipData.ContainsKey(PartnerName))Game1.player.friendshipData[PartnerName]=new Friendship(0);
        if(!api.AttachCustomCompanion(PartnerName))return;
        Person(PartnerName).DailyCompanion=true;Data.Selected=PartnerName;
        var actor=Actor(World(),PartnerName);if(actor.HasValue)agentKnownActors.Add(actor.Value.GetProperty("id").GetString()!);
    }
    private void SaveCustomPartner() {
        var npc=api?.GetCharacter(PartnerName);if(npc?.currentLocation==null)return;
        Data.Partner.Location=npc.currentLocation.NameOrUniqueName;Data.Partner.X=npc.TilePoint.X;Data.Partner.Y=npc.TilePoint.Y;
    }
    private void TickCustomPartner() {
        if(DateTime.UtcNow<partnerCheck)return;partnerCheck=DateTime.UtcNow.AddSeconds(10);
        EnsureCustomPartner();
    }
}
