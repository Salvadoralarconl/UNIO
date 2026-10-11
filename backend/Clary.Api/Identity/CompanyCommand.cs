using Npgsql;
namespace Clary.Api.Identity;
public sealed class CompanyResourceMissing():Exception("This resource no longer exists.");
internal static class CompanyCommand {
 internal static NpgsqlCommand Create(CompanyContext context,string sql,NpgsqlDataSource db){var command=db.CreateCommand(sql);command.Parameters.AddWithValue("company",context.CompanyId);return command;}
 internal static NpgsqlCommand Create(CompanyContext context,string sql,NpgsqlConnection connection,NpgsqlTransaction? transaction=null){var command=new NpgsqlCommand(sql,connection,transaction);command.Parameters.AddWithValue("company",context.CompanyId);return command;}
}
