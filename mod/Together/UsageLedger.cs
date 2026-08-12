using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private sealed class Usage {public int Calls {get;set;} public long Tokens {get;set;}}
    private string UsagePath=>Path.Combine(Helper.DirectoryPath,"usage",$"{Game1.uniqueIDForThisGame}-{Game1.player.UniqueMultiplayerID}-{Game1.Date.TotalDays}.json");
    private void LoadUsage() {
        if(!File.Exists(UsagePath))return;
        try {var u=JsonSerializer.Deserialize<Usage>(File.ReadAllText(UsagePath));if(u!=null){Data.Calls=Math.Max(Data.Calls,u.Calls);Data.Tokens=Math.Max(Data.Tokens,u.Tokens);}}
        catch {Data.Calls=Math.Max(Math.Clamp(Settings.MaxCallsPerDay,1,100),Math.Clamp(Settings.AutoplayMaxCallsPerDay,1,2000));Notice="本地用量记录不可读，暂时停止新的模型请求。";}
    }
    private void RecordUsage() {
        Directory.CreateDirectory(Path.GetDirectoryName(UsagePath)!);
        File.WriteAllText(UsagePath+".tmp",JsonSerializer.Serialize(new Usage{Calls=Data.Calls,Tokens=Data.Tokens}));
        File.Move(UsagePath+".tmp",UsagePath,true);
    }
}
