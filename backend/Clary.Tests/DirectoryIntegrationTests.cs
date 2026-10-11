using Clary.Api;
using Clary.Api.Directories;
using Clary.Api.Identity;
using Xunit;
using Npgsql;

public class DirectoryIntegrationTests
{
    static CompanyContext Context => new(IdentityFixture.Company,IdentityFixture.User,CompanyRole.Administrator,null,null);
    [Fact] public async Task DuplicateNamesHaveDistinctIds()=>await IdentityFixture.Run(async f=>{
        var r=new DirectoryRepository(f.Database);
        var a=await r.SaveEmployeeAsync(Context,null,new(" Alex ",null,null,true,null),default);
        var b=await r.SaveEmployeeAsync(Context,null,new("Alex",null,null,true,null),default);
        Assert.NotEqual(a.Id,b.Id);Assert.Equal("Alex",a.DisplayName);
    });
    [Fact] public async Task DepartmentRenamePreservesColorAndRelations()=>await IdentityFixture.Run(async f=>{
        var r=new DirectoryRepository(f.Database);var d=await r.SaveDepartmentAsync(Context,null,new("IT","#ABCDEF",true,null),default);
        var e=await r.SaveEmployeeAsync(Context,null,new("Alex",null,d.Id,true,null),default);
        var saved=await r.SaveDepartmentAsync(Context,d.Id,new("Technology",d.Color,true,d.Revision),default);
        Assert.Equal(d.Id,saved.Id);Assert.Equal("#abcdef",saved.Color);
        Assert.Equal(d.Id,Assert.Single((await r.ListEmployeesAsync(Context,null,null,50,default)).Items).DepartmentId);
        await Assert.ThrowsAsync<CompanyConflict>(()=>r.SaveDepartmentAsync(Context,d.Id,new("Stale",d.Color,true,d.Revision),default));
        await Assert.ThrowsAsync<CompanyDenied>(()=>r.SaveDepartmentAsync(Context with{CompanyId=Guid.NewGuid()},d.Id,new("Foreign",d.Color,true,saved.Revision),default));
    });
    [Fact] public async Task LocationCannotParentItselfOrDescendant()=>await IdentityFixture.Run(async f=>{
        var r=new DirectoryRepository(f.Database);var a=await r.SaveLocationAsync(Context,null,new("HQ",null,true,null),default);
        var b=await r.SaveLocationAsync(Context,null,new("Floor",a.Id,true,null),default);
        await Assert.ThrowsAsync<ArgumentException>(()=>r.SaveLocationAsync(Context,a.Id,new("HQ",a.Id,true,a.Revision),default));
        await Assert.ThrowsAsync<ArgumentException>(()=>r.SaveLocationAsync(Context,a.Id,new("HQ",b.Id,true,a.Revision),default));
    });
    [Fact] public async Task DirectoryPaginationIsStable()=>await IdentityFixture.Run(async f=>{
        var r=new DirectoryRepository(f.Database);for(var i=0;i<3;i++)await r.SaveEmployeeAsync(Context,null,new("Same",null,null,true,null),default);
        var a=await r.ListEmployeesAsync(Context,null,null,2,default);var b=await r.ListEmployeesAsync(Context,null,a.NextCursor,2,default);
        Assert.Equal(3,a.Items.Concat(b.Items).Select(x=>x.Id).Distinct().Count());Assert.Null(b.NextCursor);
        await Assert.ThrowsAsync<ArgumentException>(()=>r.ListEmployeesAsync(Context,null,"bad",2,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>r.ListEmployeesAsync(Context,null,null,101,default));
    });
    [Fact] public async Task InactiveEntryRemainsOnOldAssets()=>await IdentityFixture.Run(async f=>{
        var r=new DirectoryRepository(f.Database);var e=await r.SaveEmployeeAsync(Context,null,new("Alex",null,null,true,null),default);
        var asset=await new InventoryRepository(f.Database).CreateAsync(Context,new("KEEP","Original prose",EmployeeId:e.Id),default);
        await f.Sql($"UPDATE inventory_assets SET employee_id='{e.Id}' WHERE id='{asset.Id}'");
        await r.SaveEmployeeAsync(Context,e.Id,new(e.DisplayName,null,null,false,e.Revision),default);
        Assert.Equal(e.Id,await f.Scalar($"SELECT employee_id FROM inventory_assets WHERE id='{asset.Id}'"));
        Assert.Equal("Alex",await f.Scalar($"SELECT assignee FROM inventory_assets WHERE id='{asset.Id}'"));
    });
    [Fact] public async Task LegacyAmbiguousNamesRemainUnresolved()
    {
        var connection=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")!;var schema="directory_test_"+Guid.NewGuid().ToString("N");
        await using var admin=NpgsqlDataSource.Create(connection);
        async Task Sql(NpgsqlDataSource db,string sql){await using var q=db.CreateCommand(sql);await q.ExecuteNonQueryAsync();}
        async Task<object?> Scalar(NpgsqlDataSource db,string sql){await using var q=db.CreateCommand(sql);return await q.ExecuteScalarAsync();}
        await Sql(admin,$"CREATE SCHEMA {schema}");
        try{
            await using var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"}.ConnectionString);
            await Sql(db,await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql")));
            await Sql(db,"""
                INSERT INTO inventory_departments(normalized_name,name,color) VALUES('mis','MIS','#123456'),('it','IT','#654321');
                INSERT INTO inventory_assets(id,asset_tag,normalized_tag,computer,assignee,department,location,status) VALUES
                (gen_random_uuid(),'A','a','Original',' Salvador Alarcon ',' MIS ',' HQ ','Assigned'),
                (gen_random_uuid(),'B','b','Original','salvador alarcon','MIS','HQ','Assigned'),
                (gen_random_uuid(),'C','c','Original','Alex','MIS','HQ','Assigned'),
                (gen_random_uuid(),'D','d','Original','alex','IT','HQ','Assigned'),
                (gen_random_uuid(),'E','e','Original',repeat('x',150),'MIS',repeat('y',150),'Assigned');
                """);
            await DatabaseMigrations.ApplyAsync(db,default);
            Assert.Equal(2L,await Scalar(db,"SELECT count(*) FROM inventory_assets WHERE employee_id IS NOT NULL"));
            Assert.Equal(1L,await Scalar(db,"SELECT count(DISTINCT employee_id) FROM inventory_assets WHERE normalized_tag IN ('a','b')"));
            Assert.Equal(3L,await Scalar(db,"SELECT count(*) FROM directory_mapping_review WHERE kind='employee'"));
            Assert.Equal(1L,await Scalar(db,"SELECT count(*) FROM directory_mapping_review WHERE kind='location'"));
            Assert.Equal(" Salvador Alarcon ",await Scalar(db,"SELECT assignee FROM inventory_assets WHERE normalized_tag='a'"));
            Assert.Equal("#123456",await Scalar(db,"SELECT color FROM inventory_departments WHERE normalized_name='mis'"));
        }finally{await Sql(admin,$"DROP SCHEMA {schema} CASCADE");}
    }
    [Fact] public async Task MembershipUsesCurrentEmployeeDepartmentAndRejectsForeignEmployee()=>await IdentityFixture.Run(async f=>{
        var r=new DirectoryRepository(f.Database);var d=await r.SaveDepartmentAsync(Context,null,new("IT","#123456"),default);
        var e=await r.SaveEmployeeAsync(Context,null,new("Alex",DepartmentId:d.Id),default);
        var companies=new CompanyRepository(f.Database);
        var target=Context with{UserId=Guid.NewGuid(),Role=CompanyRole.Editor};await FixtureCompany.AddActorAsync(f.Database,target);
        var assigned=await companies.SaveAsync(Context,new(target.UserId.ToString(),"fixture-actor","Editor",true,e.Id,1),true,default);
        Assert.Equal(e.Id,assigned.EmployeeId);Assert.Equal(2,assigned.Revision);
        await Assert.ThrowsAsync<CompanyDenied>(()=>companies.SaveAsync(Context,new("bootstrap","clary-development","Administrator",true,Guid.NewGuid(),2),true,default));
        await Assert.ThrowsAsync<CompanyDenied>(()=>companies.SaveAsync(Context,new("bootstrap","clary-development","Viewer",true,null,1),true,default));
        var foreignCompany=Guid.NewGuid();var foreignEmployee=Guid.NewGuid();
        await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreignCompany}','Foreign'); INSERT INTO employees(id,company_id,display_name) VALUES('{foreignEmployee}','{foreignCompany}','Foreign employee')");
        var before=await f.Scalar($"SELECT row_to_json(m)::text FROM memberships m WHERE user_id='{target.UserId}'");var auditBefore=await f.Scalar("SELECT count(*) FROM inventory_audit");
        await f.Login();
        foreach(var invalidEmployee in new[]{foreignEmployee,Guid.NewGuid()})
        {
            var response=await f.Send(System.Net.Http.HttpMethod.Put,"/api/company/memberships",new {subject=target.UserId.ToString(),issuer="fixture-actor",role="Editor",active=true,employeeId=invalidEmployee,revision=assigned.Revision});
            Assert.Equal(System.Net.HttpStatusCode.NotFound,response.StatusCode);
            Assert.Equal(e.Id,await f.Scalar($"SELECT employee_id FROM memberships WHERE user_id='{target.UserId}'"));
            Assert.Equal(assigned.Revision,await f.Scalar($"SELECT revision FROM memberships WHERE user_id='{target.UserId}'"));
            Assert.Equal(before,await f.Scalar($"SELECT row_to_json(m)::text FROM memberships m WHERE user_id='{target.UserId}'"));
            Assert.Equal(auditBefore,await f.Scalar("SELECT count(*) FROM inventory_audit"));
        }
        var principal=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim("iss","fixture-actor"),new System.Security.Claims.Claim("sub",target.UserId.ToString())},"test"));
        Assert.Equal(d.Id,(await new CompanyAccess(f.Database).ResolveAsync(principal,Context.CompanyId,default))!.DepartmentId);
        await r.SaveEmployeeAsync(Context,e.Id,new("Alex",Revision:e.Revision),default);
        Assert.Null((await new CompanyAccess(f.Database).ResolveAsync(principal,Context.CompanyId,default))!.DepartmentId);
    });
    [Fact] public async Task DepartmentColorRejectsTrailingNewlineWithoutMutation()=>await IdentityFixture.Run(async f=>{
        await f.Login();
        var before=await f.Scalar("SELECT count(*) FROM inventory_departments");
        var invalid=await f.Send(HttpMethod.Post,"/api/directory/departments",new DepartmentInput("Invalid color","#123456\n"));
        Assert.Equal(400,(int)invalid.StatusCode);
        Assert.Equal(before,await f.Scalar("SELECT count(*) FROM inventory_departments"));
        var valid=await f.Send(HttpMethod.Post,"/api/directory/departments",new DepartmentInput("Valid color","#ABCDEF"));
        Assert.Equal(200,(int)valid.StatusCode);
        Assert.Equal("#abcdef",await f.Scalar("SELECT color FROM inventory_departments WHERE name='Valid color'"));
    });
    [Fact] public async Task DirectoryHttpContractsAndRoles()=>await IdentityFixture.Run(async f=>{
        Assert.Equal(401,(int)(await f.Client.GetAsync("/api/directory/employees")).StatusCode);await f.Login();
        var created=await f.Send(HttpMethod.Post,"/api/directory/departments",new DepartmentInput(" IT ","#123456"));Assert.Equal(200,(int)created.StatusCode);
        var saved=System.Text.Json.JsonSerializer.Deserialize<DepartmentDto>(await created.Content.ReadAsStringAsync(),new System.Text.Json.JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        Assert.Equal(409,(int)(await f.Send(HttpMethod.Put,"/api/directory/departments/"+saved.Id,new DepartmentInput("IT","#123456",true,999))).StatusCode);
        Assert.Equal(404,(int)(await f.Send(HttpMethod.Put,"/api/directory/departments/"+Guid.NewGuid(),new DepartmentInput("IT","#123456",true,1))).StatusCode);
        Assert.Equal(400,(int)(await f.Client.GetAsync("/api/directory/employees?cursor=invalid")).StatusCode);
        await f.Sql($"UPDATE memberships SET visibility_scope='AssignedOnly',role='Viewer' WHERE user_id='{Context.UserId}'");Assert.Equal(403,(int)(await f.Client.GetAsync("/api/directory/employees")).StatusCode);
    });
}
