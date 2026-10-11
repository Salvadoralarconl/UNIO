using Npgsql;
using Clary.Api.Accounts;
using System.Security.AccessControl;
using System.Security.Principal;
namespace Clary.Api.LocalIdentity;
public sealed class LocalOperatorBootstrap(NpgsqlDataSource database,string frontendOrigin="http://localhost:5174")
{
 public async Task<Guid> InitializeAsync(string outputFile,CancellationToken ct=default)
 {
  var path=PrivatePath(outputFile);
  if(!Uri.TryCreate(frontendOrigin,UriKind.Absolute,out var origin)||origin.Host!="localhost"||origin.Port!=5174||origin.Scheme is not ("http" or "https")||origin.UserInfo.Length!=0||origin.AbsolutePath!="/"||origin.Query.Length!=0||origin.Fragment.Length!=0)throw new ArgumentException("Configure a localhost frontend origin on port 5174.");
  await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
  await using(var gate=new NpgsqlCommand("LOCK TABLE operator_owner IN SHARE ROW EXCLUSIVE MODE",c,tx))await gate.ExecuteNonQueryAsync(ct);
  await using(var existing=new NpgsqlCommand("SELECT 1 FROM operator_owner",c,tx))if(await existing.ExecuteScalarAsync(ct) is not null)throw new InvalidOperationException("Operator ownership is already bound. Explicit offline recovery is required.");
  var repo=new LocalCredentialRepository();var id=await repo.CreateIdentityAsync(c,tx,null,null,null,ct);await repo.CreateUnsetAsync(c,tx,id,ct);
  await using(var owner=new NpgsqlCommand("INSERT INTO operator_owner(user_id,issuer,subject) SELECT id,issuer,identity_subject FROM users WHERE id=@user",c,tx))
  {owner.Parameters.AddWithValue("user",id);await owner.ExecuteNonQueryAsync(ct);}
  var link=await new LocalCredentialTokens().IssueAsync(c,tx,id,CredentialTokenPurpose.Setup,ct);
  await AccountAuditRepository.AppendAsync(c,tx,id,null,"LocalOperatorProvisioned",id,null,new{UserId=id},ct);
  await AccountAuditRepository.AppendAsync(c,tx,id,null,"CredentialSetupIssued",id,null,new{ExpiresAt=link.ExpiresAt},ct);
  var created=false;
  try
  {
   using(var stream=CreatePrivateFile(path))
   {
    created=true;
    await using var writer=new StreamWriter(stream);
    await writer.WriteAsync((origin.GetLeftPart(UriPartial.Authority)+link.Link+Environment.NewLine).AsMemory(),ct);await writer.FlushAsync(ct);
   }
   await tx.CommitAsync(ct);return id;
  }
  catch{if(created)File.Delete(path);throw;}
 }
 public async Task<Guid> RecoverAsync(string outputFile,CancellationToken ct=default)
 {
  var path=PrivatePath(outputFile);
  if(!Uri.TryCreate(frontendOrigin,UriKind.Absolute,out var origin)||origin.Host!="localhost"||origin.Port!=5174||origin.Scheme is not ("http" or "https")||origin.UserInfo.Length!=0||origin.AbsolutePath!="/"||origin.Query.Length!=0||origin.Fragment.Length!=0)throw new ArgumentException("Configure a localhost frontend origin on port 5174.");
  await using var c=await database.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(ct);
  await using(var gate=new NpgsqlCommand("LOCK TABLE operator_owner IN SHARE ROW EXCLUSIVE MODE",c,tx))await gate.ExecuteNonQueryAsync(ct);
  Guid id;bool configured;
  await using(var q=new NpgsqlCommand("SELECT u.id,l.password_hash IS NOT NULL FROM operator_owner o JOIN users u ON u.id=o.user_id AND u.issuer=o.issuer AND u.identity_subject=o.subject JOIN local_credentials l ON l.user_id=u.id WHERE u.active AND u.issuer=@issuer AND NOT EXISTS(SELECT 1 FROM memberships m WHERE m.user_id=u.id) FOR UPDATE OF u,l",c,tx))
  {
   q.Parameters.AddWithValue("issuer",LocalCredentialRepository.Issuer);await using var r=await q.ExecuteReaderAsync(ct);
   if(!await r.ReadAsync(ct))throw new InvalidOperationException("Recovery requires the same active companyless local Operator.");
   id=r.GetGuid(0);configured=r.GetBoolean(1);
  }
  await CompanyAccountRepository.RevokeUserAsync(c,tx,id,ct);
  var link=await new LocalCredentialTokens().IssueAsync(c,tx,id,configured?CredentialTokenPurpose.Reset:CredentialTokenPurpose.Setup,ct);
  await AccountAuditRepository.AppendAsync(c,tx,id,null,"LocalOperatorRecoveryIssued",id,null,new{Purpose=configured?"Reset":"Setup",ExpiresAt=link.ExpiresAt},ct);
  var created=false;
  try{
   using(var stream=CreatePrivateFile(path)){created=true;await using var writer=new StreamWriter(stream);await writer.WriteAsync((origin.GetLeftPart(UriPartial.Authority)+link.Link+Environment.NewLine).AsMemory(),ct);await writer.FlushAsync(ct);}
   await tx.CommitAsync(ct);return id;
  }catch{if(created)File.Delete(path);throw;}
 }
 private static string PrivatePath(string outputFile)
 {
  if(string.IsNullOrWhiteSpace(outputFile)||!Path.IsPathFullyQualified(outputFile))throw new ArgumentException("Use an absolute path beneath the current user's local Brie directory.");
  var root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Brie"));
  var path=Path.GetFullPath(outputFile);
  if(!path.StartsWith(root+Path.DirectorySeparatorChar,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal)||path.IndexOf(':',OperatingSystem.IsWindows()?2:0)>=0||File.Exists(path)||Directory.Exists(path))throw new ArgumentException("Choose a new file beneath the current user's local Brie directory.");
  for(var part=new DirectoryInfo(Path.GetDirectoryName(path)!);part is not null;part=part.Parent)
   if(part.Exists&&(part.Attributes&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Linked output paths are prohibited.");
  return path;
 }
 private static FileStream CreatePrivateFile(string path)
 {
  var directory=Path.GetDirectoryName(path)!;
  if(OperatingSystem.IsWindows())
  {
   var user=WindowsIdentity.GetCurrent().User??throw new InvalidOperationException("Current user identity required.");
   var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);security.SetOwner(user);security.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
   var info=new DirectoryInfo(directory);info.Create(security);info.SetAccessControl(security);
   var fileSecurity=new FileSecurity();fileSecurity.SetAccessRuleProtection(true,false);fileSecurity.SetOwner(user);fileSecurity.AddAccessRule(new FileSystemAccessRule(user,FileSystemRights.FullControl,AccessControlType.Allow));
   return new FileInfo(path).Create(FileMode.CreateNew,FileSystemRights.Write,FileShare.None,4096,FileOptions.None,fileSecurity);
  }
  Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
  File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
  return new FileStream(path,new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None,UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite});
 }
}
