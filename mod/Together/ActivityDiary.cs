using System.Text.Json;
namespace Together;
public sealed class DiaryRow {
    public int Day {get;set;} public int First {get;set;} public int Last {get;set;}
    public string Kind {get;set;}="";public string Item {get;set;}="";public string Location {get;set;}="";
    public int Count {get;set;} public int Cost {get;set;} public int Batches {get;set;}
    public List<string> Evidence {get;set;}=new();
    public List<string> SupportingEvidence {get;set;}=new();
}
// A projection of verified receipts, never model intentions or the source of stock.
public sealed class ActivityDiary {
    public List<DiaryRow> Rows {get;set;}=new();
    public Dictionary<string,int> Seen {get;set;}=new();
    private static string Text(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    private static int Num(JsonElement e,string key,int fallback=0)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out int n)?n:fallback;
    private static JsonElement Child(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(key,out var v)?v:default;
    private static IEnumerable<JsonElement> Array(JsonElement e)=>e.ValueKind==JsonValueKind.Array?e.EnumerateArray():Enumerable.Empty<JsonElement>();
    public bool Add(string id,int day,int time,string kind,string item,string location,int count,int cost=0) {
        if(Seen.ContainsKey(id)||count==0&&cost==0)return false;
        Seen[id]=day;
        var row=Rows.FirstOrDefault(r=>r.Day==day&&r.Kind==kind&&r.Item==item&&r.Location==location);
        if(row==null){row=new(){Day=day,First=time,Kind=kind,Item=item,Location=location};Rows.Add(row);}
        row.Last=time;row.Count+=count;row.Cost+=cost;row.Batches++;row.Evidence.Add(id);if(row.Evidence.Count>4)row.Evidence.RemoveAt(0);
        Rows.RemoveAll(r=>r.Day<day-6);foreach(var key in Seen.Where(p=>p.Value<day-6).Select(p=>p.Key).ToArray())Seen.Remove(key);
        return true;
    }
    public void Native(JsonElement action,int day,int time) {
        string id=Text(action,"command_id"),skill=Text(action,"skill");if(id.Length==0||Text(action,"status")=="running")return;
        var before=Child(action,"before");var after=Child(action,"after");string location=Text(after,"location");int n=0;
        var purchases=new Dictionary<string,int>();var purchaseRows=new List<DiaryRow>();int goldPaid=0;
        foreach(var e in Array(Child(action,"effects"))) {
            string key=id+":"+n++,kind=Text(e,"kind"),item=Text(e,"item");
            if(kind=="native_purchase") {
                string activity=Num(e,"currency")==0?"购买":"购买（货币"+Num(e,"currency")+"）";
                Add(key,day,time,activity,item,location,Num(e,"units"),Num(e,"cost"));
                if(Child(e,"quality").ValueKind==JsonValueKind.Number) {string stockKey=item+"|q"+Num(e,"quality");purchases[stockKey]=purchases.GetValueOrDefault(stockKey)+Num(e,"units");}
                if(Num(e,"currency")==0)goldPaid+=Num(e,"cost");
                var row=Rows.FirstOrDefault(r=>r.Day==day&&r.Kind==activity&&r.Item==item&&r.Location==location);if(row!=null)purchaseRows.Add(row);
            }
            else if(kind=="native_discard"&&Child(e,"verified").ValueKind==JsonValueKind.True)Add(key,day,time,"原生垃圾桶销毁",item,location,Num(e,"destroyed"));
            else if(kind=="native_recipe")Add(key,day,time,"制作完成（入包另核对）",item,location,Num(e,"count"));
            else if(kind=="native_placement")Add(key,day,time,"放置",item,location,Num(e,"consumed"));
            else if(kind=="native_shipment"||Text(e,"shipped").Length>0)Add(key,day,time,"出货待结算",item.Length>0?item:Text(e,"shipped"),location,Num(e,"count"));
            else if(Text(e,"work_skill") is {} work&&work.Length>0) {
                var state=Child(e,"after");var old=Child(e,"before");
                string crop=Text(state,"crop");if(crop.Length==0)crop=Text(old,"crop");if(crop.Length>0&&!crop.StartsWith("("))crop="(O)"+crop;
                if(work=="plant"&&Text(old,"crop").Length==0&&Text(state,"crop").Length>0)Add(key,day,time,"种下",crop,location,1);
                else if(work=="water"&&Num(old,"watered",-1)!=1&&Num(state,"watered",-1)==1)Add(key,day,time,"浇水",crop,location,1);
                else if(work is "harvest" or "forage")Add(key,day,time,work=="harvest"?"收获点":"采集点",crop.Length>0?crop:Text(old,"item"),location,1);
            }
        }
        // Bag deltas are observations, not claimed harvest yields. A failed action
        // may still have made real progress; positive deltas are not all income.
        Dictionary<string,int> Stock(JsonElement snapshot)=>Array(Child(Child(snapshot,"inventory"),"items")).Select(r=>Child(r,"item")).Where(i=>Text(i,"id").Length>0).GroupBy(i=>Text(i,"id")+"|q"+Num(i,"quality")).ToDictionary(g=>g.Key,g=>g.Sum(i=>Num(i,"count")));
        if(before.ValueKind==JsonValueKind.Object&&after.ValueKind==JsonValueKind.Object) {
            var a=Stock(before);var b=Stock(after);
            void Support(string evidence){foreach(var row in purchaseRows.Distinct()){if(!row.SupportingEvidence.Contains(evidence))row.SupportingEvidence.Add(evidence);if(row.SupportingEvidence.Count>4)row.SupportingEvidence.RemoveAt(0);}}
            foreach(var key in a.Keys.Union(b.Keys)) {
                int delta=b.GetValueOrDefault(key)-a.GetValueOrDefault(key);if(delta==0)continue;
                // Only this command's verified, quality-specific purchase can explain a bag delta.
                // A mixed or incomplete transfer remains observable, never guessed from daily totals.
                if(delta>0&&purchases.GetValueOrDefault(key)==delta)Support(id+":bag:"+key);
                else Add(id+":bag:"+key,day,time,delta>0?"行动期间入包":"行动期间取用",key,location,Math.Abs(delta));
            }
            int cash=Num(after,"money")-Num(before,"money");
            if(goldPaid>0&&cash==-goldPaid)Support(id+":cash");
            else if(cash!=0)Add(id+":cash",day,time,skill=="player.sleep"?"过夜现金变化":"现金变化","金币",location,cash);
        }
    }
    public void Transfers(string id,JsonElement rows,int day,int time,string location,bool intoBag=false) {
        int n=0;foreach(var row in Array(rows)) {
            bool withdraw=row.TryGetProperty("into_bag",out var direction)?direction.ValueKind==JsonValueKind.True:intoBag;
            Add(id+":"+n++,day,time,withdraw?"取出仓库":"存入仓库",Text(row,"item").Length>0?Text(row,"item"):Text(row,"id"),location,Num(row,"count",Num(row,"moved")));
        }
    }
}
