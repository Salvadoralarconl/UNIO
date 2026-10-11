using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clary.Api.Identity;
using Npgsql;
using NpgsqlTypes;
namespace Clary.Api.Directories;

public sealed class DirectoryRepository(NpgsqlDataSource database)
{
    record Cursor(Guid Company,string Kind,string Query,string Name,Guid Id);
    static string Name(string? value){var s=value?.Trim();if(string.IsNullOrEmpty(s)||s.Length>100)throw new ArgumentException("Name must be 1 to 100 characters.");return s;}
    static void Revision(Guid? id,int? revision){if(id.HasValue&&revision is null or <1)throw new ArgumentException("Expected revision is required.");}
    static void Param(NpgsqlCommand q,string name,object? value,NpgsqlDbType type)=>q.Parameters.AddWithValue(name,type,value??DBNull.Value);
    public Task<DirectoryPage<EmployeeDto>> ListEmployeesAsync(CompanyContext c,string? query,string? cursor,int limit,CancellationToken ct)=>List(c,"employees","display_name","id,display_name,email,department_id,active,revision",query,cursor,limit,r=>new EmployeeDto(r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.IsDBNull(3)?null:r.GetGuid(3),r.GetBoolean(4),r.GetInt32(5)),x=>x.DisplayName,x=>x.Id,ct);
    public Task<DirectoryPage<DepartmentDto>> ListDepartmentsAsync(CompanyContext c,string? query,string? cursor,int limit,CancellationToken ct)=>List(c,"inventory_departments","name","id,name,color,active,revision",query,cursor,limit,r=>new DepartmentDto(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetBoolean(3),r.GetInt32(4)),x=>x.Name,x=>x.Id,ct);
    public Task<DirectoryPage<LocationDto>> ListLocationsAsync(CompanyContext c,string? query,string? cursor,int limit,CancellationToken ct)=>List(c,"locations","name","id,name,parent_id,active,revision",query,cursor,limit,r=>new LocationDto(r.GetGuid(0),r.GetString(1),r.IsDBNull(2)?null:r.GetGuid(2),r.GetBoolean(3),r.GetInt32(4)),x=>x.Name,x=>x.Id,ct);
    async Task<DirectoryPage<T>> List<T>(CompanyContext c,string table,string name,string columns,string? query,string? cursor,int limit,Func<NpgsqlDataReader,T> read,Func<T,string> getName,Func<T,Guid> getId,CancellationToken ct)
    {
        if(limit is <1 or >100)throw new ArgumentException("Limit must be 1 to 100.");query=query?.Trim()??"";Cursor? after=null;
        if(cursor is not null){try{after=JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(cursor));}catch(Exception e)when(e is FormatException or JsonException){throw new ArgumentException("Invalid cursor.");}if(after is null||after.Company!=c.CompanyId||after.Kind!=table||after.Query!=query||after.Name is null)throw new ArgumentException("Invalid cursor.");}
        await using var q=database.CreateCommand($"SELECT {columns} FROM {table} WHERE company_id=@company AND strpos(lower({name}),lower(@query))>0"+(after is null?"":$" AND ({name} COLLATE \"C\",id)>(@name COLLATE \"C\",@id)")+$" ORDER BY {name} COLLATE \"C\",id LIMIT @limit");
        q.Parameters.AddWithValue("company",c.CompanyId);q.Parameters.AddWithValue("query",query);q.Parameters.AddWithValue("limit",limit+1);
        if(after is not null){q.Parameters.AddWithValue("name",after.Name);q.Parameters.AddWithValue("id",after.Id);}
        await using var r=await q.ExecuteReaderAsync(ct);var items=new List<T>();while(await r.ReadAsync(ct))items.Add(read(r));var more=items.Count>limit;if(more)items.RemoveAt(limit);
        return new(items,more?Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(c.CompanyId,table,query,getName(items[^1]),getId(items[^1])))):null);
    }
    async Task<NpgsqlTransaction> Begin(NpgsqlConnection connection,CompanyContext c,CancellationToken ct)
    {
        var tx=await connection.BeginTransactionAsync(ct);
        try{await AccountPermissions.RevalidateAsync(connection,tx,c,CompanyCapability.Settings,ct);}catch{await tx.DisposeAsync();throw;}
        // Serialize hierarchy and foreign-reference validation with all directory writes in this company.
        await using var q=new NpgsqlCommand("SELECT id FROM companies WHERE id=@company FOR UPDATE",connection,tx);q.Parameters.AddWithValue("company",c.CompanyId);
        if(await q.ExecuteScalarAsync(ct) is null){await tx.DisposeAsync();throw new CompanyResourceMissing();}return tx;
    }
    static async Task Exists(NpgsqlConnection connection,NpgsqlTransaction tx,CompanyContext c,string table,Guid? id,bool active,CancellationToken ct)
    {
        if(id is null)return;await using var q=new NpgsqlCommand($"SELECT 1 FROM {table} WHERE company_id=@company AND id=@id"+(active?" AND active":""),connection,tx);q.Parameters.AddWithValue("company",c.CompanyId);q.Parameters.AddWithValue("id",id.Value);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyResourceMissing();
    }
    static async Task CheckCurrent(NpgsqlConnection connection,NpgsqlTransaction tx,CompanyContext c,string table,Guid? id,int? revision,CancellationToken ct)
    {
        if(id is null)return;await using var q=new NpgsqlCommand($"SELECT revision FROM {table} WHERE company_id=@company AND id=@id",connection,tx);q.Parameters.AddWithValue("company",c.CompanyId);q.Parameters.AddWithValue("id",id.Value);var found=await q.ExecuteScalarAsync(ct);if(found is null)throw new CompanyResourceMissing();if((int)found!=revision)throw new CompanyConflict();
    }
    public async Task<DepartmentDto> SaveDepartmentAsync(CompanyContext c,Guid? id,DepartmentInput input,CancellationToken ct)
    {
        var name=Name(input.Name);Revision(id,input.Revision);if(input.Color is null||!Regex.IsMatch(input.Color,"^#[0-9a-fA-F]{6}\\z"))throw new ArgumentException("Color must be a six-digit hex value.");var color=input.Color.ToLowerInvariant();
        await using var connection=await database.OpenConnectionAsync(ct);await using var tx=await Begin(connection,c,ct);await CheckCurrent(connection,tx,c,"inventory_departments",id,input.Revision,ct);var before=await AuditRepository.SnapshotAsync(connection,tx,c,"inventory_departments","id",id,ct);var key=id??Guid.NewGuid();
        await using var q=new NpgsqlCommand(id is null?"INSERT INTO inventory_departments(company_id,id,normalized_name,name,color,active) VALUES(@company,@id,lower(@name),@name,@color,@active) RETURNING revision":"UPDATE inventory_departments SET name=@name,normalized_name=lower(@name),color=@color,active=@active,revision=revision+1 WHERE company_id=@company AND id=@id RETURNING revision",connection,tx);
        q.Parameters.AddWithValue("company",c.CompanyId);q.Parameters.AddWithValue("id",key);q.Parameters.AddWithValue("name",name);q.Parameters.AddWithValue("color",color);q.Parameters.AddWithValue("active",input.Active);
        try{var revision=(int)(await q.ExecuteScalarAsync(ct))!;var after=new DepartmentDto(key,name,color,input.Active,revision);await AuditRepository.AppendAsync(connection,tx,c,id is null?"Created":"Updated","department",key,before,after,ct);await tx.CommitAsync(ct);return after;}catch(PostgresException e)when(e.SqlState==PostgresErrorCodes.UniqueViolation){throw new CompanyConflict();}
    }
    public async Task<EmployeeDto> SaveEmployeeAsync(CompanyContext c,Guid? id,EmployeeInput input,CancellationToken ct)
    {
        await using var connection=await database.OpenConnectionAsync(ct);await using var tx=await Begin(connection,c,ct);
        var result=await SaveEmployeeWithinAsync(connection,tx,c,id,input,ct);await tx.CommitAsync(ct);return result;
    }
    internal async Task<EmployeeDto> SaveEmployeeWithinAsync(NpgsqlConnection connection,NpgsqlTransaction tx,CompanyContext c,Guid? id,EmployeeInput input,CancellationToken ct)
    {
        var name=Name(input.DisplayName);Revision(id,input.Revision);var email=string.IsNullOrWhiteSpace(input.Email)?null:input.Email.Trim();if(email?.Length>254)throw new ArgumentException("Email must be at most 254 characters.");
        await AccountPermissions.RevalidateAsync(connection,tx,c,CompanyCapability.Settings,ct);await CheckCurrent(connection,tx,c,"employees",id,input.Revision,ct);var before=await AuditRepository.SnapshotAsync(connection,tx,c,"employees","id",id,ct);await Exists(connection,tx,c,"inventory_departments",input.DepartmentId,false,ct);var key=id??Guid.NewGuid();
        await using var q=new NpgsqlCommand(id is null?"INSERT INTO employees(company_id,id,display_name,email,department_id,active) VALUES(@company,@id,@name,@email,@department,@active) RETURNING revision":"UPDATE employees SET display_name=@name,email=@email,department_id=@department,active=@active,revision=revision+1 WHERE company_id=@company AND id=@id RETURNING revision",connection,tx);
        q.Parameters.AddWithValue("company",c.CompanyId);q.Parameters.AddWithValue("id",key);q.Parameters.AddWithValue("name",name);Param(q,"email",email,NpgsqlDbType.Text);Param(q,"department",input.DepartmentId,NpgsqlDbType.Uuid);q.Parameters.AddWithValue("active",input.Active);var revision=(int)(await q.ExecuteScalarAsync(ct))!;var after=new EmployeeDto(key,name,email,input.DepartmentId,input.Active,revision);await AuditRepository.AppendAsync(connection,tx,c,id is null?"Created":"Updated","employee",key,before,after,ct);return after;
    }
    public async Task<LocationDto> SaveLocationAsync(CompanyContext c,Guid? id,LocationInput input,CancellationToken ct)
    {
        var name=Name(input.Name);Revision(id,input.Revision);await using var connection=await database.OpenConnectionAsync(ct);await using var tx=await Begin(connection,c,ct);await CheckCurrent(connection,tx,c,"locations",id,input.Revision,ct);var before=await AuditRepository.SnapshotAsync(connection,tx,c,"locations","id",id,ct);await Exists(connection,tx,c,"locations",input.ParentId,false,ct);var key=id??Guid.NewGuid();
        if(input.ParentId is Guid parent){await using var cycle=new NpgsqlCommand("WITH RECURSIVE ancestors AS (SELECT id,parent_id FROM locations WHERE company_id=@company AND id=@parent UNION SELECT l.id,l.parent_id FROM locations l JOIN ancestors a ON l.id=a.parent_id WHERE l.company_id=@company) SELECT 1 FROM ancestors WHERE id=@id",connection,tx);cycle.Parameters.AddWithValue("company",c.CompanyId);cycle.Parameters.AddWithValue("parent",parent);cycle.Parameters.AddWithValue("id",key);if(await cycle.ExecuteScalarAsync(ct) is not null)throw new ArgumentException("A location cannot parent itself or a descendant.");}
        await using var q=new NpgsqlCommand(id is null?"INSERT INTO locations(company_id,id,name,parent_id,active) VALUES(@company,@id,@name,@parent,@active) RETURNING revision":"UPDATE locations SET name=@name,parent_id=@parent,active=@active,revision=revision+1 WHERE company_id=@company AND id=@id RETURNING revision",connection,tx);q.Parameters.AddWithValue("company",c.CompanyId);q.Parameters.AddWithValue("id",key);q.Parameters.AddWithValue("name",name);Param(q,"parent",input.ParentId,NpgsqlDbType.Uuid);q.Parameters.AddWithValue("active",input.Active);var revision=(int)(await q.ExecuteScalarAsync(ct))!;var after=new LocationDto(key,name,input.ParentId,input.Active,revision);await AuditRepository.AppendAsync(connection,tx,c,id is null?"Created":"Updated","location",key,before,after,ct);await tx.CommitAsync(ct);return after;
    }
    public async Task<IReadOnlyList<DirectoryMappingReview>> MappingReviewAsync(CompanyContext c,CancellationToken ct)
    {
        await using var q=database.CreateCommand("SELECT asset_id,kind,legacy_text,reason FROM directory_mapping_review WHERE company_id=@company ORDER BY asset_id,kind");q.Parameters.AddWithValue("company",c.CompanyId);await using var r=await q.ExecuteReaderAsync(ct);var result=new List<DirectoryMappingReview>();while(await r.ReadAsync(ct))result.Add(new(r.GetGuid(0),r.GetString(1),r.GetString(2),r.GetString(3)));return result;
    }
}
