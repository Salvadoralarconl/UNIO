using Clary.Api;
using Npgsql;
using Xunit;
public class ConversationPersistenceTests {
    [Fact] public async Task FreshStoreRestoresSavedContextAndSidebarAfterRestart() {
        var connection=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")??throw new InvalidOperationException("Set CLARY_TEST_CONNECTION.");
        var schema="chat_test_"+Guid.NewGuid().ToString("N");
        await using var admin=NpgsqlDataSource.Create(connection);
        await using(var create=admin.CreateCommand($"CREATE SCHEMA {schema}"))await create.ExecuteNonQueryAsync();
        try {
            await using var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"}.ConnectionString);
            var sql=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql"));
            await using(var init=db.CreateCommand(sql))await init.ExecuteNonQueryAsync();
            await FixtureCompany.MigrateAsync(db);
            var history=new ConversationRepository(db);
            var firstStore=new ConversationStore((company,q,c,ct)=>Task.FromResult(new Answer("insufficient",[],[],[],"Test",null)),"Test",history);
            var first=await firstStore.ReplyAsync(FixtureCompany.Context,new("My printer has paper but Windows is not printing"),default);
            var second=await firstStore.ReplyAsync(FixtureCompany.Context,new("T88V",first.ConversationId),default);
            ConversationContext? context=null;
            var restarted=new ConversationStore((company,q,c,ct)=>{context=c;return Task.FromResult(new Answer("insufficient",[],[],[],"Test",null));},"Test",new ConversationRepository(db));
            var continued=await restarted.ReplyAsync(FixtureCompany.Context,new("USB",first.ConversationId),default);
            Assert.Equal(first.ConversationId,continued.ConversationId);Assert.Contains("T88V",context!.ScopeQuestion);Assert.Contains("Windows",context.ScopeQuestion);
            Assert.Equal(3,(await history.LoadAsync(FixtureCompany.Context,first.ConversationId!.Value,default))!.Count);
            Assert.Single(await history.ListAsync(FixtureCompany.Context,default));
            var chatId=first.ConversationId!.Value;
            await history.ActionAsync(FixtureCompany.Context,chatId,new("rename","Printer support"),default);await history.ActionAsync(FixtureCompany.Context,chatId,new("pin"),default);
            var named=Assert.Single(await history.ListAsync(FixtureCompany.Context,default));Assert.Equal("Printer support",named.Title);Assert.True(named.Pinned);
            var project=await history.CreateProjectAsync(FixtureCompany.Context,"Helpdesk",default);await history.ActionAsync(FixtureCompany.Context,chatId,new("move",ProjectId:project.Id),default);Assert.Equal(project.Id,Assert.Single(await history.ListAsync(FixtureCompany.Context,default)).ProjectId);
            var folder=await history.CreatePinnedFolderAsync(FixtureCompany.Context,"Printers",default);
            await history.ActionAsync(FixtureCompany.Context,chatId,new("folder",FolderId:folder.Id),default);
            Assert.Equal(folder.Id,Assert.Single(await new ConversationRepository(db).ListAsync(FixtureCompany.Context,default)).PinnedFolderId);
            await history.PinnedFolderActionAsync(FixtureCompany.Context,folder.Id,new("rename","Hardware"),default);
            Assert.Equal("Hardware",Assert.Single(await history.PinnedFoldersAsync(FixtureCompany.Context,default)).Name);
            await Assert.ThrowsAsync<Clary.Api.Identity.CompanyResourceMissing>(()=>history.ActionAsync(FixtureCompany.Context,chatId,new("folder",FolderId:Guid.NewGuid()),default));
            await history.PinnedFolderActionAsync(FixtureCompany.Context,folder.Id,new("delete"),default);
            Assert.Empty(await history.PinnedFoldersAsync(FixtureCompany.Context,default));
            Assert.Null(Assert.Single(await history.ListAsync(FixtureCompany.Context,default)).PinnedFolderId);
            Assert.True(Assert.Single(await history.ListAsync(FixtureCompany.Context,default)).Pinned);
            var token=await history.ShareAsync(FixtureCompany.Context,chatId,default);Assert.NotNull(await history.SharedAsync(FixtureCompany.Context,token,default));
            await history.ActionAsync(FixtureCompany.Context,chatId,new("archive"),default);var archived=Assert.Single(await history.ListAsync(FixtureCompany.Context,default));Assert.True(archived.Archived);Assert.False(archived.Pinned);
            await history.ActionAsync(FixtureCompany.Context,chatId,new("unarchive"),default);Assert.False(Assert.Single(await history.ListAsync(FixtureCompany.Context,default)).Archived);
            await history.ActionAsync(FixtureCompany.Context,chatId,new("delete"),default);Assert.Empty(await history.ListAsync(FixtureCompany.Context,default));Assert.Null(await history.LoadAsync(FixtureCompany.Context,chatId,default));Assert.NotNull(await history.SharedAsync(FixtureCompany.Context,token,default));

        }finally{await using var drop=admin.CreateCommand($"DROP SCHEMA {schema} CASCADE");await drop.ExecuteNonQueryAsync();}
    }
}
