using Clary.Api;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
public class TicketTemplateTests {
 [Fact] public async Task TemplatesSaveReusableInstructionsAndChecklistAndRejectViewerWrites()=>await IdentityFixture.Run(async f=>{
  await f.Login();var response=await f.Send(HttpMethod.Post,"/api/tickets/templates",new{name="Onboarding",type="Onboarding",instructions="Collect equipment and configure accounts.",assignmentMode="All",assigneeIds=Array.Empty<Guid>(),documentIds=Array.Empty<Guid>(),checklist=new[]{"Collect equipment","Test access"}});
  Assert.Equal(HttpStatusCode.OK,response.StatusCode);var template=await response.Content.ReadFromJsonAsync<JsonElement>();var id=template.GetProperty("id").GetGuid();Assert.Equal(2,template.GetProperty("checklist").GetArrayLength());
  var list=await f.Send(HttpMethod.Get,"/api/tickets/templates",new{});Assert.Equal(HttpStatusCode.OK,list.StatusCode);Assert.Contains("Onboarding",await list.Content.ReadAsStringAsync());
  var duplicate=await f.Send(HttpMethod.Post,"/api/tickets/templates",new{name="onboarding",type="Support",instructions="",assignmentMode="All"});Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
  await f.Sql($"UPDATE memberships SET role='Viewer' WHERE user_id='{IdentityFixture.User}'");
  var denied=await f.Send(HttpMethod.Put,$"/api/tickets/templates/{id}",new{name="Changed",type="Support",instructions="",assignmentMode="All",checklist=Array.Empty<string>(),revision=1});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
 });
 [Fact] public async Task ChecklistChecksRecordActingProfileAndRevision()=>await IdentityFixture.Run(async f=>{
  await f.Login();var response=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Task",type="Support",dueDate="2026-10-14",assignmentMode="All",checklist=new[]{"Confirm access"}});Assert.Equal(HttpStatusCode.OK,response.StatusCode);var ticket=(await response.Content.ReadFromJsonAsync<JsonElement>())!;var id=ticket.GetProperty("id").GetGuid();var item=ticket.GetProperty("checklist")[0].GetProperty("id").GetGuid();
  var checkedItem=await f.Send(HttpMethod.Put,$"/api/tickets/{id}/checklist/{item}",new{completed=true,revision=1});Assert.Equal(HttpStatusCode.OK,checkedItem.StatusCode);var result=await checkedItem.Content.ReadFromJsonAsync<JsonElement>();Assert.True(result.GetProperty("checklist")[0].GetProperty("completed").GetBoolean());Assert.Equal("Development user",result.GetProperty("checklist")[0].GetProperty("completedBy").GetString());
  Assert.Equal(HttpStatusCode.Conflict,(await f.Send(HttpMethod.Put,$"/api/tickets/{id}/checklist/{item}",new{completed=false,revision=1})).StatusCode);
 });
}

