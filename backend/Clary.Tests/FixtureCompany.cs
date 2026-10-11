using Clary.Api;
using Clary.Api.Identity;
using Npgsql;
internal static class FixtureCompany {
 internal static readonly CompanyContext Context=new(Guid.Parse("00000000-0000-0000-0000-000000000001"),Guid.NewGuid(),CompanyRole.Administrator,null,null);
 internal static async Task AddActorAsync(NpgsqlDataSource db,CompanyContext context){
  await using var q=db.CreateCommand("INSERT INTO users(id,issuer,identity_subject,display_name) VALUES(@user,'fixture-actor',@subject,'Fixture actor') ON CONFLICT(id) DO NOTHING; INSERT INTO memberships(company_id,user_id,role) VALUES(@company,@user,@role) ON CONFLICT(user_id) DO NOTHING");
  q.Parameters.AddWithValue("user",context.UserId);q.Parameters.AddWithValue("subject",context.UserId.ToString());q.Parameters.AddWithValue("company",context.CompanyId);q.Parameters.AddWithValue("role",context.Role.ToString());await q.ExecuteNonQueryAsync();
 }
 internal static async Task MigrateAsync(NpgsqlDataSource db){
  await DatabaseMigrations.ApplyAsync(db,default);
  await using(var actor=db.CreateCommand("INSERT INTO users(id,issuer,identity_subject,display_name) VALUES(@id,'test','fixture','Fixture actor') ON CONFLICT DO NOTHING")){actor.Parameters.AddWithValue("id",Context.UserId);await actor.ExecuteNonQueryAsync();}
  await AddActorAsync(db,Context);
  // Legacy fixture seed SQL is local to its isolated schema, never production.
  foreach(var table in new[]{"documents","passages","library_folders","document_folders"}){await using var q=db.CreateCommand($"ALTER TABLE {table} ALTER COLUMN company_id SET DEFAULT '{Context.CompanyId}'");await q.ExecuteNonQueryAsync();}
 }
}
