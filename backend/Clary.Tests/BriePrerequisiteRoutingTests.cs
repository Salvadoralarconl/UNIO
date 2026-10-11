using Clary.Api;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;
public class BriePrerequisiteRoutingTests {
 sealed class Router:IBrieIntentRouter {public int Calls;public Task<BrieIntentDecision> InterpretAsync(BrieIntentContext c,CancellationToken ct){Calls++;return Task.FromResult(new BrieIntentDecision(Calls==1?"documented_help":"clarify"));}}
 sealed class Parser:IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string s,CancellationToken ct)=>Task.FromResult(new BrieTicketPatch());}
 [Theory]
 [InlineData("yes",true,false,true,true)]
 [InlineData("yes",true,false,true,true,"Completion of the source prerequisite: Verify identity")]
 [InlineData("no",true,false,true,true)]
 [InlineData("I don't know",true,false,true,true)]
 [InlineData("yes",false,false,true,false)]
 [InlineData("yes",true,true,true,false)]
 [InlineData("yes",true,false,false,false)]
 [InlineData("New issue: my printer failed",true,false,true,false)]
 [InlineData("Create a ticket",true,false,true,false)]
 public async Task OnlyServerBoundPendingPrerequisiteReplyBypassesIntent(string reply,bool state,bool confirmed,bool question,bool routed,string condition="an external display works"){
  var router=new Router();var calls=0;
  await IdentityFixture.Run(async f=>{await f.Login();var first=(await(await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage("The laptop screen is black",Guid.NewGuid()))).Content.ReadFromJsonAsync<Answer>())!;
   var next=await f.Send(HttpMethod.Post,"/api/brie/messages",new BrieMessage(reply,Guid.NewGuid(),first.ConversationId));Assert.True(next.IsSuccessStatusCode);Assert.Equal(routed?2:1,calls);Assert.Equal(routed?1:2,router.Calls);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM brie_ticket_tasks"));
  },b=>{b.Services.AddSingleton<IBrieIntentRouter>(router);b.Services.AddSingleton<IBrieTicketParser>(new Parser());b.Services.AddSingleton<BrieTickets>();b.Services.AddSingleton(sp=>new ConversationStore((c,q,ctx,ct)=>{
   calls++;return Task.FromResult(calls>1?new Answer("insufficient",[],[],[],"Test",null):new Answer("clarify",[question?SourcePrerequisites.QuestionFor(condition):"Which device?"],[],[],"Test",null){Prerequisite=state?new SourcePrerequisite(Guid.NewGuid(),Guid.NewGuid(),"Synthetic source",new AdviceStep("If an external display works, record the backlight.",[]),condition,confirmed?true:null):null});
  },"Test",sp.GetRequiredService<ConversationRepository>(),documentationDriven:true));});
 }
}

