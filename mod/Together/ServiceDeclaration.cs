using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
namespace Together;
public sealed partial class PlayerExecutor {
    internal static List<Point> ServiceCounters(GameLocation l,string kind,string shop) {
        var found=new List<Point>();
        for(int y=0;y<l.Map.Layers[0].LayerHeight;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth;x++) {
            var a=l.GetTilePropertySplitBySpaces("Action","Buildings",x,y);if(a.Length==0)continue;
            bool match=kind switch {
                "shop"=>ShopFromAction(l,a)==shop,
                "daily_quests"=>a[0]=="Billboard"&&a.Length>1&&a[1]=="3",
                "special_orders"=>a[0]=="SpecialOrders","qi_orders"=>a[0]=="QiChallengeBoard",
                "museum_donate" or "museum_reward"=>a[0]=="Gunther",
                "build"=>a[0] is "Carpenter" or "WizardBook","upgrade_house"=>a[0]=="Carpenter",
                "animals"=>a[0]=="AnimalShop","upgrade_tools" or "geodes" or "claim_tool"=>a[0]=="Blacksmith",_=>false};
            if(match)found.Add(new(x,y));
        }
        return found;
    }
    internal static string? ServiceParameterError(JsonElement args) {
        string kind=AgentToolRegistry.Text(args,"service","shop"),shop=AgentToolRegistry.Text(args,"shop"),name=AgentToolRegistry.Text(args,"location",Game1.currentLocation.NameOrUniqueName);
        if(kind is not ("shop" or "build" or "upgrade_house" or "upgrade_tools" or "animals" or "geodes" or "claim_tool" or "museum_donate" or "museum_reward" or "daily_quests" or "special_orders" or "qi_orders"))return "parameter_invalid:service";
        if(kind=="shop"&&shop.Length==0)return "parameter_required:shop";
        var l=Game1.getLocationFromName(name);if(l==null)return "parameter_invalid:location:"+name;
        if(ServiceCounters(l,kind,shop).Count>0)return null;
        var valid=Game1.locations.Where(x=>ServiceCounters(x,kind,shop).Count>0).Select(x=>x.NameOrUniqueName);
        return "parameter_service_location_mismatch:location="+name+":service="+kind+":shop="+shop+":valid_locations="+string.Join(",",valid);
    }
}
public sealed partial class ModEntry {
    internal void ValidateToolDeclaration(string tool,JsonElement args) {
        string? error=null;
        if(tool=="player.service")error=PlayerExecutor.ServiceParameterError(args);
        if(tool=="player.place_facility"&&string.IsNullOrWhiteSpace(AgentToolRegistry.Text(args,"item")))error="parameter_required:item:player.place_facility";
        if(tool=="farm.plan"&&!Game1.currentLocation.IsGreenhouse&&(!Game1.currentLocation.IsFarm||!Game1.currentLocation.IsOutdoors))error="plan_on_farm_or_greenhouse_first_travel_to_Farm";
        if(tool=="player.social"&&AutoplayRunning&&SocialBasis(args)==null)error="social_basis_required:choose_current_task_birthday_gift_or_relationship_target";
        if(error==null)return;
        Data.Autoplay.Record("declaration_rejected",AgentJson.Encode(new{tool,args,error,category="parameter_or_precondition",no_action_started=true}));
        throw new InvalidOperationException(error);
    }
}
