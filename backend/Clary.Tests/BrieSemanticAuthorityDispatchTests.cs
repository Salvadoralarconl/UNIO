using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clary.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
public class BrieSemanticAuthorityDispatchTests {
 sealed class Handler:HttpMessageHandler {
  public int Calls;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
   var reply=++Calls==1?JsonSerializer.Serialize(new{intent="create_employee",evidence="Please add that person to the directory now."}):JsonSerializer.Serialize(new{operation="create_employee",requestKind="execute",evidence="Please add that person to the directory now."});
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content=reply}}))});
  }
 }
 sealed class Parser:IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct)=>Task.FromResult(new BrieTicketPatch());}
 [Fact] public async Task VerifiedSemanticIntentReachesAuthenticatedBusinessFlowAndReplaySkipsModels(){
  var handler=new Handler();using var http=new HttpClient(handler){BaseAddress=new Uri("http://synthetic.invalid/")};
  await IdentityFixture.Run(async f=>{
   await f.Login();var before=await f.Scalar("SELECT count(*) FROM employees");
   var request=new BrieMessage("Please add that person to the directory now.",Guid.NewGuid());
   var response=await f.Send(HttpMethod.Post,"/api/brie/messages",request);response.EnsureSuccessStatusCode();
   var answer=(await response.Content.ReadFromJsonAsync<Answer>())!;
   Assert.Equal("clarify",answer.TicketTask?.Status);Assert.Contains("name",answer.TicketTask!.Message);Assert.Equal(2,handler.Calls);
   Assert.Equal(before,await f.Scalar("SELECT count(*) FROM employees"));
   var repeated=await f.Send(HttpMethod.Post,"/api/brie/messages",request);repeated.EnsureSuccessStatusCode();
   Assert.Equal(answer.TicketTask.TaskId,(await repeated.Content.ReadFromJsonAsync<Answer>())!.TicketTask!.TaskId);Assert.Equal(2,handler.Calls);
  },b=>{b.Services.AddSingleton<IBrieIntentRouter>(new OllamaBrieIntentRouter(http,"fixture"));b.Services.AddSingleton<IBrieTicketParser>(new Parser());b.Services.AddSingleton<BrieTickets>();});
 }
 [Fact] public async Task VerifiedDecisionCannotBeReboundToAnotherMessageOrOperation(){
  using var http=new HttpClient(new Handler()){BaseAddress=new Uri("http://synthetic.invalid/")};
  const string message="Please add that person to the directory now.";
  var decision=await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new(message,[],null,null,false),default);
    bool Authorizes(BrieIntentDecision d,string text)=>(bool)typeof(BrieIntentDecision).GetMethod("Authorizes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(d,[text])!;
  Assert.True(Authorizes(decision,message));Assert.False(Authorizes(decision,"Do not add that person to the directory now."));
  Assert.False(Authorizes(decision with{Intent="cancel_ticket"},message));
  var roundtrip=JsonSerializer.Deserialize<BrieIntentDecision>(JsonSerializer.Serialize(decision))!;
  Assert.False(Authorizes(roundtrip,message));
 }
}

