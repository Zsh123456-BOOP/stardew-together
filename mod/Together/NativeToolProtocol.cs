using System.Text.Json;
using System.Text.Json.Nodes;
namespace Together;

// API names cannot contain dots. Business names and the executor stay unchanged.
public static class NativeToolProtocol {
    public static string Name(string tool)=>tool.Replace(".","__",StringComparison.Ordinal);
    private static readonly HashSet<string> Numbers=new("count quality budget keep_gold max_unit_price reserve_stamina until daily_limit offset limit depth expected_revision day deadline not_before seconds x y width height radius slot required_free_slots max_food objective level profession".Split(' '));
    private static readonly HashSet<string> Booleans=new("enabled run additional allow_new_facilities remove_trees include_trees exact_quality recipe right submit allow".Split(' '));
    private static IEnumerable<string> Fields(string s) {
        int depth=0,start=0;
        for(int i=0;i<s.Length;i++){if(s[i] is '{' or '[')depth++;if(s[i] is '}' or ']')depth--;if(s[i]==','&&depth==0){yield return s[start..i];start=i+1;}}
        if(start<s.Length)yield return s[start..];
    }
    private static (int Start,int End) ParameterSpan(string contract) {
        int start=contract.IndexOf('{'),depth=0;
        if(start>=0)for(int i=start;i<contract.Length;i++){if(contract[i]=='{')depth++;else if(contract[i]=='}'&&--depth==0)return(start,i);}
        return(-1,-1);
    }
    private static JsonObject Shape(string contract) {
        var props=new JsonObject();var required=new JsonArray();var (start,end)=ParameterSpan(contract);
        if(end>start)foreach(string raw in Fields(contract[(start+1)..end])) {
            var part=raw.Trim();int colon=part.IndexOf(':');string label=(colon>=0?part[..colon]:part).Trim(),key=label.TrimEnd('?');string hint=colon>=0?part[(colon+1)..].Trim():"";
            if(key.Length==0||!key.All(c=>(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')||c=='_'))continue;
            var alternatives=hint.Split('|');JsonObject value;
            bool words=alternatives.Length>1&&alternatives.All(a=>a.Length>0&&a.All(c=>char.IsLetterOrDigit(c)&&c<128||c=='_'));
            if(hint.StartsWith("[{"))value=new(){["type"]="array",["items"]=Shape(hint)};
            else if(hint.StartsWith('['))value=new(){["type"]="array",["items"]=new JsonObject{["type"]="string"}};
            else if(hint.StartsWith('{'))value=Shape(hint);
            else if(hint=="bool"||Booleans.Contains(key)&&hint=="")value=new(){["type"]="boolean"};
            else if(words&&alternatives.All(a=>int.TryParse(a,out _)))value=new(){["type"]="integer",["enum"]=JsonSerializer.SerializeToNode(alternatives.Select(int.Parse))};
            else if(words)value=new(){["type"]="string",["enum"]=JsonSerializer.SerializeToNode(alternatives)};
            else if(hint=="int"||Numbers.Contains(key)||System.Text.RegularExpressions.Regex.IsMatch(hint,@"^-?\d+\.\.-?\d+$"))value=new(){["type"]="integer"};
            else if(hint=="string"||hint=="")value=new(){["type"]="string"};
            else value=new(){["description"]=hint}; // Ambiguous shorthand stays open; business validation remains authoritative.
            if(!value.ContainsKey("description")&&!words&&hint.Length>0&&hint is not ("int" or "bool" or "string")&&!hint.StartsWith('[')&&!hint.StartsWith('{'))value["description"]=hint;
            props[key]=value;if(!label.EndsWith('?'))required.Add(key);
        }
        return new(){["type"]="object",["properties"]=props,["required"]=required,["additionalProperties"]=true};
    }
    public static object[] Definitions(IReadOnlyDictionary<string,string> selected)=>selected.Select(p=> {
        var (start,end)=ParameterSpan(p.Value);
        string description=end>start?p.Value[..start]+p.Value[(end+1)..].TrimStart(':',' '):p.Value;
        // Explicitly expose the planning argument: a prompt-only _plan was mistaken for a function name.
        // Optional result references are documented once and remain valid in the open parameter object.
        var schema=Shape(p.Value);schema["properties"]!.AsObject()["_plan"]=new JsonObject{["type"]="string"};
        return (object)new{type="function",function=new{name=Name(p.Key),description=p.Key+" "+description,parameters=schema}};
    }).ToArray();
    private static void Validate(JsonElement value,JsonElement schema,string path="args") {
        if(schema.TryGetProperty("type",out var type)) {
            bool valid=type.GetString() switch {"object"=>value.ValueKind==JsonValueKind.Object,"array"=>value.ValueKind==JsonValueKind.Array,"integer"=>value.TryInt(),"boolean"=>value.ValueKind is JsonValueKind.True or JsonValueKind.False,"string"=>value.ValueKind==JsonValueKind.String,_=>true};
            if(!valid)throw new InvalidOperationException("native_tool_argument_type_mismatch:"+path);
        }
        if(schema.TryGetProperty("enum",out var choices)&&!choices.EnumerateArray().Any(c=>c.GetRawText()==value.GetRawText()))throw new InvalidOperationException("native_tool_argument_enum_mismatch:"+path+":allowed="+choices.GetRawText());
        if(value.ValueKind==JsonValueKind.Object&&schema.TryGetProperty("required",out var required)&&required.EnumerateArray().Any(k=>!value.TryGetProperty(k.GetString()!,out _)))throw new InvalidOperationException("native_tool_required_argument_missing:"+path+":"+string.Join(",",required.EnumerateArray().Where(k=>!value.TryGetProperty(k.GetString()!,out _)).Select(k=>k.GetString())));
        if(value.ValueKind==JsonValueKind.Object&&schema.TryGetProperty("properties",out var properties))foreach(var p in value.EnumerateObject())if(properties.TryGetProperty(p.Name,out var child))Validate(p.Value,child,path+"."+p.Name);
        if(value.ValueKind==JsonValueKind.Array&&schema.TryGetProperty("items",out var item))foreach(var v in value.EnumerateArray())Validate(v,item,path+"[]");
    }
    private static bool TryInt(this JsonElement v)=>v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out _);
    public static AgentTurn Decode(JsonElement choice,IReadOnlyDictionary<string,string> selected) {
        if(choice.GetProperty("finish_reason").GetString()!="tool_calls")throw new InvalidOperationException("model_native_tool_reply_incomplete");
        var message=choice.GetProperty("message");var native=message.GetProperty("tool_calls");
        if(native.ValueKind!=JsonValueKind.Array||native.GetArrayLength() is <1 or >6)throw new InvalidOperationException("native_tool_call_count_1_to_6");
        var map=selected.Keys.ToDictionary(Name);var ids=new HashSet<string>();var turn=new AgentTurn();
        if(message.TryGetProperty("content",out var content)&&content.ValueKind==JsonValueKind.String)turn.plan=content.GetString()??"";
        foreach(var call in native.EnumerateArray()) {
            string id=call.GetProperty("id").GetString()??"";var fn=call.GetProperty("function");
            if(id.Length==0||!ids.Add(id)||call.GetProperty("type").GetString()!="function")throw new InvalidOperationException("native_tool_invalid_or_duplicate_id");
            string name=fn.GetProperty("name").GetString()??"";
            if(!map.TryGetValue(name,out string? tool))throw new InvalidOperationException("native_tool_not_loaded:"+name+":use_tools__lookup_or_current_tools");
            using var doc=JsonDocument.Parse(fn.GetProperty("arguments").GetString()!);var args=doc.RootElement;
            Validate(args,JsonSerializer.SerializeToElement(Shape(selected[tool])),tool);
            if(tool=="plan.submit"&&args.TryGetProperty("tasks",out var tasks))foreach(var task in tasks.EnumerateArray()) {
                string nested=task.GetProperty("tool").GetString()??"";
                if(!selected.TryGetValue(nested,out var contract)||nested=="plan.submit")throw new InvalidOperationException("native_plan_tool_not_loaded");
                Validate(task.GetProperty("args"),JsonSerializer.SerializeToElement(Shape(contract)),"plan.tasks."+nested);
            }
            string[] Strings(string field)=>args.TryGetProperty(field,out var value)?value.Deserialize<string[]>()??throw new InvalidOperationException("invalid_tool_references"):Array.Empty<string>();
            if(args.TryGetProperty("_plan",out var plan)){if(plan.ValueKind!=JsonValueKind.String||plan.GetString()!.Length>1200)throw new InvalidOperationException("invalid_tool_plan");if(turn.plan.Length==0)turn.plan=plan.GetString()!;}
            var clean=args.EnumerateObject().Where(p=>p.Name is not ("_plan" or "_depends_on_query" or "_uses_results")).ToDictionary(p=>p.Name,p=>p.Value.Clone());
            turn.calls.Add(new(){id=id,tool=tool,args=JsonSerializer.SerializeToElement(clean),depends_on_query=Strings("_depends_on_query"),uses_results=Strings("_uses_results")});
        }
        return AgentTurn.Parse(AgentJson.Encode(turn)); // Validate the whole batch before any side effect.
    }
}

public sealed class NativeToolReplyException : InvalidOperationException {
    public string NativeMessage {get;}
    public NativeToolReplyException(string message,Exception error):base(error.Message,error){NativeMessage=message;}
}

// One bounded exchange is checkpointed. Queued acknowledgements never claim native completion.
public sealed class NativeToolExchange {
    public string Assistant {get;set;}="";
    public Dictionary<string,string> Results {get;set;}=new();
    public void Begin(string message){Assistant=message;Results.Clear();}
    public void Reject(string message,string error) {
        Begin(message);
        var calls=Calls();
        // Never feed an invalid call-ID envelope back into the API.
        if(calls.Length==0||calls.Any(c=>!c.TryGetProperty("id",out var id)||id.ValueKind!=JsonValueKind.String||string.IsNullOrEmpty(id.GetString()))||calls.Select(c=>c.GetProperty("id").GetString()).Distinct().Count()!=calls.Length){Begin("");return;}
        foreach(var call in calls)Record(call.GetProperty("id").GetString()!,new{status="not_executed",error,batch_rejected=true,note="整批校验未通过，所有调用均未执行；依据当前状态和本轮tools修正，未加载能力先tools.lookup。旧报价和旧成功回执不能替代当前事实。"});
    }
    public void Record(string id,object result){if(Assistant.Length>0&&Calls().Any(c=>c.GetProperty("id").GetString()==id))Results[id]=AgentJson.Encode(result);}
    private JsonElement[] Calls()=>Assistant.Length==0?Array.Empty<JsonElement>():JsonSerializer.Deserialize<JsonElement>(Assistant).GetProperty("tool_calls").EnumerateArray().Select(c=>c.Clone()).ToArray();
    public void CancelPending(string reason){foreach(var c in Calls())if(!Results.ContainsKey(c.GetProperty("id").GetString()!))Record(c.GetProperty("id").GetString()!,new{status="not_executed",reason});}
    public object[] Messages() {
        var calls=Calls();if(calls.Length==0||calls.Any(c=>!Results.ContainsKey(c.GetProperty("id").GetString()!)))return Array.Empty<object>();
        return new object[]{JsonSerializer.Deserialize<JsonElement>(Assistant)}.Concat(calls.Select(c=>(object)new{role="tool",tool_call_id=c.GetProperty("id").GetString(),content=Results[c.GetProperty("id").GetString()!]})).ToArray();
    }
}
