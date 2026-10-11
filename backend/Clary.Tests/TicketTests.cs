using Clary.Api;
using Clary.Api.Identity;
using System.Net;
using System.Net.Http.Json;
using Xunit;
public class TicketTests {
 [Fact] public async Task TicketAttachmentsRespectAssignmentAndCanBeDownloadedAndRemoved()=>await IdentityFixture.Run(async f=>{
  await f.Login();var c=InventoryAuditTests.Context;var repo=new TicketRepository(f.Database);
  var viewer=c with{UserId=Guid.NewGuid(),Role=CompanyRole.Viewer};await FixtureCompany.AddActorAsync(f.Database,viewer);
  var outsider=c with{UserId=Guid.NewGuid(),Role=CompanyRole.Viewer};await FixtureCompany.AddActorAsync(f.Database,outsider);
  var ticket=await repo.SaveAsync(c,null,new("Attachment test",null,"Support",new(2026,10,14),AssigneeIds:[viewer.UserId]),default);
  var file=await repo.AddAttachmentAsync(viewer,ticket.Id,"C:\\fakepath\\handover.txt",[1,2,3],default);Assert.Equal("handover.txt",file.Filename);
  Assert.Single(await repo.AttachmentsAsync(viewer,ticket.Id,default));Assert.Equal(new byte[]{1,2,3},(await repo.OpenAttachmentAsync(viewer,ticket.Id,file.Id,default)).Content);
  await Assert.ThrowsAsync<InventoryMissing>(()=>repo.OpenAttachmentAsync(outsider,ticket.Id,file.Id,default));
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.AddAttachmentAsync(viewer,ticket.Id,"empty.txt",[],default));
  await repo.RemoveAttachmentAsync(viewer,ticket.Id,file.Id,default);Assert.Empty(await repo.AttachmentsAsync(c,ticket.Id,default));
 });
 [Fact] public async Task TicketPriorityPersistsAndRejectsUnknownValues()=>await IdentityFixture.Run(async f=>{
  await f.Login();
  var created=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Urgent repair",type="Support",dueDate="2026-10-14",priority="Urgent"});
  Assert.Equal(HttpStatusCode.OK,created.StatusCode);
  var ticket=await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();Assert.Equal("Urgent",ticket.GetProperty("priority").GetString());
  var invalid=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Invalid priority",type="Support",dueDate="2026-10-14",priority="Invalid"});Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
 });
 [Fact] public async Task SetupTicketAppearsOnceAndCanBeDeleted()=>await IdentityFixture.Run(async f=>{
  await f.Login();var repo=new TicketRepository(f.Database);var c=InventoryAuditTests.Context;
  await f.Sql($"SELECT seed_workspace_setup('{IdentityFixture.Company}','{IdentityFixture.User}')");
  await f.Sql($"SELECT seed_workspace_setup('{IdentityFixture.Company}','{IdentityFixture.User}')");
  var page=await repo.ListAsync(c,new(),default);var setup=Assert.Single(page.Items.Where(t=>t.Title=="Set up your workspace"));
  Assert.Equal(7,setup.Checklist.Count);Assert.Contains("delete",setup.AllowedActions);Assert.Equal("Assigned",setup.Status);
  var viewer=c with{UserId=Guid.NewGuid(),Role=CompanyRole.Viewer};await FixtureCompany.AddActorAsync(f.Database,viewer);
  Assert.Null(await repo.GetAsync(viewer,setup.Id,default));
  await repo.ActionAsync(c,setup.Id,new("delete",setup.Revision),default);
  await f.Sql($"SELECT seed_workspace_setup('{IdentityFixture.Company}','{IdentityFixture.User}')");
  Assert.Null(await repo.GetAsync(c,setup.Id,default));Assert.DoesNotContain((await repo.ListAsync(c,new(),default)).Items,t=>t.Id==setup.Id);
 });
 [Fact] public async Task NamedAndAllAssignmentsRespectMembershipAndPreserveActorSnapshots()=>await IdentityFixture.Run(async f=>{
  await f.Login();var admin=InventoryAuditTests.Context;var viewer=admin with{UserId=Guid.NewGuid(),Role=CompanyRole.Viewer};await FixtureCompany.AddActorAsync(f.Database,viewer);
  var repo=new TicketRepository(f.Database);var named=await repo.SaveAsync(admin,null,new("Named","Details","Support",new(2026,10,14),AssigneeIds:[viewer.UserId]),default);
  Assert.NotNull(await repo.GetAsync(viewer,named.Id,default));var comment=await repo.AddCommentAsync(viewer,named.Id,new("Testing"),default);Assert.Equal("Fixture actor",comment.AuthorName);
  var outsider=admin with{UserId=Guid.NewGuid(),Role=CompanyRole.Viewer};await FixtureCompany.AddActorAsync(f.Database,outsider);Assert.Null(await repo.GetAsync(outsider,named.Id,default));await Assert.ThrowsAsync<InventoryMissing>(()=>repo.AddCommentAsync(outsider,named.Id,new("No access"),default));
  var all=await repo.SaveAsync(admin,null,new("Shared",null,"Support",new(2026,10,14),"All"),default);Assert.NotNull(await repo.GetAsync(outsider,all.Id,default));
  await f.Sql($"UPDATE users SET display_name='Renamed profile' WHERE id='{viewer.UserId}'");var feed=await repo.FeedAsync(admin,named.Id,true,30,null,default);Assert.Equal("Fixture actor",feed.Items[0].GetProperty("authorName").GetString());
  await f.Sql($"UPDATE memberships SET active=false WHERE user_id='{viewer.UserId}'");await Assert.ThrowsAsync<CompanyDenied>(()=>repo.AddCommentAsync(viewer,named.Id,new("Denied"),default));
 });
 [Fact] public async Task StandaloneTicketCreationCommentsAndApprovalRecordActor()=>await IdentityFixture.Run(async f=>{
  await f.Login();await f.Sql($"UPDATE users SET display_name='Taylor Smith' WHERE id='{IdentityFixture.User}'");
  var response=await f.Send(HttpMethod.Post,"/api/tickets",new{title="Install standard apps",description="Prepare the workstation",type="Support",dueDate="2026-10-14",assignmentMode="All",assigneeIds=Array.Empty<Guid>(),assetLinks=Array.Empty<object>(),documentIds=Array.Empty<Guid>()});
  Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  var ticket=await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();var id=ticket.GetProperty("id").GetGuid();
  Assert.Equal("Taylor Smith",ticket.GetProperty("creatorName").GetString());
  var comment=await f.Send(HttpMethod.Post,$"/api/tickets/{id}/comments",new{body="Ready to start"});Assert.Equal(HttpStatusCode.OK,comment.StatusCode);
  var approve=await f.Send(HttpMethod.Post,$"/api/tickets/{id}/actions",new{action="approve",revision=1});Assert.Equal(HttpStatusCode.OK,approve.StatusCode);
  var approved=await approve.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();Assert.Equal("Assigned",approved.GetProperty("status").GetString());Assert.Equal("Taylor Smith",approved.GetProperty("approverName").GetString());Assert.NotEqual(System.Text.Json.JsonValueKind.Null,approved.GetProperty("approvedAt").ValueKind);
 });
 [Fact] public async Task CanonicalSchemaSupportsStandaloneTicketsAndTenantLinks()=>await IdentityFixture.Run(async f=>{
  var id=Guid.NewGuid();
  await f.Sql($"INSERT INTO equipment_tickets(id,company_id,title,state,type,due_date,creator_user_id,creator_name) VALUES('{id}','{IdentityFixture.Company}','Support task','Pending','Support','2026-10-14','{IdentityFixture.User}','Bootstrap')");
  Assert.Equal(1L,await f.Scalar($"SELECT count(*) FROM equipment_tickets WHERE id='{id}' AND request_id IS NULL"));
  foreach(var state in new[]{"Pending","Approved","Assigned","In Progress","Complete","Canceled"})await f.Sql($"UPDATE equipment_tickets SET state='{state}' WHERE id='{id}'");
  Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM ticket_asset_reservations"));
  var other=Guid.NewGuid();await f.Sql($"INSERT INTO companies(id,name) VALUES('{other}','Other')");
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Sql($"INSERT INTO ticket_assignees(company_id,ticket_id,user_id) VALUES('{other}','{id}','{IdentityFixture.User}')"));
 });
}
