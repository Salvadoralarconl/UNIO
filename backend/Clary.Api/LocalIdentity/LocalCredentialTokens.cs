using Npgsql;
using System.Security.Cryptography;
using System.Text;
namespace Clary.Api.LocalIdentity;
public sealed class LocalCredentialTokens
{
 public async Task<CredentialLink> IssueAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid userId,CredentialTokenPurpose purpose,CancellationToken ct)
 {
  if(!Enum.IsDefined(purpose))throw new ArgumentException("Invalid credential token purpose.");
  await using(var gate=new NpgsqlCommand("SELECT u.id FROM users u JOIN local_credentials l ON l.user_id=u.id WHERE u.id=@user AND u.active FOR UPDATE OF u,l",c,tx))
  {gate.Parameters.AddWithValue("user",userId);if(await gate.ExecuteScalarAsync(ct) is null)throw new InvalidOperationException("Active local identity required.");}
  await RevokeAsync(c,tx,userId,ct);
  var raw=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
  await using var q=new NpgsqlCommand("WITH time AS (SELECT clock_timestamp() AS at) INSERT INTO local_credential_tokens(id,user_id,purpose,token_hash,credential_revision,created_at,expires_at) SELECT @id,@user,@purpose,@hash,l.revision,time.at,time.at+interval '30 minutes' FROM local_credentials l CROSS JOIN time WHERE l.user_id=@user RETURNING expires_at",c,tx);
  q.Parameters.AddWithValue("id",Guid.NewGuid());q.Parameters.AddWithValue("user",userId);q.Parameters.AddWithValue("purpose",purpose.ToString());q.Parameters.AddWithValue("hash",SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
  var expiry=(DateTime)(await q.ExecuteScalarAsync(ct))!;
  return new(userId,new DateTimeOffset(expiry),$"/setup-account#token={raw}");
 }
 public async Task<Guid?> ConsumeAsync(NpgsqlConnection c,NpgsqlTransaction tx,string token,CredentialTokenPurpose purpose,CancellationToken ct)
 {
  if(!Enum.IsDefined(purpose)||token is not {Length:64}||token.Any(ch=>!char.IsAsciiHexDigit(ch)))return null;
  var hash=SHA256.HashData(Encoding.UTF8.GetBytes(token));Guid user;
  await using(var find=new NpgsqlCommand("SELECT user_id FROM local_credential_tokens WHERE token_hash=@hash AND purpose=@purpose",c,tx))
  {find.Parameters.AddWithValue("hash",hash);find.Parameters.AddWithValue("purpose",purpose.ToString());if(await find.ExecuteScalarAsync(ct) is not Guid id)return null;user=id;}
  // Caller has already acquired company/authority locks. User -> credentials -> token is shared with issuance.
  await using(var gate=new NpgsqlCommand("SELECT u.id FROM users u JOIN local_credentials l ON l.user_id=u.id WHERE u.id=@user AND u.active FOR UPDATE OF u,l",c,tx))
  {gate.Parameters.AddWithValue("user",user);if(await gate.ExecuteScalarAsync(ct) is null)return null;}
  await using(var gate=new NpgsqlCommand("SELECT id FROM local_credential_tokens WHERE token_hash=@hash FOR UPDATE",c,tx)){gate.Parameters.AddWithValue("hash",hash);await gate.ExecuteScalarAsync(ct);}
  // Sample time after every lock wait, never before it.
  await using var q=new NpgsqlCommand("UPDATE local_credential_tokens t SET consumed_at=clock_timestamp() FROM local_credentials l WHERE t.token_hash=@hash AND t.purpose=@purpose AND t.user_id=l.user_id AND t.credential_revision=l.revision AND t.consumed_at IS NULL AND t.revoked_at IS NULL AND t.expires_at>clock_timestamp() RETURNING t.user_id",c,tx);
  q.Parameters.AddWithValue("hash",hash);q.Parameters.AddWithValue("purpose",purpose.ToString());return await q.ExecuteScalarAsync(ct) is Guid result?result:null;
 }
 public async Task RevokeAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid userId,CancellationToken ct)
 {
  await using var q=new NpgsqlCommand("UPDATE local_credential_tokens SET revoked_at=clock_timestamp() WHERE user_id=@user AND revoked_at IS NULL AND consumed_at IS NULL",c,tx);q.Parameters.AddWithValue("user",userId);await q.ExecuteNonQueryAsync(ct);
 }
}
