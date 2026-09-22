using System.Net;
using System.Text;
using System.Text.Json;
using Together;
public static class ModelBudgetChecks {
    private sealed class FakeUsage:HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"usage\":{\"total_tokens\":25000}}")});
    }
    public static void Run(Action<bool,string> check) {
        string folder=Path.Combine(Path.GetTempPath(),"together-budget-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try {
            ModelRequestBudget.Configure(Path.Combine(folder,"usage.json"),0,16000);using var client=new HttpClient(new FakeUsage());
            for(int n=0;n<2;n++){using var req=new HttpRequestMessage(HttpMethod.Post,"https://invalid.local"){Content=new StringContent("{\"max_tokens\":32}",Encoding.UTF8,"application/json")};using var reply=ModelRequestBudget.SendAsync(client,req).GetAwaiter().GetResult();}
            var status=JsonSerializer.SerializeToElement(ModelRequestBudget.Status());
            check(status.GetProperty("unlimited").GetBoolean()&&status.GetProperty("reported_tokens").GetInt64()==50000&&status.GetProperty("requests").GetInt32()==2,"explicit unlimited token budget preserves real usage accounting");
            ModelRequestBudget.Configure(Path.Combine(folder,"bounded.json"),10000,16000);bool rejected=false;
            using var large=new HttpRequestMessage(HttpMethod.Post,"https://invalid.local"){Content=new StringContent("{\"max_tokens\":3000}")};
            try{using var reply=ModelRequestBudget.SendAsync(client,large).GetAwaiter().GetResult();}catch(InvalidOperationException e){rejected=e.Message=="daily_model_token_reservation_budget_exceeded";}
            check(rejected,"configured finite budget still rejects excessive reservations");
        }finally{Directory.Delete(folder,true);}
    }
}
