using Microsoft.Extensions.Logging;
using Clary.Api;using Clary.Api.Identity;using Clary.Api.Accounts;using Npgsql;using System.Text.Json;using System.Diagnostics;
if(args.Contains("--prompt-size")){PromptSizing.Run();return;}
await using var db=NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("CLARY_TEST_CONNECTION")!);
var context=new CompanyContext(Guid.Parse("e788cda2-f133-4464-9f7b-26b7ca51efbf"),Guid.Parse("693d3198-ec08-46dc-b45f-9a3914065573"),CompanyRole.Administrator,null,null);
using var client=new HttpClient(new EvaluationTimingHandler()){BaseAddress=new Uri((Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")??"http://127.0.0.1:11434").TrimEnd('/')+"/"),Timeout=TimeSpan.FromMinutes(3)};
var profile=new EmbeddingProfile("Ollama",Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_MODEL")??"embeddinggemma",int.Parse(Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_DIMENSIONS")??"768"),"chars-1200-160-v1");
var embeddings=new OllamaEmbeddingProvider(client,profile);var repo=new KnowledgeRepository(db,.45,3,2,.2);var model=Environment.GetEnvironmentVariable("OLLAMA_GENERATION_MODEL")??"qwen3:8b";
if(args.Contains("--inspect-saved-printer")){await SavedPrinterInspection.RunAsync(db,context,repo);return;}
var modelArg=args.FirstOrDefault(a=>a.StartsWith("--model=")); if(modelArg is not null){model=modelArg[8..];args=args.Where(a=>a!=modelArg).ToArray();}
if(args.Contains("--parser")){
 var parser=new OllamaBrieTicketParser(client,model);
 var cases=new[]{"Create a ticket titled Printer offline, due 2026-10-12, priority High, unassigned","Create employee Jane Example","Create asset with tag DEMO-42, name Office laptop, type Computer, status Available"};
 var observations=new List<object>();
 foreach(var question in cases){var timer=Stopwatch.StartNew();var patch=await parser.ParseAsync(question,default);var matched=question.Contains("ticket")?patch.Title=="Printer offline"&&patch.DueDate=="2026-10-12"&&patch.Priority=="High"&&patch.Assignee=="unassigned":question.Contains("employee")?patch.Name=="Jane Example":patch.AssetTag=="DEMO-42"&&patch.Name=="Office laptop"&&patch.AssetType=="Computer"&&patch.Status=="Available";var observation=new{question,seconds=timer.Elapsed.TotalSeconds,matched,fields=patch};observations.Add(observation);Console.WriteLine(JsonSerializer.Serialize(observation));if(!matched)Environment.ExitCode=1;}
 await File.WriteAllTextAsync("artifacts/brie-parser-live.json",JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));return;
}
using var logging=LoggerFactory.Create(builder=>builder.AddSimpleConsole());
var service=new TroubleshootingService(embeddings,new OllamaAnswerProvider(client,model,logging.CreateLogger<OllamaAnswerProvider>()),repo.ForCompany(context),"Live Ollama",new ModelSupportVerifier(client,"Ollama",model),repo.ForCompany(context,true),documentationDriven:true);
var results=new List<object>();
var retrievalOnly=args.Contains("--retrieval-only"); args=args.Where(a=>a!="--retrieval-only").ToArray();
var questions=args.Length>0?args:new[]{"My printer is offline and cannot be found. What should I check?","My webcam works in Windows Camera but not in Teams. What do I do?","What should IT prepare before a new employee starts?","What is our company Wi-Fi password?"};
foreach(var question in questions){var watch=Stopwatch.StartNew();try{var vector=(await embeddings.EmbedAsync([question],default))[0];var hits=await repo.SearchAsync(context,vector,profile,5,default);if(hits.Count==0)hits=await repo.SearchKeywordsAsync(context,question,profile,3,default);Console.WriteLine("QUESTION: "+question);Console.WriteLine("EVIDENCE: "+string.Join(" | ",hits.Select(h=>h.Title).Distinct()));Console.WriteLine("OPTIONS: "+CitationReferences.ProcedureOptions(hits).Count);if(retrievalOnly){foreach(var option in CitationReferences.ProcedureOptions(hits))Console.WriteLine("OPTION: "+option.Text[..Math.Min(option.Text.Length,240)]);continue;}var answer=await service.AskAsync(question,default);results.Add(new{question,seconds=watch.Elapsed.TotalSeconds,answer});Console.WriteLine("RESULT: "+answer.Status+"; citations="+answer.Steps.Sum(s=>s.Citations.Count)+"; questions="+string.Join(" | ",answer.Questions));}catch(Exception e){results.Add(new{question,error=e.Message});Console.WriteLine("ERROR: "+e.Message);}Console.WriteLine("SECONDS: "+watch.Elapsed.TotalSeconds);await File.WriteAllTextAsync("artifacts/brie-evaluation-"+model.Replace(":","-").Replace("/","-")+".json",JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}

// Test-helper instrumentation only. Store timing/token metadata, never prompts or credentials.
sealed class EvaluationTimingHandler:DelegatingHandler
{
 readonly List<object> observations=[];
 public EvaluationTimingHandler():base(new HttpClientHandler()){}
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
 {
  var stage=request.RequestUri?.AbsolutePath.Contains("embed")==true?"embedding":"model";
  if(stage=="model"&&request.Content is not null){
   using var input=JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
   if(input.RootElement.TryGetProperty("messages",out var messages)){
    var system=messages[0].GetProperty("content").GetString()??"";
    stage=system.StartsWith("You are Brie")?"source selection":system.StartsWith("Review an IT answer")?"context verification":system.StartsWith("Check whether")?"quote verification":system.StartsWith("Extract explicit fields")?"action field extraction":"model";
   }
  }
  var timer=Stopwatch.StartNew();var response=await base.SendAsync(request,ct);
  var bytes=await response.Content.ReadAsByteArrayAsync(ct);timer.Stop();
  if(response.IsSuccessStatusCode){
   using var output=JsonDocument.Parse(bytes);var root=output.RootElement;
   double? Seconds(string key)=>root.TryGetProperty(key,out var value)&&value.TryGetDouble(out var number)?number/1_000_000_000:null;
   long? Count(string key)=>root.TryGetProperty(key,out var value)&&value.TryGetInt64(out var number)?number:null;
   var observation=new{stage,httpSeconds=timer.Elapsed.TotalSeconds,serverSeconds=Seconds("total_duration"),loadSeconds=Seconds("load_duration"),promptSeconds=Seconds("prompt_eval_duration"),decodeSeconds=Seconds("eval_duration"),promptTokens=Count("prompt_eval_count"),outputTokens=Count("eval_count")};
   observations.Add(observation);Console.WriteLine("TIMING: "+JsonSerializer.Serialize(observation));
   await File.WriteAllTextAsync("artifacts/brie-ollama-timing.json",JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}),ct);
  }
  return response;
 }
}





