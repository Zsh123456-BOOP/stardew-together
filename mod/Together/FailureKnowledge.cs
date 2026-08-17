using System.Security.Cryptography;
using System.Text;

namespace Together;
public sealed class FailureExperience {
    public string Key {get;set;}="";
    public string Actor {get;set;}="";
    public string Tool {get;set;}="";
    public string Reason {get;set;}="";
    public string Conditions {get;set;}="";
    public string TaskEvidence {get;set;}="";
    public int Day {get;set;}
    public int RetryAfterMinute {get;set;}
    public int Attempts {get;set;}
}
public sealed class FailureKnowledge {
    public List<FailureExperience> Entries {get;set;}=new();
    public static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Key(string actor,string tool,string args)=>Hash(actor+"\n"+tool+"\n"+args);
    public FailureExperience? Block(string key,string conditions,int day,int minute)=>Entries.LastOrDefault(e=>e.Key==key&&e.Conditions==conditions&&e.Day==day&&minute<e.RetryAfterMinute);
    public void Record(string key,string actor,string tool,string reason,string conditions,string task,int day,int minute) {
        var last=Entries.LastOrDefault(e=>e.Key==key&&e.Conditions==conditions&&e.Day==day);
        int attempts=(last?.Attempts??0)+1;Entries.RemoveAll(e=>e.Key==key);
        Entries.Add(new(){Key=key,Actor=actor,Tool=tool,Reason=reason,Conditions=conditions,TaskEvidence=task,Day=day,RetryAfterMinute=minute+Math.Min(120,20*attempts),Attempts=attempts});
        if(Entries.Count>96)Entries.RemoveRange(0,Entries.Count-96);
    }
    public void Success(string key)=>Entries.RemoveAll(e=>e.Key==key);
}
