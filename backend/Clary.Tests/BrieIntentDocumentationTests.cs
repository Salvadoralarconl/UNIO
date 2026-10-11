using Clary.Api;
using System.Net;
using System.Text.Json;
using Xunit;
public class BrieIntentDocumentationTests {
 sealed class Handler:HttpMessageHandler {public string Prompt="";protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){using var body=JsonDocument.Parse(await r.Content!.ReadAsStringAsync(ct));Prompt=body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content="{\"intent\":\"documented_help\",\"evidence\":null}"}}))};}}
 [Fact] public async Task ExplicitDocumentationQuestionsAreNotAmbiguousActionRequests(){var handler=new Handler();using var http=new HttpClient(handler){BaseAddress=new Uri("http://synthetic.invalid")};var result=await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new("My computer is connected to Wi-Fi but cannot browse. What should I record according to the approved Wi-Fi guides?",[],null,null,false),default);Assert.Contains("Questions about what approved documentation says are documented_help",handler.Prompt);Assert.Equal("documented_help",result.Intent);}
}
