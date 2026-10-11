using Clary.Api.Identity;
using Npgsql;
namespace Clary.Api;
public static class TicketAccess {
 public static bool Manager(CompanyContext c)=>AccountPermissions.Allows(c,CompanyCapability.Write);
 public static bool Administrator(CompanyContext c)=>c.Role==CompanyRole.Administrator&&c.VisibilityScope==CompanyVisibilityScope.Company;
 public static async Task<CompanyContext> LiveAsync(NpgsqlConnection conn,NpgsqlTransaction tx,CompanyContext c,CancellationToken ct){
  await AccountPermissions.RevalidateAsync(conn,tx,c,CompanyCapability.OwnInventory,ct);
  await using var q=new NpgsqlCommand("SELECT role,visibility_scope FROM memberships WHERE company_id=@company AND user_id=@user AND active",conn,tx);q.Parameters.AddWithValue("company",c.CompanyId);q.Parameters.AddWithValue("user",c.UserId);await using var r=await q.ExecuteReaderAsync(ct);if(!await r.ReadAsync(ct))throw new CompanyDenied();return c with{Role=Enum.Parse<CompanyRole>(r.GetString(0)),VisibilityScope=Enum.Parse<CompanyVisibilityScope>(r.GetString(1))};
 }
 public static bool Assigned(TicketDto t,CompanyContext c)=>t.AssignmentMode=="All"||t.Assignees.Any(u=>u.Id==c.UserId);
 public static IReadOnlyList<string> Actions(TicketDto t,CompanyContext c){
  if(t.IsWorkspaceSetup){if(!Administrator(c))return [];return t.Status is "Complete" or "Canceled"?["delete","comment"]:["delete","start","complete","comment"];}
  if(t.Status is "Complete" or "Canceled")return Assigned(t,c)||Administrator(c)?["comment"]:[];
  var result=new List<string>();var manager=Manager(c);if(manager){result.Add("edit");result.Add("priority");result.Add("cancel");if(t.Status=="Pending"&&AccountPermissions.Allows(c,CompanyCapability.Approve))result.Add("approve");}
  if(t.Status!="Pending"&&(Assigned(t,c)||Administrator(c)||t.RequestId is not null&&AccountPermissions.Allows(c,CompanyCapability.Approve))){if(t.Status!="In Progress")result.Add("start");result.Add("complete");}
  if(manager&&t.Type=="Repair"&&t.Status!="Pending")result.Add("send-for-repair");
  if(Assigned(t,c)||Administrator(c))result.Add("comment");return result;
 }
}
