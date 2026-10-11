using System.Net;
using System.Text;
using System.Text.Json;
using Clary.Api;
using Xunit;
public class BrieSemanticAuthorityTests {
 sealed class Replies(params string[] replies):HttpMessageHandler {
  int index;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
   var json=replies[Math.Min(index++,replies.Length-1)];
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content=json}}),Encoding.UTF8,"application/json")});
  }
 }
 [Theory]
 [InlineData("Please put dock MAP-D17 under Mina Park.","assign_asset")]
 [InlineData("Assign MAP-D17 to Jo Kim, please.","assign_asset")]
 [InlineData("Please add that person to the directory now.","create_employee")]
 public async Task IndependentlyVerifiedSemanticRequestDoesNotRequireCanonicalVerbOrObject(string message,string intent){
  using var http=new HttpClient(new Replies(JsonSerializer.Serialize(new{intent,evidence=message}),JsonSerializer.Serialize(new{operation=intent,requestKind="execute",evidence=message}))){BaseAddress=new Uri("http://synthetic.invalid/")};
  var result=await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new(message,["The person is Harper Lane."],null,null,false),default);
  Assert.Equal(intent,result.Intent);
 }
 [Theory]
 [InlineData("explain","assign_asset")]
 [InlineData("multiple","assign_asset")]
 [InlineData("execute","create_employee")]
 [InlineData("uncertain","assign_asset")]
 public async Task DisagreementOrNonExecutionNeverAuthorizes(string kind,string operation){
  const string message="Please put dock MAP-D17 under Mina Park.";
  using var http=new HttpClient(new Replies(JsonSerializer.Serialize(new{intent="assign_asset",evidence=message}),JsonSerializer.Serialize(new{operation,requestKind=kind,evidence=message}))){BaseAddress=new Uri("http://synthetic.invalid/")};
  Assert.Equal("clarify",(await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new(message,[],null,null,false),default)).Intent);
 }
 [Theory]
 [InlineData("The note says \"put dock MAP-D17 under Mina Park\".","put dock MAP-D17 under Mina Park")]
 [InlineData("Do not put dock MAP-D17 under Mina Park.","put dock MAP-D17 under Mina Park")]
 [InlineData("How do I put dock MAP-D17 under Mina Park?","put dock MAP-D17 under Mina Park")]
 public async Task ASecondModelCannotOverrideQuotedNegatedOrAdviceEvidence(string message,string evidence){
  using var http=new HttpClient(new Replies(JsonSerializer.Serialize(new{intent="assign_asset",evidence}),JsonSerializer.Serialize(new{operation="assign_asset",requestKind="execute",evidence}))){BaseAddress=new Uri("http://synthetic.invalid/")};
  Assert.Equal("clarify",(await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new(message,[],null,null,false),default)).Intent);
 }
 [Theory]
 [InlineData("cancel T-204","create_ticket")]
 [InlineData("assign MAP-D17 to Jo Kim","create_asset")]
 [InlineData("create an asset","assign_asset")]
 [InlineData("add an asset","assign_asset")]
 [InlineData("create an employee","cancel_ticket")]
 [InlineData("create a ticket for the inventory team","create_asset")]
 public async Task TwoIncorrectModelsCannotOverrideExplicitDifferentOperation(string evidence,string intent){
  using var http=new HttpClient(new Replies(JsonSerializer.Serialize(new{intent,evidence}),JsonSerializer.Serialize(new{operation=intent,requestKind="execute",evidence}))){BaseAddress=new Uri("http://synthetic.invalid/")};
  Assert.Equal("clarify",(await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new(evidence,["Prior user facts"],null,null,false),default)).Intent);
 } sealed class Capture:HttpMessageHandler {
  public string[] Allowed=[];
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
   using var parsed=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
   Allowed=parsed.RootElement.GetProperty("format").GetProperty("properties").GetProperty("intent").GetProperty("enum").EnumerateArray().Select(x=>x.GetString()!).ToArray();
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content="{\"intent\":\"clarify\",\"evidence\":null}"}}))};
  }
 }
 [Fact] public async Task TaskControlIsNotAnAvailableIntentWithoutAPendingTask(){
  var handler=new Capture();using var http=new HttpClient(handler){BaseAddress=new Uri("http://synthetic.invalid/")};
  await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new("Leave the equipment alone; cancel the support ticket.",[],null,null,false),default);
  Assert.DoesNotContain("pause_task",handler.Allowed);Assert.DoesNotContain("continue_task",handler.Allowed);Assert.Contains("cancel_ticket",handler.Allowed);
 }}


