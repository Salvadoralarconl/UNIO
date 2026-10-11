using Npgsql;
namespace Clary.Api.Accounts;

/// <summary>Offline provisioning only: callers must verify the provider issuer and immutable subject.</summary>
public sealed class OperatorBootstrap(NpgsqlDataSource database)
{
 internal static bool ValidBinding(string? issuer,string? subject)=>
  issuer is {Length: >0 and <=2000} && issuer==issuer.Trim() && !issuer.Any(char.IsWhiteSpace)
  && Uri.TryCreate(issuer,UriKind.Absolute,out var uri) && uri.Scheme=="https" && !string.IsNullOrEmpty(uri.Host)
  && uri.UserInfo.Length==0 && uri.Query.Length==0 && uri.Fragment.Length==0
  && subject is {Length: >0 and <=500} && subject==subject.Trim() && !subject.Any(char.IsControl);
 public async Task<OperatorContext> InitializeAsync(string issuer,string subject,CancellationToken ct=default)
 {
  if(!ValidBinding(issuer,subject))throw new ArgumentException("A verified HTTPS issuer and non-empty immutable subject are required.");
  await using var connection=await database.OpenConnectionAsync(ct);
  await using var tx=await connection.BeginTransactionAsync(ct);
  // Lock the singleton table even when empty; SELECT FOR UPDATE alone cannot serialize first provisioning.
  await using(var gate=new NpgsqlCommand("LOCK TABLE operator_owner IN SHARE ROW EXCLUSIVE MODE",connection,tx))await gate.ExecuteNonQueryAsync(ct);
  await using(var existing=new NpgsqlCommand("SELECT o.user_id,o.issuer,o.subject,u.active FROM operator_owner o JOIN users u ON u.id=o.user_id",connection,tx))
  {
   await using var r=await existing.ExecuteReaderAsync(ct);
   if(await r.ReadAsync(ct))
   {
    if(r.GetString(1)!=issuer||r.GetString(2)!=subject||!r.GetBoolean(3))throw new InvalidOperationException("Operator ownership is already bound or inactive.");
    return new(r.GetGuid(0));
   }
  }
  await using(var insert=new NpgsqlCommand("INSERT INTO users(id,issuer,identity_subject,display_name) VALUES(@id,@issuer,@subject,@subject) ON CONFLICT(issuer,identity_subject) DO NOTHING",connection,tx))
  {insert.Parameters.AddWithValue("id",Guid.NewGuid());insert.Parameters.AddWithValue("issuer",issuer);insert.Parameters.AddWithValue("subject",subject);await insert.ExecuteNonQueryAsync(ct);}
  Guid userId;
  await using(var find=new NpgsqlCommand("SELECT id,active FROM users WHERE issuer=@issuer AND identity_subject=@subject FOR UPDATE",connection,tx))
  {
   find.Parameters.AddWithValue("issuer",issuer);find.Parameters.AddWithValue("subject",subject);await using var r=await find.ExecuteReaderAsync(ct);
   if(!await r.ReadAsync(ct)||!r.GetBoolean(1))throw new InvalidOperationException("Operator identity must be active.");userId=r.GetGuid(0);
  }
  await using(var owner=new NpgsqlCommand("INSERT INTO operator_owner(user_id,issuer,subject) VALUES(@user,@issuer,@subject)",connection,tx))
  {owner.Parameters.AddWithValue("user",userId);owner.Parameters.AddWithValue("issuer",issuer);owner.Parameters.AddWithValue("subject",subject);await owner.ExecuteNonQueryAsync(ct);}
  await tx.CommitAsync(ct);return new(userId);
 }
}
