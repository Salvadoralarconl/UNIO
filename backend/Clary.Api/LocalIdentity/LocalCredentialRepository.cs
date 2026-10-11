using Npgsql;
namespace Clary.Api.LocalIdentity;
public sealed class LocalCredentialRepository
{
 public const string Issuer="https://brie.local/identity";
 public static string NormalizeUsername(string username)
 {
  var value=username?.Trim();
  if(value is not {Length: >=3 and <=64}||value.Any(ch=>!(ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-')))throw new ArgumentException("A username of 3 to 64 ASCII letters, digits, dots, underscores or hyphens is required.");
  return value.ToLowerInvariant();
 }
 public static void ValidateNames(string firstName,string lastName)
 {
  if(string.IsNullOrWhiteSpace(firstName)||string.IsNullOrWhiteSpace(lastName)||firstName.Length>100||lastName.Length>100||firstName.Any(char.IsControl)||lastName.Any(char.IsControl))throw new ArgumentException("First and last names of 1 to 100 characters are required.");
 }
 public async Task<Guid> CreateIdentityAsync(NpgsqlConnection c,NpgsqlTransaction tx,string? firstName,string? lastName,string? email,CancellationToken ct)
 {
  if(firstName is not null||lastName is not null)ValidateNames(firstName!,lastName!);
  if(email?.Length>320)throw new ArgumentException("Email is too long.");
  var id=Guid.NewGuid();
  await using var q=new NpgsqlCommand("INSERT INTO users(id,issuer,identity_subject,display_name,email) VALUES(@id,@issuer,@subject,@name,@email)",c,tx);
  q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("issuer",Issuer);q.Parameters.AddWithValue("subject",Guid.NewGuid().ToString("D"));q.Parameters.AddWithValue("name",firstName is null?"Operator":firstName+" "+lastName);q.Parameters.AddWithValue("email",NpgsqlTypes.NpgsqlDbType.Text,(object?)email??DBNull.Value);await q.ExecuteNonQueryAsync(ct);return id;
 }
 public async Task CreateUnsetAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid userId,CancellationToken ct)
 {
  await using var q=new NpgsqlCommand("INSERT INTO local_credentials(user_id) VALUES(@user)",c,tx);q.Parameters.AddWithValue("user",userId);await q.ExecuteNonQueryAsync(ct);
 }
 public async Task SetAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid userId,string username,string firstName,string lastName,string passwordHash,CancellationToken ct)
 {
  var normalized=NormalizeUsername(username);ValidateNames(firstName,lastName);if(string.IsNullOrEmpty(passwordHash))throw new ArgumentException("Password hash required.");
  await using(var gate=new NpgsqlCommand("SELECT id FROM users WHERE id=@user FOR UPDATE",c,tx)){gate.Parameters.AddWithValue("user",userId);await gate.ExecuteScalarAsync(ct);}
  await using var q=new NpgsqlCommand("WITH changed AS (UPDATE local_credentials SET username=@username,normalized_username=@normalized,first_name=@first,last_name=@last,password_hash=@hash,revision=revision+1,failed_attempts=0,locked_until=NULL WHERE user_id=@user RETURNING user_id) UPDATE users SET display_name=@first||' '||@last FROM changed WHERE users.id=changed.user_id RETURNING users.id",c,tx);
  q.Parameters.AddWithValue("username",username.Trim());q.Parameters.AddWithValue("normalized",normalized);q.Parameters.AddWithValue("first",firstName);q.Parameters.AddWithValue("last",lastName);q.Parameters.AddWithValue("hash",passwordHash);q.Parameters.AddWithValue("user",userId);if(await q.ExecuteScalarAsync(ct) is null)throw new InvalidOperationException("Local credentials must already exist.");
 }
 public async Task<LocalProfile?> ProfileAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid userId,CancellationToken ct)
 {
  await using var q=new NpgsqlCommand("SELECT username,first_name,last_name,password_hash IS NOT NULL FROM local_credentials WHERE user_id=@user",c,tx);q.Parameters.AddWithValue("user",userId);await using var r=await q.ExecuteReaderAsync(ct);return await r.ReadAsync(ct)?new(r.IsDBNull(0)?null:r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetBoolean(3)):null;
 }
}
