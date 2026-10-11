using Clary.Api.Identity;
using Clary.Api;
using Npgsql;
using Xunit;
public class LibraryTests {
 [Fact] public async Task FoldersAndPlacementsPersistWithoutChangingApprovalOrContent(){
  var connection=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("Set CLARY_TEST_CONNECTION.");
  var schema="library_test_"+Guid.NewGuid().ToString("N");
  await using var admin=NpgsqlDataSource.Create(connection);
  await using(var cmd=admin.CreateCommand($"CREATE SCHEMA {schema}"))await cmd.ExecuteNonQueryAsync();
  try{
   await using var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"}.ConnectionString);
   await using(var cmd=db.CreateCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql"))))await cmd.ExecuteNonQueryAsync();await FixtureCompany.MigrateAsync(db);
   var library=new LibraryRepository(db);var root=await library.CreateAsync(FixtureCompany.Context,"Printers",null,default);var child=await library.CreateAsync(FixtureCompany.Context,"Epson",root.Id,default);
   var id=Guid.NewGuid();await using(var cmd=db.CreateCommand("INSERT INTO documents(id,title,version,approval,indexing,filename,content_hash,canonical_text) VALUES(@id,'Manual','1','draft','pending','manual.txt','hash','Original text')")){cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync();}
   await library.MoveDocumentAsync(FixtureCompany.Context,id,child.Id,default);
   var restored=await new LibraryRepository(db).ListAsync(FixtureCompany.Context,default);Assert.Equal(2,restored.Folders.Count);Assert.Equal(child.Id,Assert.Single(restored.Placements).FolderId);
   await using(var cmd=db.CreateCommand("SELECT approval||':'||canonical_text FROM documents WHERE id=@id")){cmd.Parameters.AddWithValue("id",id);Assert.Equal("draft:Original text",await cmd.ExecuteScalarAsync());}
   await library.MoveDocumentAsync(FixtureCompany.Context,id,null,default);Assert.Empty((await library.ListAsync(FixtureCompany.Context,default)).Placements);
   await Assert.ThrowsAsync<ArgumentException>(()=>library.CreateAsync(FixtureCompany.Context," ",null,default));
   await Assert.ThrowsAsync<CompanyResourceMissing>(()=>library.CreateAsync(FixtureCompany.Context,"Bad parent",Guid.NewGuid(),default));
   await Assert.ThrowsAsync<CompanyResourceMissing>(()=>library.MoveDocumentAsync(FixtureCompany.Context,id,Guid.NewGuid(),default));
   var operations=new LibraryOperations(db);
   var modelPage=await operations.NewPageAsync(FixtureCompany.Context,new("Multi-model Epson guide","Supported models: Epson TM-T88V and Epson TM-T20III. Check the USB connection.",null),default);
   await using(var modelQuery=db.CreateCommand("SELECT supported_models FROM documents WHERE id=@id")){modelQuery.Parameters.AddWithValue("id",modelPage.Id);var models=(string[])(await modelQuery.ExecuteScalarAsync())!;Assert.Contains("TM-T88V",models);Assert.Contains("TM-T20III",models);}
   await using(var clear=db.CreateCommand("UPDATE documents SET supported_models='{}' WHERE id=@id")){clear.Parameters.AddWithValue("id",modelPage.Id);await clear.ExecuteNonQueryAsync();}
   await EpsonModels.RepairMissingAsync(FixtureCompany.Context,db);
   await using(var repaired=db.CreateCommand("SELECT supported_models FROM documents WHERE id=@id AND approval='draft' AND indexing='pending'")){repaired.Parameters.AddWithValue("id",modelPage.Id);Assert.Contains("TM-T88V",(string[])(await repaired.ExecuteScalarAsync())!);}
   await operations.FileAsync(FixtureCompany.Context,modelPage.Id,new("delete",null,null),default);

   var page=await operations.NewPageAsync(FixtureCompany.Context,new("USB guide","# Detection\nCheck Windows.",child.Id),default);
   await operations.ApplyAsync(FixtureCompany.Context,root.Id,new("rename",null,"Receipt printers"),default);
   await Assert.ThrowsAsync<ArgumentException>(()=>operations.ApplyAsync(FixtureCompany.Context,root.Id,new("move",child.Id,null),default));
   var copied=await operations.ApplyAsync(FixtureCompany.Context,root.Id,new("copy",null,null),default);
   var afterCopy=await library.ListAsync(FixtureCompany.Context,default);Assert.Equal(4,afterCopy.Folders.Count);
   var copiedPage=Assert.Single(afterCopy.Placements.Where(p=>p.DocumentId!=page.Id));
   await using(var cmd=db.CreateCommand("SELECT approval||':'||indexing FROM documents WHERE id=@id")){cmd.Parameters.AddWithValue("id",copiedPage.DocumentId);Assert.Equal("draft:pending",await cmd.ExecuteScalarAsync());}
   var deleted=await operations.ApplyAsync(FixtureCompany.Context,root.Id,new("delete",null,null),default);
   Assert.Equal(1,deleted.PreservedDocuments);Assert.Equal(2,(await library.ListAsync(FixtureCompany.Context,default)).Folders.Count);
   Assert.DoesNotContain((await library.ListAsync(FixtureCompany.Context,default)).Placements,p=>p.DocumentId==page.Id);
   await using(var cmd=db.CreateCommand("SELECT canonical_text FROM documents WHERE id=@id")){cmd.Parameters.AddWithValue("id",page.Id);Assert.Equal("# Detection\nCheck Windows.",await cmd.ExecuteScalarAsync());}
   await operations.FileAsync(FixtureCompany.Context,page.Id,new("rename",null,"Updated guide"),default);
   await operations.FileAsync(FixtureCompany.Context,page.Id,new("move",copied.FolderId,null),default);
   await operations.FileAsync(FixtureCompany.Context,page.Id,new("copy",null,null),default);
   await using(var cmd=db.CreateCommand("SELECT count(*) FROM documents WHERE title='Updated guide (copy)' AND approval='draft' AND indexing='pending'"))Assert.Equal(1L,await cmd.ExecuteScalarAsync());
   await operations.FileAsync(FixtureCompany.Context,page.Id,new("delete",null,null),default);
   await using(var cmd=db.CreateCommand("SELECT count(*) FROM documents WHERE id=@id")){cmd.Parameters.AddWithValue("id",page.Id);Assert.Equal(0L,await cmd.ExecuteScalarAsync());}
   var rich="""[{"type":"h2","align":"center","children":[{"type":"text","text":"Saved heading"}]}]""";
   var editable=await operations.NewPageAsync(FixtureCompany.Context,new("Rich guide","ignored",copied.FolderId,rich),default);
   var revision=await operations.EditAsync(FixtureCompany.Context,editable.Id,new("Revised guide","ignored",null,rich),default);
   Assert.Equal(revision.Id,await operations.ResolveAsync(FixtureCompany.Context,editable.Id,default));Assert.NotEqual(editable.Id,revision.Id);Assert.Equal("draft",revision.Approval);
   Assert.Equal(rich,await operations.ContentAsync(FixtureCompany.Context,revision.Id,default));
   Assert.Contains((await library.ListAsync(FixtureCompany.Context,default)).Placements,p=>p.DocumentId==revision.Id&&p.FolderId==copied.FolderId);
   await using(var cmd=db.CreateCommand("SELECT approval||':'||archived::text||':'||canonical_text FROM documents WHERE id=@id")){cmd.Parameters.AddWithValue("id",editable.Id);Assert.Equal("revoked:true:## Saved heading",await cmd.ExecuteScalarAsync());}
   await Assert.ThrowsAsync<CompanyResourceMissing>(()=>operations.EditAsync(FixtureCompany.Context,editable.Id,new("Duplicate","ignored",null,rich),default));
  }finally{await using var cmd=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");await cmd.ExecuteNonQueryAsync();}
 }
}



