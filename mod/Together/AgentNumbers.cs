using System.Globalization;
using System.Text.Json;
namespace Together;
public static class AgentNumbers {
    public static int Read(JsonElement args,string key,int fallback=0) {
        if(args.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("arguments_must_be_object");
        if(!args.TryGetProperty(key,out var value))return fallback;
        if(value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int n))return n;
        if(value.ValueKind==JsonValueKind.String&&int.TryParse(value.GetString(),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out n))return n;
        throw new InvalidOperationException("parameter_requires_integer:"+key);
    }
}
