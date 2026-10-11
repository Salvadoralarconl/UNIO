using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clary.Api;
using Clary.Api.Identity;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
public class AssetConfigurationTests {
 static CompanyContext C=>InventoryAuditTests.Context;
 static Task Run(Func<IdentityFixture,Task> action)=>IdentityFixture.Run(action,b=>b.Services.AddSingleton<AssetConfigurationRepository>());
 [Fact] public async Task TypeCategoriesAreScopedValidatedAndPreserved()=>await Run(async f=>{
  var repo=new AssetConfigurationRepository(f.Database);
  var first=await repo.SaveTypeAsync(C,null,new("Portable equipment","Laptop",Categories:["Laptop","Desktop"]),default);
  var other=await repo.SaveTypeAsync(C,null,new("Display equipment","Monitor",Categories:["27-inch display"]),default);
  var config=await repo.GetAsync(C,default);
  Assert.Equal(new[]{"Laptop","Desktop"},config.Types.Single(t=>t.Id==first.Id).Categories);
  Assert.Equal(new[]{"27-inch display"},config.Types.Single(t=>t.Id==other.Id).Categories);
  var updated=await repo.SaveTypeAsync(C,first.Id,new("Portable equipment","Laptop",Revision:first.Revision),default);
  Assert.Equal(first.Categories,updated.Categories);
  await Assert.ThrowsAsync<ArgumentException>(()=>repo.SaveTypeAsync(C,first.Id,new("Portable equipment","Laptop",Revision:updated.Revision,Categories:["Laptop"," laptop "]),default));
 });
 [Fact] public async Task ConfigurationSeedsDefaultsAndPreservesLegacyData()=>await Run(async f=>{
  await f.Login();var response=await f.Client.GetAsync("/api/inventory/configuration");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  var config=await response.Content.ReadFromJsonAsync<AssetConfigurationDto>();Assert.Equal(new[]{"Computer","Printer","Monitor","Network","Accessory"},config!.Types.Select(t=>t.Code));Assert.Equal(new[]{"Box","Package","Laptop","Monitor","Printer","Server","Router","Wifi","Headphones","Keyboard","Mouse","Smartphone","Tablet","Camera","Video","Mic","Speaker","Cable","HardDrive","Cpu","Wrench","Hammer","Drill","Car","Truck","Bike","Armchair","Sofa","Lightbulb","Briefcase"},config.Icons);
  var inv=new InventoryRepository(f.Database);var asset=await inv.CreateAsync(C,new("KEEP","Existing"),default);var snapshot=await f.Scalar("SELECT to_jsonb(a)::text FROM inventory_assets a");var repo=new AssetConfigurationRepository(f.Database);var type=config.Types[0];var renamed=await repo.SaveTypeAsync(C,type.Id,new("Workstations","Laptop",false,0,type.Revision),default);Assert.Equal(type.Code,renamed.Code);Assert.False(renamed.Active);Assert.Equal(snapshot,await f.Scalar("SELECT to_jsonb(a)::text FROM inventory_assets a"));
  await using var conn=await f.Database.OpenConnectionAsync();await using var tx=await conn.BeginTransactionAsync();var company=Guid.NewGuid();await using(var q=new Npgsql.NpgsqlCommand("INSERT INTO companies(id,name) VALUES(@id,'New')",conn,tx)){q.Parameters.AddWithValue("id",company);await q.ExecuteNonQueryAsync();}await AssetConfigurationRepository.SeedDefaultsAsync(conn,tx,company,default);await AssetConfigurationRepository.SeedDefaultsAsync(conn,tx,company,default);await tx.CommitAsync();Assert.Equal(5L,await f.Scalar($"SELECT count(*) FROM asset_types WHERE company_id='{company}'"));
 });
 [Fact] public async Task ConfigurationRejectsForeignIdsAndNonSettingsActors()=>await Run(async f=>{
  var repo=new AssetConfigurationRepository(f.Database);await Assert.ThrowsAsync<InventoryMissing>(()=>repo.SaveTypeAsync(C,Guid.NewGuid(),new("Foreign","Box",Revision:1),default));await Assert.ThrowsAsync<ArgumentException>(()=>repo.SaveFieldAsync(C,null,new(Guid.NewGuid(),"Foreign","shortText"),default));
  await f.Sql("UPDATE memberships SET role='Editor'");await Assert.ThrowsAsync<CompanyDenied>(()=>repo.SaveTypeAsync(C,null,new("Denied","Box"),default));await f.Login();Assert.Equal(HttpStatusCode.Forbidden,(await f.Send(HttpMethod.Post,"/api/inventory/configuration/asset-types",new{name="Denied",icon="Box"})).StatusCode);
  await f.Sql("UPDATE memberships SET role='Viewer',visibility_scope='AssignedOnly'");Assert.Equal(HttpStatusCode.OK,(await f.Client.GetAsync("/api/inventory/configuration")).StatusCode);
 });
 [Fact] public async Task ConfigurationRevisionsAndAuditsRollback()=>await Run(async f=>{
  var repo=new AssetConfigurationRepository(f.Database);var type=await repo.SaveTypeAsync(C,null,new("Tools","Wrench"),default);Assert.StartsWith("custom-",type.Code);await Assert.ThrowsAsync<InventoryConflict>(()=>repo.SaveTypeAsync(C,type.Id,new("Stale","Box",Revision:9),default));
  var attempts=await Task.WhenAll(Enumerable.Range(0,2).Select(async i=>{try{await repo.SaveTypeAsync(C,type.Id,new("Tools "+i,"Wrench",Revision:1),default);return true;}catch(InventoryConflict){return false;}}));Assert.Single(attempts,x=>x);
  await f.Sql("CREATE FUNCTION reject_configuration_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.resource_type IN ('asset-type','asset-field') THEN RAISE EXCEPTION 'forced'; END IF; RETURN NEW; END $$; CREATE TRIGGER reject_configuration_audit BEFORE INSERT ON inventory_audit FOR EACH ROW EXECUTE FUNCTION reject_configuration_audit();");await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>repo.SaveTypeAsync(C,null,new("Rollback","Box"),default));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM asset_types WHERE name='Rollback'"));
 });
 [Theory] [InlineData("shortText")] [InlineData("longText")] [InlineData("number")] [InlineData("date")] [InlineData("boolean")] [InlineData("choice")]
 public async Task DefinitionsSupportSixKindsAndArchiveWithoutRemovingValues(string kind)=>await Run(async f=>{
  var asset=await new InventoryRepository(f.Database).CreateAsync(C,new("FIELD","Asset"),default);var repo=new AssetConfigurationRepository(f.Database);var field=await repo.SaveFieldAsync(C,null,new(null,"Detail",kind,Required:true,Choices:kind=="choice"?[" One ","Two"]:null),default);await f.Sql($"UPDATE inventory_assets SET custom_fields=jsonb_build_object('{field.Id}','historical') WHERE id='{asset.Id}'");await Assert.ThrowsAsync<InventoryConflict>(()=>repo.SaveFieldAsync(C,field.Id,new(null,"Detail",kind=="date"?"shortText":"date",Revision:1),default));var archived=await repo.SaveFieldAsync(C,field.Id,new(null,"Detail",kind,Active:false,Choices:field.Choices,Revision:1),default);Assert.False(archived.Active);Assert.Equal("historical",await f.Scalar($"SELECT custom_fields->>'{field.Id}' FROM inventory_assets WHERE id='{asset.Id}'"));
 });
 [Fact] public async Task ConfigurationRejectsUnknownPropertiesCsrfAndMetadataBounds()=>await Run(async f=>{
  await f.Login();Assert.Equal(HttpStatusCode.BadRequest,(await f.Client.PostAsJsonAsync("/api/inventory/configuration/asset-types",new{name="Tool",icon="Box"})).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Post,"/api/inventory/configuration/asset-types",new{name="Tool",icon="Box",companyId=Guid.NewGuid()})).StatusCode);
  var repo=new AssetConfigurationRepository(f.Database);await Assert.ThrowsAsync<ArgumentException>(()=>repo.SaveTypeAsync(C,null,new("Bad","<svg>"),default));await Assert.ThrowsAsync<ArgumentException>(()=>repo.SaveTypeAsync(C,null,new(new string('x',101),"Box"),default));await Assert.ThrowsAsync<ArgumentException>(()=>repo.SaveFieldAsync(C,null,new(null,"Choice","choice",Choices:["a"," A "]),default));await Assert.ThrowsAsync<ArgumentException>(()=>repo.SaveFieldAsync(C,null,new(null,"Choice","number",Choices:["a"]),default));
 });
 [Fact] public async Task MigrationBackfillsAssetsAndKitIdsWithoutRewritingRequests(){
  var connection=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")!;var schema="identity_test_"+Guid.NewGuid().ToString("N");await using var admin=Npgsql.NpgsqlDataSource.Create(connection);await using(var q=admin.CreateCommand($"CREATE SCHEMA {schema}"))await q.ExecuteNonQueryAsync();
  var scripts=Path.Combine(Path.GetTempPath(),"clary_asset_migration_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scripts);
  try{await using var db=Npgsql.NpgsqlDataSource.Create(new Npgsql.NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"}.ConnectionString);
   async Task Sql(string sql){await using var q=db.CreateCommand(sql);await q.ExecuteNonQueryAsync();}async Task<object?> Scalar(string sql){await using var q=db.CreateCommand(sql);return await q.ExecuteScalarAsync();}
   await Sql(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql")));var migrations=Path.Combine(AppContext.BaseDirectory,"database","migrations");foreach(var file in Directory.GetFiles(migrations,"*.sql").Where(x=>!Path.GetFileName(x).StartsWith("010-")&&!Path.GetFileName(x).StartsWith("015-")&&!Path.GetFileName(x).StartsWith("016-")&&!Path.GetFileName(x).StartsWith("017-")))File.Copy(file,Path.Combine(scripts,Path.GetFileName(file)));await DatabaseMigrations.ApplyAsync(db,scripts,default);
   await Sql("INSERT INTO inventory_assets(id,company_id,asset_tag,normalized_tag,computer,status,category) VALUES('60000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000001','KEEP','keep','Preserved','Available','Printer'); INSERT INTO equipment_kits(id,company_id,name,lines) VALUES('60000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001','Keep','[{\"category\":\"Printer\",\"customCategory\":\"Original\",\"quantity\":2,\"extra\":\"preserve\"}]');");
   await Sql("INSERT INTO equipment_kits(id,company_id,name,lines) VALUES(gen_random_uuid(),'00000000-0000-0000-0000-000000000001','Empty','[]')");
   var asset=await Scalar("SELECT to_jsonb(a)::text FROM inventory_assets a");var kits=await Scalar("SELECT lines::text FROM equipment_kits WHERE name='Keep'");
   // Build a complete legacy snapshot using only the pre-010 schema.
   await Sql("BEGIN; INSERT INTO employees(id,company_id,display_name) VALUES('60000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000001','Employee'); INSERT INTO equipment_tickets(id,company_id,request_id,title,state) VALUES('60000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000001','60000000-0000-0000-0000-000000000005','Equipment request: Keep','Pending'); INSERT INTO equipment_requests(id,company_id,title,employee_id,kit_id,kit_name,state,ticket_id,lines) SELECT '60000000-0000-0000-0000-000000000005',company_id,'Keep','60000000-0000-0000-0000-000000000003',id,name,'Pending','60000000-0000-0000-0000-000000000004',lines FROM equipment_kits WHERE name='Keep'; COMMIT;");var snapshot=await Scalar("SELECT to_jsonb(r)::text FROM equipment_requests r");var ticket=await Scalar("SELECT to_jsonb(t)::text FROM equipment_tickets t");
   await DatabaseMigrations.ApplyAsync(db,migrations,default);Assert.Equal(asset,await Scalar("SELECT (to_jsonb(a)-'asset_type_id'-'custom_fields')::text FROM inventory_assets a"));Assert.Equal(kits,await Scalar("SELECT jsonb_agg(e-'assetTypeId')::text FROM equipment_kits CROSS JOIN LATERAL jsonb_array_elements(lines) e WHERE name='Keep'"));Assert.Equal(snapshot,await Scalar("SELECT to_jsonb(r)::text FROM equipment_requests r"));Assert.Equal(ticket,await Scalar("SELECT (to_jsonb(t)-'dismissed'-'is_workspace_setup'-'priority')::text FROM equipment_tickets t"));Assert.Equal("[]",await Scalar("SELECT lines::text FROM equipment_kits WHERE name='Empty'"));Assert.Equal(5L,await Scalar("SELECT count(*) FROM asset_types"));Assert.Equal(1L,await Scalar("SELECT count(*) FROM inventory_assets WHERE asset_type_id IS NOT NULL AND custom_fields='{}'"));
  }finally{await using var q=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");await q.ExecuteNonQueryAsync();Directory.Delete(scripts,true);}
 } [Fact] public async Task CompanyCreationSeedsDefaultsAndRollsThemBackWithProvisioning()=>await Run(async f=>{
  var user=await CompanyAccountTests.Owner(f);var repo=new Clary.Api.Accounts.CompanyAccountRepository(f.Database);var owner=new Clary.Api.Accounts.OperatorContext(user);
  Clary.Api.Accounts.CompanyCreateInput Input(string subject)=>new(subject,new(Clary.Api.Accounts.PlanKind.Ten,null,null),new("https://accounts.example",subject,subject,null,CompanyRole.Administrator,true,null));
  var created=await repo.CreateAsync(owner,Input("fresh-admin"),default);Assert.Equal(5L,await f.Scalar($"SELECT count(*) FROM asset_types WHERE company_id='{created.Id}'"));
  await Assert.ThrowsAsync<InvalidOperationException>(()=>repo.CreateAsync(owner,Input("failed-admin"),default,async(conn,tx,account,ct)=>{await using var q=new Npgsql.NpgsqlCommand("SELECT count(*) FROM asset_types t JOIN memberships m ON m.company_id=t.company_id WHERE m.user_id=@id",conn,tx);q.Parameters.AddWithValue("id",account.Id);Assert.Equal(5L,await q.ExecuteScalarAsync(ct));throw new InvalidOperationException("fixture provisioning failure");}));Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM companies WHERE name='failed-admin'"));Assert.Equal(10L,await f.Scalar("SELECT count(*) FROM asset_types"));
 });}




