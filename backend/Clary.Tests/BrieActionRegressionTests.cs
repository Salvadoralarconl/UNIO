using System.Net;
using System.Text;
using System.Text.Json;
using Clary.Api;
using Clary.Api.Directories;
using Xunit;
public class BrieActionRegressionTests {
 sealed class Model(string intent,string evidence):HttpMessageHandler {
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
   var body=await request.Content!.ReadAsStringAsync(ct);var content=body.Contains("requestKind")?JsonSerializer.Serialize(new{operation=intent,requestKind="execute",evidence}):JsonSerializer.Serialize(new{intent,evidence});
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content}}),Encoding.UTF8,"application/json")};
  }
 }
 [Theory]
 [InlineData("Create a ticket titled Screen dark. Make it due tomorrow.")]
 [InlineData("Create a ticket named Network drops. Make it due next month.")]
 public async Task DateClauseIsNotASecondITOperation(string text){using var http=new HttpClient(new Model("create_ticket",text)){BaseAddress=new Uri("http://fixture.invalid")};Assert.Equal("create_ticket",(await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new(text,[],null,null,false),default)).Intent);}
 [Theory]
 [InlineData("Register the spare dock as Loaner, tag D-7, and open a ticket called Dock fails.","Register the spare dock as Loaner")]
 [InlineData("Register a laptop and assign it to Mina.","Register a laptop")]
 public async Task ExplicitEquipmentAndSecondOperationRequiresSequencing(string text,string evidence){using var http=new HttpClient(new Model("create_asset",evidence)){BaseAddress=new Uri("http://fixture.invalid")};Assert.Equal("clarify",(await new OllamaBrieIntentRouter(http,"fixture").InterpretAsync(new(text,[],null,null,false),default)).Intent);}
 sealed class Parser:IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct)=>Task.FromResult(new BrieTicketPatch(Target:"EMAIL-1",Employee:"Sam"));public Task<BrieTicketPatch> ParseAsync(BrieFieldInput input,CancellationToken ct)=>Task.FromResult(input.PendingField=="employee"?new BrieTicketPatch(Employee:input.Message.Split(' ').Last().TrimEnd('.')):new BrieTicketPatch(Target:"EMAIL-1",Employee:"Sam"));}
 [Theory][InlineData(false)][InlineData(true)]
 public async Task ExactEmployeeEmailResolvesWithinCurrentCompanyOnly(bool foreign)=>await KnowledgeIsolationFixture.Run(async f=>{
  var dirs=new DirectoryRepository(f.Db);await dirs.SaveEmployeeAsync(f.A,null,new("Sam","sam.one@example.test"),default);var intended=await dirs.SaveEmployeeAsync(f.A,null,new("Sam","sam.two@example.test"),default);await dirs.SaveEmployeeAsync(f.B,null,new("Sam","sam.foreign@example.test"),default);
  var inventory=new InventoryRepository(f.Db);var asset=await inventory.CreateAsync(f.A,new("EMAIL-1","Laptop",Status:"Available"),default);var svc=new BrieTickets(f.Db,new(f.Db),new Parser());
  var first=await svc.ReplyAsync(f.A,new("Assign asset EMAIL-1 to Sam",Guid.NewGuid()),default);Assert.Equal("clarify",first.Status);Assert.Equal(2,first.Choices!.Count);
  var email=foreign?"sam.foreign@example.test":"sam.two@example.test";var last=await svc.ReplyAsync(f.A,new("Use employee "+email,Guid.NewGuid(),TaskId:first.TaskId),default);
  Assert.Equal(foreign?"clarify":"completed",last.Status);Assert.Equal(foreign?(Guid?)null:intended.Id,(await inventory.GetAsync(f.A,asset.Id,default))!.EmployeeId);
 });
}
