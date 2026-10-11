using Clary.Api.Identity;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace Clary.Api;
public sealed class UploadService(NpgsqlDataSource database,SampleLoader indexer,IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim indexingGate=new(1,1);
    private string Folder=>Path.Combine(environment.ContentRootPath,"uploads");
    public async Task<DocumentInfo> UploadAsync(CompanyContext context,IFormFile file,string? title,string? version,CancellationToken ct) {
        if(file.Length is <=0 or >DocumentExtractor.MaxBytes)throw new ArgumentException("Choose a nonempty file no larger than 15 MB.");
        title=string.IsNullOrWhiteSpace(title)?Path.GetFileNameWithoutExtension(file.FileName):title.Trim();
        version=string.IsNullOrWhiteSpace(version)?"1.0":version.Trim();
        if(title.Length is <1 or >200||version.Length>40)throw new ArgumentException("Use a title up to 200 characters and a version up to 40 characters.");
        using var buffer=new MemoryStream();await file.CopyToAsync(buffer,ct);
        var bytes=buffer.ToArray();var text=DocumentExtractor.Extract(file.FileName,bytes);
        var models=EpsonModels.Extract(text);
        var id=Guid.NewGuid();var filename=id+Path.GetExtension(file.FileName).ToLowerInvariant();
        Directory.CreateDirectory(Folder);var path=Path.Combine(Folder,filename);await File.WriteAllBytesAsync(path,bytes,ct);
        try {
            await using var connection=await database.OpenConnectionAsync(ct);await using var tx=await connection.BeginTransactionAsync(ct);await AccountPermissions.RevalidateAsync(connection,tx,context,CompanyCapability.Write,ct);
            await using var cmd=CompanyCommand.Create(context,"""
                INSERT INTO documents(company_id,id,title,version,approval,indexing,filename,content_hash,canonical_text,supported_models)
                VALUES(@company,@id,@title,@version,'draft','pending',@file,@hash,@text,@models)
                """,connection,tx);
            cmd.Parameters.AddWithValue("id",id);cmd.Parameters.AddWithValue("title",title);cmd.Parameters.AddWithValue("version",version);cmd.Parameters.AddWithValue("file","uploads/"+filename);cmd.Parameters.AddWithValue("hash",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));cmd.Parameters.AddWithValue("text",text);cmd.Parameters.AddWithValue("models",models);
            await cmd.ExecuteNonQueryAsync(ct);await CompanyFiles.RecordAsync(context,connection,tx,id,"uploads/"+filename,"document",id,ct);await tx.CommitAsync(ct);return new(id,title,version,"draft","pending",null,true);
        }catch {File.Delete(path);throw;}
    }
    public async Task ApproveAndIndexAsync(CompanyContext context,Guid id,CancellationToken ct) {
        if(!await indexingGate.WaitAsync(0,ct))throw new ArgumentException("Another upload is indexing. Wait for it to finish and retry.");
        try {
            
            await using var cmd=CompanyCommand.Create(context,"UPDATE documents SET approval='approved' WHERE company_id=@company AND id=@id AND NOT archived AND (filename LIKE 'uploads/%' OR filename LIKE 'wiki/%') RETURNING canonical_text",database);cmd.Parameters.AddWithValue("id",id);
            var text=await AccountPermissions.ScalarAsync(database,cmd,context,CompanyCapability.Approve,ct) as string??throw new CompanyResourceMissing();
            await indexer.IndexAsync(context,id,text,ct);
        } finally{indexingGate.Release();}
    }
    public async Task<string?> TextAsync(CompanyContext context,Guid id,CancellationToken ct) {
        
            await using var cmd=CompanyCommand.Create(context,"SELECT canonical_text FROM documents WHERE company_id=@company AND id=@id",database);cmd.Parameters.AddWithValue("id",id);return await cmd.ExecuteScalarAsync(ct) as string;
    }
}

