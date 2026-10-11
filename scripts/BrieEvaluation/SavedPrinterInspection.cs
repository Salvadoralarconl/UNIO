using Clary.Api;
using Clary.Api.Identity;
using Npgsql;
using System.Text.Json;

internal static class SavedPrinterInspection
{
 public static async Task RunAsync(NpgsqlDataSource db,CompanyContext context,KnowledgeRepository repository)
 {
  // Supported offline review with live membership revalidation, not browser authentication.
  _=await repository.DocumentsAsync(context,default);
  await using var query=db.CreateCommand("SELECT c.id,c.updated_at,t.value::text FROM conversations c CROSS JOIN LATERAL jsonb_array_elements(c.turns) t(value) WHERE c.company_id=@company AND replace(t.value->>'question',chr(8217),chr(39))=@question ORDER BY c.updated_at DESC");
  query.Parameters.AddWithValue("company",context.CompanyId);
  query.Parameters.AddWithValue("question","It's an Epson TM-T88V connected by USB. Nothing prints, and only the green light is on.");
  var matches=new List<(Guid Id,DateTime Updated,SavedTurn Turn)>();
  await using(var reader=await query.ExecuteReaderAsync())while(await reader.ReadAsync())matches.Add((reader.GetGuid(0),reader.GetDateTime(1),JsonSerializer.Deserialize<SavedTurn>(reader.GetString(2),new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
  if(matches.Count!=1){Console.WriteLine(JsonSerializer.Serialize(new{matches=matches.Count,reviewed=false,reason="No unique exact saved-question match."}));return;}
  var match=matches[0];var steps=new List<object>();
  foreach(var step in match.Turn.Answer.Steps){
   var citations=new List<object>();
   foreach(var citation in step.Citations){var source=await repository.GetApprovedPassageAsync(context,citation.PassageId,default);citations.Add(new{citation.PassageId,title=source?.Title,approvedAccessible=source is not null,exactQuotePresent=source?.Text.Contains(citation.Quote,StringComparison.Ordinal)==true,substantive=CitationSupport.IsSubstantive(citation.Quote),stepVerbatim=step.Text==citation.Quote});}
   steps.Add(new{text=step.Text,citations});
  }
  var review=new{matches=1,reviewed=true,conversationId=match.Id,updatedAt=match.Updated,status=match.Turn.Answer.Status,questions=match.Turn.Answer.Questions,steps,source="persisted application record; no new model request"};
  var json=JsonSerializer.Serialize(review,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);await File.WriteAllTextAsync("artifacts/brie-saved-printer-inspection.json",json);
 }
}
