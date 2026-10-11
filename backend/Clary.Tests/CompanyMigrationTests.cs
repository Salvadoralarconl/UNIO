using Clary.Api;
using Npgsql;
using Xunit;

public class CompanyMigrationTests
{
    [Fact] public async Task DanglingReplacementIsReportedBeforeAnyMigrationWrites() => await WithDatabase(async db=>
    {
        await Sql(db,"INSERT INTO documents(id,title,version,approval,indexing,filename,content_hash,canonical_text,replaced_by) VALUES(gen_random_uuid(),'Preserved','1','draft','pending','wiki/page.md','hash','Preserved prose','10000000-0000-0000-0000-000000000001')");
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>Apply(db));
        Assert.Contains("1 dangling document replacement",ex.Message);
        Assert.Equal(true,await Scalar(db,"SELECT to_regclass(current_schema()||'.schema_migrations') IS NULL"));
        Assert.Equal("Preserved prose",await Scalar(db,"SELECT canonical_text FROM documents"));
        Assert.Equal(Guid.Parse("10000000-0000-0000-0000-000000000001"),await Scalar(db,"SELECT replaced_by FROM documents"));
    });
    [Theory]
    [InlineData("exact", true)]
    [InlineData("zero", false)]
    [InlineData("multiple", false)]
    [InlineData("changed", false)]
    [InlineData("restored-target-other-dangling", false)]
    public async Task RepairProposalRequiresTheExactSoleDanglingPair(string scenario, bool permitted) => await WithDatabase(async db =>
    {
        const string reviewed = "0b1d819b-8ba0-4de2-8f2f-6a82217e6326";
        const string target = "11e84017-1a5b-4b14-a807-78aa19ff2d1c";
        async Task Document(string id, string? replacement) => await Sql(db,
            $"INSERT INTO documents(id,title,version,approval,indexing,filename,content_hash,canonical_text,replaced_by) VALUES('{id}','Preserved','1','draft','pending','wiki/page.md','hash','Preserved prose',{(replacement is null ? "NULL" : "'" + replacement + "'")})");
        await Document(reviewed, scenario == "zero" ? null : scenario == "changed" ? "10000000-0000-0000-0000-000000000009" : target);
        if (scenario is "multiple" or "restored-target-other-dangling")
            await Document("10000000-0000-0000-0000-000000000002", "10000000-0000-0000-0000-000000000003");
        if (scenario == "restored-target-other-dangling") await Document(target, null);
        var before = await Scalar(db, "SELECT jsonb_agg(to_jsonb(d) ORDER BY id)::text FROM documents d");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "scripts", "repair-proposals"))) root = root.Parent;
        Assert.NotNull(root);
        var proposal = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "scripts", "repair-proposals", "001-archive-dangling-document-replacement.sql"));
        if (permitted)
        {
            await Sql(db, proposal);
            Assert.Equal(DBNull.Value, await Scalar(db, $"SELECT replaced_by FROM documents WHERE id='{reviewed}'"));
            Assert.Equal(1L, await Scalar(db, "SELECT count(*) FROM legacy_document_replacement_archive"));
            Assert.Equal(Guid.Parse(target), await Scalar(db, "SELECT original_replaced_by FROM legacy_document_replacement_archive"));
            Assert.Equal("Preserved prose", await Scalar(db, "SELECT canonical_text FROM documents"));
            await Assert.ThrowsAsync<PostgresException>(() => Sql(db, proposal));
            Assert.Equal(1L, await Scalar(db, "SELECT count(*) FROM legacy_document_replacement_archive"));
        }
        else
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() => Sql(db, proposal));
            Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
            Assert.Equal(before, await Scalar(db, "SELECT jsonb_agg(to_jsonb(d) ORDER BY id)::text FROM documents d"));
            Assert.Equal(true, await Scalar(db, "SELECT to_regclass(current_schema()||'.legacy_document_replacement_archive') IS NULL"));
        }
    });

    private static string Migrations => Path.Combine(AppContext.BaseDirectory,"database","migrations");
    private static async Task Apply(NpgsqlDataSource db, string? directory = null)
    {
        await DatabaseMigrations.ApplyAsync(db,directory ?? Migrations,CancellationToken.None);
    }
    private static async Task WithDatabase(Func<NpgsqlDataSource,Task> run)
    {
        var connection=Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION") ?? throw new InvalidOperationException("CLARY_TEST_CONNECTION is required.");
        var schema="migration_test_"+Guid.NewGuid().ToString("N");
        await using var admin=NpgsqlDataSource.Create(connection);
        await using(var q=admin.CreateCommand($"CREATE SCHEMA {schema}"))await q.ExecuteNonQueryAsync();
        try
        {
            await using var db=NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection){SearchPath=schema+",public"}.ConnectionString);
            await Sql(db,await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"database","001-initial.sql")));
            await run(db);
        }
        finally {await Sql(admin,$"DROP SCHEMA {schema} CASCADE");}
    }
    private static async Task Sql(NpgsqlDataSource db,string sql){await using var q=db.CreateCommand(sql);await q.ExecuteNonQueryAsync();}
    private static async Task<object?> Scalar(NpgsqlDataSource db,string sql){await using var q=db.CreateCommand(sql);return await q.ExecuteScalarAsync();}

    [Fact] public async Task LegacyDataKeepsIdsAndRelations() => await WithDatabase(async db=>
    {
        await Sql(db,"""
            INSERT INTO documents(id,title,version,approval,indexing,filename,content_hash,canonical_text,rich_content)
            VALUES ('10000000-0000-0000-0000-000000000001','Original title','1','draft','pending','uploads/original.pdf','hash','Original prose','<img src="/api/library/images/20000000-0000-0000-0000-000000000001">');
            INSERT INTO passages VALUES('10000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001','Original passage','page 1',0,'[1,2,3]');
            INSERT INTO library_folders VALUES('10000000-0000-0000-0000-000000000003','Original folder',NULL);
            INSERT INTO document_folders VALUES('10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000003');
            INSERT INTO chat_projects VALUES('10000000-0000-0000-0000-000000000004','Original project');
            INSERT INTO pinned_chat_folders VALUES('10000000-0000-0000-0000-000000000005','Original pin');
            INSERT INTO conversations(id,title,turns,project_id,pinned_folder_id) VALUES('10000000-0000-0000-0000-000000000006','Original chat','[{"text":"Original turn"}]','10000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000005');
            INSERT INTO conversation_shares(id,title,turns) VALUES('10000000-0000-0000-0000-000000000007','Original share','[]');
            INSERT INTO inventory_assets(id,asset_tag,normalized_tag,computer,department,status) VALUES('10000000-0000-0000-0000-000000000008','PC-1','pc-1','Original computer','IT','Assigned');
            INSERT INTO inventory_activity(id,asset_id,computer,kind) VALUES('10000000-0000-0000-0000-000000000009','10000000-0000-0000-0000-000000000008','Original computer','created');
            INSERT INTO inventory_departments VALUES('it','IT','#3b82f6');
            INSERT INTO inventory_documentation VALUES('Computer','10000000-0000-0000-0000-000000000001');
            """);
        var tables=new[]{"documents","passages","library_folders","document_folders","chat_projects","pinned_chat_folders","conversations","conversation_shares","inventory_assets","inventory_activity","inventory_departments","inventory_documentation"};
        var snapshots=new Dictionary<string,string>();
        foreach(var table in tables)snapshots[table]=(string)(await Scalar(db,$"SELECT jsonb_agg(to_jsonb(t))::text FROM {table} t"))!;
        await Apply(db);
        foreach(var table in tables)
        {
            var added=table=="inventory_assets"?"-'employee_id'-'department_id'-'location_id'":table=="inventory_departments"?"-'id'-'active'-'revision'":"";
            Assert.Equal(snapshots[table],await Scalar(db,$"SELECT jsonb_agg(to_jsonb(t)-'company_id'{added})::text FROM {table} t"));
            Assert.Equal(0L,await Scalar(db,$"SELECT count(*) FROM {table} WHERE company_id IS NULL"));
        }
        Assert.Equal(0L,await Scalar(db,"SELECT count(*) FROM inventory_assets WHERE employee_id IS NOT NULL"));
        Assert.Equal(2L,await Scalar(db,"SELECT count(*) FROM owned_files"));
        Assert.Equal("uploads/images/20000000-0000-0000-0000-000000000001.bin",await Scalar(db,"SELECT storage_path FROM owned_files WHERE kind='wiki_image'"));
    });

    [Fact] public async Task RepeatedStartupIsIdempotent() => await WithDatabase(async db=>
    {
        await Task.WhenAll(Apply(db),Apply(db));
        await Apply(db);
        Assert.Equal(1L,await Scalar(db,"SELECT count(*) FROM companies"));
        Assert.Equal(1L,await Scalar(db,"SELECT count(*) FROM schema_migrations WHERE version=3 AND name='003-asset-employee-reference.sql'"));
        Assert.Equal("YES",await Scalar(db,"SELECT is_nullable FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='inventory_assets' AND column_name='employee_id' AND udt_name='uuid'"));
        Assert.Equal(1L,await Scalar(db,"SELECT CASE WHEN pg_get_indexdef(to_regclass(current_schema()||'.inventory_assets_company_employee')) LIKE '%(company_id, employee_id)%' THEN 1::bigint ELSE 0::bigint END"));
        Assert.Equal((long)Directory.GetFiles(Migrations,"*.sql").Length,await Scalar(db,"SELECT count(*) FROM schema_migrations"));
    });

    [Fact] public async Task FailedMigrationRollsBack() => await WithDatabase(async db=>
    {
        var directory=Path.Combine(Path.GetTempPath(),"clary_migrations_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory,"002-company-foundation.sql"),await File.ReadAllTextAsync(Path.Combine(Migrations,"002-company-foundation.sql"))+"\nSELECT 1/0;");
            await Assert.ThrowsAsync<PostgresException>(()=>Apply(db,directory));
            Assert.Equal(true,await Scalar(db,"SELECT to_regclass(current_schema()||'.companies') IS NULL"));
            Assert.Equal(0L,await Scalar(db,"SELECT count(*) FROM schema_migrations"));
            await Apply(db);
        }
        finally {Directory.Delete(directory,true);}
    });

    [Fact] public async Task ChangedAppliedMigrationIsRejected() => await WithDatabase(async db=>
    {
        await Apply(db);
        var directory=Path.Combine(Path.GetTempPath(),"clary_migrations_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory,"002-company-foundation.sql"),await File.ReadAllTextAsync(Path.Combine(Migrations,"002-company-foundation.sql"))+"\n-- tampered");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>Apply(db,directory));
            Assert.Equal((long)Directory.GetFiles(Migrations,"*.sql").Length,await Scalar(db,"SELECT count(*) FROM schema_migrations"));
        }
        finally {Directory.Delete(directory,true);}
    });

    [Fact] public async Task SharedImageRemainsLinkedToEachLegacyDocument() => await WithDatabase(async db=>
    {
        await Sql(db,"""
            INSERT INTO documents(id,title,version,approval,indexing,filename,content_hash,canonical_text,rich_content)
            SELECT gen_random_uuid(),'Copied page','1','draft','pending','wiki/page.md','hash','Original prose','<img src="/api/library/images/20000000-0000-0000-0000-000000000001">' FROM generate_series(1,2);
            """);
        await Apply(db);
        Assert.Equal(1L,await Scalar(db,"SELECT count(*) FROM owned_files WHERE kind='wiki_image'"));
        Assert.Equal(2L,await Scalar(db,"SELECT count(*) FROM document_files"));
    });

    [Fact] public async Task CompanyUniquenessAndChildReferencesAreScoped() => await WithDatabase(async db=>
    {
        await Apply(db);
        await Sql(db,"""
            INSERT INTO companies(id,name) VALUES('30000000-0000-0000-0000-000000000001','Other');
            INSERT INTO inventory_assets(id,company_id,asset_tag,normalized_tag,computer,status)
            SELECT gen_random_uuid(),id,'PC-1','pc-1','Computer','Available' FROM companies;
            INSERT INTO inventory_departments(company_id,normalized_name,name,color) SELECT id,'it','IT','#3b82f6' FROM companies;
            INSERT INTO library_folders(id,company_id,name) VALUES('30000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000001','Other folder');
            """);
        Assert.Equal(2L,await Scalar(db,"SELECT count(*) FROM inventory_assets"));
        Assert.Equal(2L,await Scalar(db,"SELECT count(*) FROM inventory_departments"));
        var duplicate=await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,"INSERT INTO inventory_assets(id,company_id,asset_tag,normalized_tag,computer,status) SELECT gen_random_uuid(),id,'PC-1','pc-1','Duplicate','Available' FROM companies LIMIT 1"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation,duplicate.SqlState);
        var ex=await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,"INSERT INTO library_folders(id,company_id,name,parent_id) SELECT gen_random_uuid(),id,'Bad child','30000000-0000-0000-0000-000000000002' FROM companies WHERE id<>'30000000-0000-0000-0000-000000000001'"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,ex.SqlState);
        ex=await Assert.ThrowsAsync<PostgresException>(()=>Sql(db,"INSERT INTO inventory_activity(id,company_id,asset_id,computer,kind) SELECT gen_random_uuid(),c.id,a.id,'Bad','created' FROM companies c JOIN inventory_assets a ON a.company_id<>c.id LIMIT 1"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,ex.SqlState);
    });

    [Fact] public async Task AmbiguousFileIdsAbortWithoutGuessingOwnership() => await WithDatabase(async db=>
    {
        await Sql(db,"""
            INSERT INTO documents(id,title,version,approval,indexing,filename,content_hash,canonical_text,rich_content)
            VALUES('20000000-0000-0000-0000-000000000001','Collision','1','draft','pending','uploads/file.pdf','hash','Original','<img src="/api/library/images/20000000-0000-0000-0000-000000000001">');
            """);
        var ex=await Assert.ThrowsAsync<PostgresException>(()=>Apply(db));
        Assert.Equal(PostgresErrorCodes.UniqueViolation,ex.SqlState);
        Assert.Equal(0L,await Scalar(db,"SELECT count(*) FROM schema_migrations"));
        Assert.Equal(true,await Scalar(db,"SELECT to_regclass(current_schema()||'.companies') IS NULL"));
    });
}



