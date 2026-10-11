using Clary.Api;
using Clary.Api.Identity;
using Clary.Api.Directories;
using Xunit;
public class BrieInventoryTests {
 sealed class Parser(BrieTicketPatch patch):IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct)=>Task.FromResult(patch);public Task<BrieTicketPatch> ParseAsync(BrieFieldInput input,CancellationToken ct)=>Task.FromResult(input.PendingField=="assetTag"&&input.Message is "DEMO-1" or "IP-1"?new BrieTicketPatch(AssetTag:input.Message):patch);}
 sealed class PendingNameParser:IBrieTicketParser {public Task<BrieTicketPatch> ParseAsync(string text,CancellationToken ct)=>Task.FromResult(text=="Actual laptop"?new BrieTicketPatch(Name:"Actual laptop"):new BrieTicketPatch(AssetTag:"HEY-1"));}
 static BrieMessage M(string text,Guid? task=null,string? token=null)=>new(text,Guid.NewGuid(),TaskId:task,ConfirmationToken:token);
 static BrieTickets Service(KnowledgeIsolationFixture f,BrieTicketPatch patch)=>new(f.Db,new(f.Db),new Parser(patch));
 [Fact] public async Task AssetCreationAsksMissingFieldsAndDoesNotWriteDocumentation()=>await KnowledgeIsolationFixture.Run(async f=>{
  var svc=Service(f,new(Name:"Demo laptop",AssetTag:"Demo laptop"));var first=await svc.ReplyAsync(f.A,M("Create asset Demo laptop"),default);Assert.Contains("tag",first.Message);
  var second=await svc.ReplyAsync(f.A,M("DEMO-1",first.TaskId),default);Assert.NotEmpty(second.Choices!);
  var third=await svc.ReplyAsync(f.A,M(second.Choices![0].Id.ToString(),first.TaskId),default);Assert.Contains("status",third.Message);
  var request=M("Available",first.TaskId);var result=await svc.ReplyAsync(f.A,request,default);Assert.Equal("completed",result.Status);Assert.Equal("asset",result.RecordKind);
  var asset=Assert.Single(await new InventoryRepository(f.Db).ListAsync(f.A,default));Assert.Equal("DEMO-1",asset.AssetTag);Assert.Equal("Demo laptop",asset.Computer);
  await using(var q=f.Db.CreateCommand("SELECT count(*) FROM inventory_documentation"))Assert.Equal(0L,await q.ExecuteScalarAsync());
  Assert.Equal(result,await svc.ReplyAsync(f.A,request,default));Assert.Single(await new InventoryRepository(f.Db).ListAsync(f.A,default));
  await Assert.ThrowsAsync<CompanyResourceMissing>(()=>svc.GetAsync(f.B,first.TaskId,default));
 });
 [Fact] public async Task EmployeeIsDirectoryOnlyAndRequiresSettingsPermission()=>await KnowledgeIsolationFixture.Run(async f=>{
  var svc=Service(f,new(Name:"Demo Employee"));long users;await using(var q=f.Db.CreateCommand("SELECT count(*) FROM users"))users=(long)(await q.ExecuteScalarAsync())!;
  var result=await svc.ReplyAsync(f.A,M("Create employee Demo Employee"),default);Assert.Equal("employee",result.RecordKind);Assert.Equal("completed",result.Status);
  await using(var q=f.Db.CreateCommand("SELECT count(*) FROM users"))Assert.Equal(users,await q.ExecuteScalarAsync());
  var denied=f.A with{UserId=Guid.NewGuid(),Role=CompanyRole.Editor};await FixtureCompany.AddActorAsync(f.Db,denied);
  await Assert.ThrowsAsync<CompanyDenied>(()=>svc.ReplyAsync(denied,M("Create employee Demo Employee"),default));
 });
 [Fact] public async Task ReassignmentNeedsBoundConfirmationAndRejectsForeignEmployee()=>await KnowledgeIsolationFixture.Run(async f=>{
  var dirs=new DirectoryRepository(f.Db);var jane=await dirs.SaveEmployeeAsync(f.A,null,new("Jane"),default);var sam=await dirs.SaveEmployeeAsync(f.A,null,new("Sam"),default);var foreign=await dirs.SaveEmployeeAsync(f.B,null,new("Foreign"),default);
  var repo=new InventoryRepository(f.Db);var asset=await repo.CreateAsync(f.A,new("AS-1","Laptop",Status:"Assigned",EmployeeId:jane.Id),default);
  var svc=Service(f,new(Target:"AS-1",Employee:foreign.Id.ToString()));var first=await svc.ReplyAsync(f.A,M("Assign asset AS-1 to "+foreign.Id),default);Assert.Equal("clarify",first.Status);
  var preview=await svc.ReplyAsync(f.A,M(sam.Id.ToString(),first.TaskId),default);Assert.Equal("confirmation",preview.Status);Assert.Equal(jane.Id,(await repo.GetAsync(f.A,asset.Id,default))!.EmployeeId);
  var wrong=await svc.ReplyAsync(f.A,M("Confirm",first.TaskId,"wrong"),default);Assert.Equal("confirmation",wrong.Status);
  var request=M("Confirm",first.TaskId,wrong.ConfirmationToken);var result=await svc.ReplyAsync(f.A,request,default);Assert.Equal("completed",result.Status);Assert.Equal(sam.Id,(await repo.GetAsync(f.A,asset.Id,default))!.EmployeeId);Assert.Equal(result,await svc.ReplyAsync(f.A,request,default));
 });
 [Fact] public async Task UnsupportedModulesNeverStartActionsAndInventedParserFieldsAreDropped()=>await KnowledgeIsolationFixture.Run(async f=>{
  Assert.False(BrieTickets.StartsTask("Create documentation about a ticket"));Assert.False(BrieTickets.StartsTask("Create account for Jane"));
  var svc=Service(f,new(Name:"Invented Employee"));var result=await svc.ReplyAsync(f.A,M("Create employee"),default);Assert.Equal("clarify",result.Status);
  await using var q=f.Db.CreateCommand("SELECT count(*) FROM employees");Assert.Equal(0L,await q.ExecuteScalarAsync());
 });
 [Theory] [InlineData(false)] [InlineData(true)] public async Task PendingReassignmentCannotBypassConfirmationAfterAssetChanges(bool retarget)=>await KnowledgeIsolationFixture.Run(async f=>{
  var dirs=new DirectoryRepository(f.Db);var jane=await dirs.SaveEmployeeAsync(f.A,null,new("Jane"),default);var sam=await dirs.SaveEmployeeAsync(f.A,null,new("Sam"),default);
  var repo=new InventoryRepository(f.Db);var original=await repo.CreateAsync(f.A,new("BOUND-1","Original",Status:"Assigned",EmployeeId:jane.Id),default);
  var svc=Service(f,new(Target:"Original",Employee:"Sam"));var preview=await svc.ReplyAsync(f.A,M("Assign asset Original to Sam"),default);Assert.Equal("confirmation",preview.Status);
  await using(var q=f.Db.CreateCommand("UPDATE inventory_assets SET employee_id=NULL,status='Available',revision=revision+1,computer=@tag WHERE id=@id")){q.Parameters.AddWithValue("tag",retarget?"Renamed":"Original");q.Parameters.AddWithValue("id",original.Id);await q.ExecuteNonQueryAsync();}
  var target=retarget?await repo.CreateAsync(f.A,new("BOUND-2","Original",Status:"Available"),default):(await repo.GetAsync(f.A,original.Id,default))!;
  var result=await svc.ReplyAsync(f.A,M(retarget?"Confirm":"No",preview.TaskId,retarget?preview.ConfirmationToken:null),default);
  Assert.Equal("confirmation",result.Status);Assert.Null((await repo.GetAsync(f.A,target.Id,default))!.EmployeeId);Assert.Null((await repo.GetAsync(f.A,original.Id,default))!.EmployeeId);
 }); [Fact] public async Task InvalidOptionalIpCanBeCorrectedWithoutLosingOtherFields()=>await KnowledgeIsolationFixture.Run(async f=>{
  var svc=Service(f,new(Name:"Laptop",IpAddress:"not-an-ip"));var first=await svc.ReplyAsync(f.A,M("Create asset Laptop IP not-an-ip"),default);Assert.Contains("IP",first.Message);
  var tag=await svc.ReplyAsync(f.A,M("192.0.2.15",first.TaskId),default);Assert.Contains("tag",tag.Message);
  var type=await svc.ReplyAsync(f.A,M("IP-1",first.TaskId),default);var status=await svc.ReplyAsync(f.A,M(type.Choices![0].Id.ToString(),first.TaskId),default);var done=await svc.ReplyAsync(f.A,M("Available",first.TaskId),default);
  Assert.Equal("completed",done.Status);var asset=Assert.Single(await new InventoryRepository(f.Db).ListAsync(f.A,default));Assert.Equal("192.0.2.15",asset.IpAddress);Assert.Equal("Laptop",asset.Computer);
 });
 [Fact] public async Task GreetingDoesNotFillPendingAssetName()=>await KnowledgeIsolationFixture.Run(async f=>{
  var svc=new BrieTickets(f.Db,new(f.Db),new PendingNameParser());var first=await svc.ReplyAsync(f.A,M("Create asset HEY-1"),default);Assert.Contains("name",first.Message);
  var greeting=await svc.ReplyAsync(f.A,M("HEY!",first.TaskId),default);Assert.Equal("clarify",greeting.Status);Assert.Contains("How can I help",greeting.Message);
  var next=await svc.ReplyAsync(f.A,M("Actual laptop",first.TaskId),default);Assert.NotEmpty(next.Choices!);
  await using var q=f.Db.CreateCommand("SELECT state->>'name' FROM brie_ticket_tasks WHERE id=@id");q.Parameters.AddWithValue("id",first.TaskId);Assert.Equal("Actual laptop",await q.ExecuteScalarAsync());
 });}



