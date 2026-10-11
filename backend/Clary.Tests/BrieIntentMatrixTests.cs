using System.Net;
using System.Text.Json;
using Clary.Api;
using Xunit;
public class BrieIntentMatrixTests {
 static readonly string[] Operations=["create_ticket","create_asset","create_employee","assign_asset","cancel_ticket"];
 public static IEnumerable<object[]> Matrix(){
  (string operation,string phrase)[] requests=[
   ("create_ticket","create a ticket"),("create_ticket","create a ticket for the inventory team"),("create_ticket","log this for IT"),
   ("create_asset","create an asset"),("create_asset","create an asset for the ticket team"),("create_asset","register this laptop in inventory for support"),
   ("create_employee","add Noor to the employee directory"),("create_employee","create an employee for the ticket team"),
   ("assign_asset","assign dock MAP-D17 to Jo Kim"),("assign_asset","assign the asset to the employee in support"),
   ("cancel_ticket","cancel ticket T-204"),("cancel_ticket","cancel ticket T-204 about inventory")];
  foreach(var request in requests)foreach(var proposed in Operations)yield return [request.operation,request.phrase,proposed];
 }
 sealed class Handler(string proposal):HttpMessageHandler {
  public JsonElement Request;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Request=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content=proposal}}))};}
 }
 [Theory][MemberData(nameof(Matrix))]public async Task ProposalMustMatchRequestedObject(string authorized,string phrase,string proposed){using var client=new HttpClient(new Handler(JsonSerializer.Serialize(new{intent=proposed,evidence=phrase}))){BaseAddress=new Uri("http://synthetic.invalid")};var result=await new OllamaBrieIntentRouter(client,"fixture").InterpretAsync(new(phrase,[],null,null,false),default);Assert.Equal(proposed==authorized?authorized:"clarify",result.Intent);}
 [Theory][InlineData("10-12-2026")][InlineData("2026-10-12")][InlineData("Low")][InlineData("Please create an asset instead")][InlineData("Create an asset and another ticket")]
 public async Task PendingSchemaCannotProposeANewBusinessAction(string message){var handler=new Handler("{\"intent\":\"clarify\",\"evidence\":null}");using var client=new HttpClient(handler){BaseAddress=new Uri("http://synthetic.invalid")};await new OllamaBrieIntentRouter(client,"fixture").InterpretAsync(new(message,[],"create","dueDate",false),default);var labels=handler.Request.GetProperty("format").GetProperty("properties").GetProperty("intent").GetProperty("enum").EnumerateArray().Select(x=>x.GetString()).ToArray();Assert.Contains("continue_task",labels);Assert.Contains("pause_task",labels);Assert.DoesNotContain("documented_help",labels);Assert.DoesNotContain("advice",labels);foreach(var operation in Operations)Assert.DoesNotContain(operation,labels);}
}
