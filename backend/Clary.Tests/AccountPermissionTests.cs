using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

public class AccountPermissionTests
{
    [Fact] public void UnknownCapabilityFailsClosed() => Assert.False(AccountPermissions.Allows(new(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null),(CompanyCapability)999));
    [Theory]
    [InlineData("Viewer","/api/questions", "ask")]
    [InlineData("Viewer","/api/conversations/00000000-0000-0000-0000-000000000099/action", "rename")]
    [InlineData("Viewer","/api/pinned-folders", "create")]
    [InlineData("Editor","/api/conversations/00000000-0000-0000-0000-000000000099/action", "delete")]
    [InlineData("Editor","/api/pinned-folders/00000000-0000-0000-0000-000000000099/action", "delete")]
    [InlineData("Editor","/api/directory/departments", "create")]
    public async Task SharedMutationHandlersDenyInsufficientCapabilities(string role,string path,string operation) => await IdentityFixture.Run(async f=>
    {
        await f.Sql($"UPDATE memberships SET role='{role}'");await f.Login();
        var result=await f.Send(HttpMethod.Post,path,new {operation,name="Denied",question="Denied",role="Administrator",companyId=Guid.NewGuid()});
        Assert.Equal(HttpStatusCode.Forbidden,result.StatusCode);
    });
    [Fact] public async Task SessionExposesExplicitCapabilities() => await IdentityFixture.Run(async f=>
    {
        await f.Sql("UPDATE memberships SET role='Viewer'");await f.Login();
        var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");
        Assert.False(session.GetProperty("isOperator").GetBoolean());
        var capabilities=session.GetProperty("capabilities");Assert.True(capabilities.GetProperty("read").GetBoolean());
        foreach(var key in new[]{"write","delete","approve","index","repair","settings","accountAdmin"})Assert.False(capabilities.GetProperty(key).GetBoolean());
    });
    [Theory] [InlineData("Administrator","bootstrap")] [InlineData("Viewer","bootstrap")] [InlineData("Administrator","other")]
    public async Task LegacyMembershipApiCannotAssignAdministratorOrEditSelf(string role,string subject) => await IdentityFixture.Run(async f=>
    {
        await f.Login();var result=await f.Send(HttpMethod.Post,"/api/company/memberships",new {subject,issuer="clary-development",role,active=true});
        Assert.Equal(HttpStatusCode.Forbidden,result.StatusCode);
    });
    [Theory] [InlineData("UPDATE memberships SET active=false")] [InlineData("UPDATE memberships SET role='Viewer'")] [InlineData("UPDATE companies SET active=false")]
    public async Task StaleContextCannotCommitMutation(string sql) => await IdentityFixture.Run(async f=>
    {
        var stale=new Clary.Api.Identity.CompanyContext(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null);
        await f.Sql(sql);
        await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>new Clary.Api.LibraryRepository(f.Database).CreateAsync(stale,"Denied",null,default));
        Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM library_folders"));
    });
    [Theory] [InlineData("Administrator",true,true,true)] [InlineData("Editor",true,false,false)] [InlineData("Viewer",false,false,false)]
    public async Task SessionCapabilityMatrix(string role,bool write,bool delete,bool settings) => await IdentityFixture.Run(async f=>
    {
        await f.Sql($"UPDATE memberships SET role='{role}'");await f.Login();
        var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");var c=session.GetProperty("capabilities");
        Assert.True(c.GetProperty("read").GetBoolean());Assert.Equal(write,c.GetProperty("write").GetBoolean());Assert.Equal(delete,c.GetProperty("delete").GetBoolean());
        foreach(var key in new[]{"approve","index","repair"})Assert.Equal(write,c.GetProperty(key).GetBoolean());
        foreach(var key in new[]{"settings","accountAdmin"})Assert.Equal(settings,c.GetProperty(key).GetBoolean());
    });
    [Theory] [InlineData("Viewer")] [InlineData("Editor")]
    public async Task ProtectedContentMutationsUseProductionMappings(string role) => await IdentityFixture.Run(async f=>
    {
        await f.Sql($"UPDATE memberships SET role='{role}'");await f.Login();
        var paths=role=="Viewer"?new[]{"/api/library/pages","/api/library/folders","/api/documents/upload","/api/documents/00000000-0000-0000-0000-000000000099/approve","/api/samples/index","/api/inventory/documentation/link"}:new[]{"/api/library/files/00000000-0000-0000-0000-000000000099/action","/api/library/folders/00000000-0000-0000-0000-000000000099/action"};
        foreach(var path in paths)Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,path,new {operation="delete",name="Denied",title="Denied",role="Administrator"})).StatusCode);
    });
    [Fact] public async Task ViewerCannotRepairPersistedContent() => await IdentityFixture.Run(async f=>
    {
        await f.Sql("UPDATE memberships SET role='Viewer'; INSERT INTO documents(company_id,id,title,version,approval,indexing,filename,content_hash,canonical_text) VALUES('00000000-0000-0000-0000-000000000001',gen_random_uuid(),'Repair','1','draft','pending','wiki/test.md','test','Epson TM-T88V')");
        await Assert.ThrowsAsync<Clary.Api.Identity.CompanyDenied>(()=>Clary.Api.EpsonModels.RepairMissingAsync(new(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null),f.Database));
        Assert.Equal(0,await f.Scalar("SELECT cardinality(supported_models) FROM documents"));
    });
    [Fact] public async Task OperatorAuthorityIsIndependentAndNeverImpersonatesCompany() => await IdentityFixture.Run(async f=>
    {
        await new OperatorBootstrap(f.Database).InitializeAsync("https://operator.example","trusted-owner");
        f.SignInAs("https://operator.example","trusted-owner",IdentityFixture.Company);
        var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");Assert.True(session.GetProperty("isOperator").GetBoolean());Assert.Equal(JsonValueKind.Null,session.GetProperty("activeCompany").ValueKind);
        foreach(var capability in session.GetProperty("capabilities").EnumerateObject())Assert.False(capability.Value.GetBoolean());
        Assert.Equal(HttpStatusCode.OK,(await f.Client.GetAsync("/api/operator/probe")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/session/company",new {companyId=IdentityFixture.Company})).StatusCode);
        await f.Login();Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/operator/probe")).StatusCode);
    });
    [Fact] public async Task ClientRoleClaimsCannotGrantOperatorOrCompanyAuthority() => await IdentityFixture.Run(async f=>
    {
        await f.Sql("UPDATE memberships SET role='Viewer'");
        f.SignInAs("clary-development","bootstrap",IdentityFixture.Company,new System.Security.Claims.Claim("role","Administrator"),new System.Security.Claims.Claim("isOperator","true"));
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync("/api/operator/probe")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/pinned-folders",new {name="Denied"})).StatusCode);
        var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");Assert.False(session.GetProperty("isOperator").GetBoolean());Assert.Equal("Viewer",session.GetProperty("role").GetString());
    });
    [Fact] public async Task AssignedOnlyViewerKeepsInventoryScopeAcrossAllContent() => await IdentityFixture.Run(async f=>
    {
        await f.Sql("UPDATE memberships SET role='Viewer',visibility_scope='AssignedOnly'");await f.Login();
        foreach(var path in new[]{"/api/documents","/api/library","/api/conversations","/api/chat-projects","/api/pinned-folders","/api/files/00000000-0000-0000-0000-000000000099","/api/directory/employees"})Assert.Equal(HttpStatusCode.Forbidden,(await f.Client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await f.Client.GetAsync("/api/inventory/assets")).StatusCode);
        var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");Assert.False(session.GetProperty("capabilities").GetProperty("read").GetBoolean());Assert.True(session.GetProperty("capabilities").GetProperty("ownInventory").GetBoolean());
    });
    [Fact] public async Task EditorCannotCreateDirectoryEntryThroughAssetBody() => await IdentityFixture.Run(async f=>
    {
        await f.Sql("UPDATE memberships SET role='Editor'");await f.Login();
        Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/inventory/assets",new {assetTag="DENIED",computer="Denied",newAssigneeName="Hidden directory write"})).StatusCode);
        Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM employees"));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_assets"));
    });
    [Fact] public async Task CompanyAdministratorCannotModifyForeignMembership() => await IdentityFixture.Run(async f=>
    {
        var foreign=new Clary.Api.Identity.CompanyContext(Guid.NewGuid(),Guid.NewGuid(),CompanyRole.Viewer,null,null);await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign.CompanyId}','Foreign')");await FixtureCompany.AddActorAsync(f.Database,foreign);await f.Login();
        Assert.Equal(HttpStatusCode.NotFound,(await f.Send(HttpMethod.Put,"/api/company/memberships",new {subject=foreign.UserId.ToString(),issuer="fixture-actor",role="Editor",active=true,revision=1})).StatusCode);
        Assert.Equal("Viewer",await f.Scalar($"SELECT role FROM memberships WHERE user_id='{foreign.UserId}'"));
    });
    [Theory] [InlineData("UPDATE memberships SET active=false")] [InlineData("UPDATE users SET active=false")] [InlineData("UPDATE companies SET active=false")]
    public async Task RevocationDuringQuestionGenerationPreventsCommit(string sql)
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await IdentityFixture.Run(async f=>
        {
            await f.Login();var pending=f.Send(HttpMethod.Post,"/api/questions",new {question="How do I configure VPN?"});
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));await f.Sql(sql);release.SetResult();
            Assert.Equal(HttpStatusCode.Forbidden,(await pending).StatusCode);Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM conversations"));
        },builder=>Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(builder.Services,sp=>new Clary.Api.ConversationStore(async(company,q,c,ct)=>{entered.SetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(10));return new Clary.Api.Answer("insufficient",[],[],[],"Test",null);},"Test",Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Clary.Api.ConversationRepository>(sp))));
    }
    [Fact] public async Task SuspendedCompanySessionReturnsRecoveryWithoutCapabilities() => await IdentityFixture.Run(async f=>
    {
        await f.Login();await f.Sql("UPDATE companies SET active=false");var session=await f.Client.GetFromJsonAsync<JsonElement>("/api/session");
        Assert.Equal(JsonValueKind.Null,session.GetProperty("activeCompany").ValueKind);Assert.Empty(session.GetProperty("memberships").EnumerateArray());
        foreach(var capability in session.GetProperty("capabilities").EnumerateObject())Assert.False(capability.Value.GetBoolean());
    });
    [Theory] [InlineData("UPDATE users SET active=false",401)] [InlineData("UPDATE companies SET active=false",403)]
    public async Task ExistingCookieRechecksAccountAndCompany(string sql,int status) => await IdentityFixture.Run(async f=>
    {await f.Login();await f.Sql(sql);Assert.Equal(status,(int)(await f.Client.GetAsync("/api/company")).StatusCode);});
}

