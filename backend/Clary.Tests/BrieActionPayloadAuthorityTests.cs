using System.Net;
using System.Text.Json;
using Clary.Api;
using Xunit;
public class BrieActionPayloadAuthorityTests {
 sealed class Model(string intent,string evidence):HttpMessageHandler {
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
   var body=await request.Content!.ReadAsStringAsync(ct);
   var result=body.Contains("requestKind")?JsonSerializer.Serialize(new{operation=intent,requestKind="execute",evidence}):JsonSerializer.Serialize(new{intent,evidence});
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content=result}}))};
  }
 }
 [Theory]
 [InlineData("Log this please: title Slow laptop; details Apps respond slowly.","create_ticket","create_ticket")]
 [InlineData("File this for support: description The employee cannot sign in.","create_ticket","create_ticket")]
 [InlineData("Create a ticket: title Inventory laptop issue.","create_asset","clarify")]
 [InlineData("Register an asset: description Ticket for printer support.","create_ticket","clarify")]
 [InlineData("Do not log this: title Slow laptop.","create_ticket","clarify")]
 [InlineData("The note says log this: title Slow laptop.","create_ticket","clarify")]
 [InlineData("How do I log this: title Slow laptop?","create_ticket","clarify")]
 [InlineData("Create \"Support: name pending\" in the employee directory.","create_ticket","clarify")]
 [InlineData("Create \"Support: name pending\" in the employee directory.","create_employee","create_employee")]
 [InlineData("Create \"Support; name pending\" in the employee directory.","create_ticket","clarify")]
 [InlineData("Create \"Support. name pending\" in the employee directory.","create_employee","create_employee")]
 [InlineData("Create ‘Support: name pending’ in the employee directory.","create_ticket","clarify")]
 [InlineData("Create ‘Support: name pending’ in the employee directory.","create_employee","create_employee")]
 [InlineData("Create «Support: name pending» in the employee directory.","create_ticket","clarify")]
 [InlineData("Create \"Support: name pending in the employee directory.","create_ticket","clarify")]
 public async Task FieldPayloadDoesNotRedefineRequestedOperation(string message,string proposed,string expected){
  using var http=new HttpClient(new Model(proposed,message)){BaseAddress=new Uri("http://fixture.invalid/")};
  var result=await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new(message,[],null,null,false),default);
  Assert.Equal(expected,result.Intent);
 }
}
