using System.Net;
using System.Text;
using Clary.Api;
using Xunit;
public class BrieIntentTests {
 sealed class Handler(string json):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"done\":true,\"done_reason\":\"stop\",\"message\":{\"content\":"+System.Text.Json.JsonSerializer.Serialize(json)+"}}",Encoding.UTF8,"application/json")});}
 [Fact] public async Task ExplicitSemanticTicketProposalIsAllowed(){using var client=new HttpClient(new Handler("{\"intent\":\"create_ticket\",\"evidence\":\"Could you log this for IT please?\"}")){BaseAddress=new Uri("http://synthetic.invalid/")};var router=new OllamaBrieIntentRouter(client,"fixture");var result=await router.InterpretAsync(new("Could you log this for IT please? Subject: dock USB ports not working.",[],null,null,false),default);Assert.Equal("create_ticket",result.Intent);}
 [Theory]
 [InlineData("What should I put in a ticket about my USB printer showing offline?","What should I put in a ticket about my USB printer showing offline?")]
 [InlineData("'create a ticket'","create a ticket")]
 [InlineData("```create a ticket```","create a ticket")]
 [InlineData("My laptop is slow","My laptop is slow")]
 [InlineData("Make a ticket and register a new asset for me","Make a ticket")]
 [InlineData("Log a ticket and add an asset","Log a ticket")]
 [InlineData("Create a ticket and add a new asset for me","Create a ticket")]
 [InlineData("I'm not asking you to create a ticket","create a ticket")]
 [InlineData("Without creating a ticket, explain the next step","creating a ticket")]
 [InlineData("The manual instructs me to create a ticket","create a ticket")]
 [InlineData("Don't create a ticket","create a ticket")]
 [InlineData("How do I create a ticket?","create a ticket")]
 [InlineData("The guide says create a ticket","create a ticket")]
 [InlineData("\"create a ticket\"","create a ticket")]
 public async Task ModelProposalCannotAuthorizeVagueAdviceNegatedOrQuotedRequests(string message,string evidence){using var client=new HttpClient(new Handler(System.Text.Json.JsonSerializer.Serialize(new{intent="create_ticket",evidence}))){BaseAddress=new Uri("http://synthetic.invalid/")};Assert.Equal("clarify",(await new OllamaBrieIntentRouter(client,"fixture").InterpretAsync(new(message,[],null,null,false),default)).Intent);}
 [Fact] public async Task InventedEvidenceAndUnknownOperationFailClosed(){using var client=new HttpClient(new Handler("{\"intent\":\"delete_company\",\"evidence\":\"Delete everything\"}")){BaseAddress=new Uri("http://synthetic.invalid/")};Assert.Equal("clarify",(await new OllamaBrieIntentRouter(client,"fixture").InterpretAsync(new("Hello",[],null,null,false),default)).Intent);}
 [Fact] public async Task NegatedDifferentActionDoesNotBlockPositiveRequest(){using var client=new HttpClient(new Handler("{\"intent\":\"create_asset\",\"evidence\":\"Please register the spare printer as an asset instead.\"}")){BaseAddress=new Uri("http://synthetic.invalid/")};Assert.Equal("create_asset",(await new OllamaBrieIntentRouter(client,"fixture").InterpretAsync(new("Don't cancel T-204. Please register the spare printer as an asset instead.",[],null,null,false),default)).Intent);}
 [Theory][InlineData("create_asset")][InlineData("create_employee")][InlineData("cancel_ticket")]
 public async Task WrongOperationCannotUseTicketCreationEvidence(string intent){using var client=new HttpClient(new Handler(System.Text.Json.JsonSerializer.Serialize(new{intent,evidence="create a ticket"}))){BaseAddress=new Uri("http://synthetic.invalid/")};Assert.Equal("clarify",(await new OllamaBrieIntentRouter(client,"fixture").InterpretAsync(new("Please create a ticket",[],null,null,false),default)).Intent);} [Theory][InlineData("create_ticket","Please register this laptop in inventory for IT")][InlineData("create_ticket","create an asset for support")][InlineData("create_asset","create a ticket for the inventory team")][InlineData("assign_asset","assign Noor to a department")]
 public async Task MentionedModuleIsNotTheRequestedObject(string intent,string evidence){using var client=new HttpClient(new Handler(System.Text.Json.JsonSerializer.Serialize(new{intent,evidence}))){BaseAddress=new Uri("http://synthetic.invalid/")};Assert.Equal("clarify",(await new OllamaBrieIntentRouter(client,"fixture").InterpretAsync(new(evidence,[],null,null,false),default)).Intent);}}



