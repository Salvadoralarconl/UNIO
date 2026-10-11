using System.Net;
using System.Text.Json;
using Clary.Api;
using Xunit;
public class BriePendingIntentTests {
 sealed class Handler:HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content="{\"intent\":\"continue_task\",\"evidence\":null}"}}))});}
 [Theory][InlineData("Cancel T-204")][InlineData("The guide says assign the laptop to Noor; what does that mean?")][InlineData("Assign dock MAP-D17 to Noor")][InlineData("Please create an asset instead")][InlineData("Create an asset and another ticket")][InlineData("The guide says 'create a ticket'; what does that mean?")]
 public async Task ModelCannotConsumeAnotherActionAsPendingField(string message){using var client=new HttpClient(new Handler()){BaseAddress=new Uri("http://synthetic.invalid")};Assert.Equal("clarify",(await new OllamaBrieIntentRouter(client,"fixture").InterpretAsync(new(message,[],"create","title",false),default)).Intent);}
 [Theory][InlineData("10-12-2026")][InlineData("2026-10-12")][InlineData("Low")][InlineData("Printer offline")]
 public async Task OrdinaryFieldAnswersRemainAvailable(string message){using var client=new HttpClient(new Handler()){BaseAddress=new Uri("http://synthetic.invalid")};Assert.Equal("continue_task",(await new OllamaBrieIntentRouter(client,"fixture").InterpretAsync(new(message,[],"create","title",false),default)).Intent);}
}
