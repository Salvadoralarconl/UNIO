using Clary.Api;
using System.Text.Json;

// Stress a literal later selection with real observation/support checks; synthetic
// source and facts, no database, tools or business records. This is a seen gate.
using var client=new HttpClient(new Trace()){BaseAddress=new Uri("http://100.79.42.5:11434/"),Timeout=TimeSpan.FromSeconds(90)};
var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Synthetic sign-in guide","1","Procedure","1. Use the approved identity verification process before resetting credentials or enrollment. A display name alone is not identity proof.\n2. Direct the verified employee to the approved reset/enrollment flow.\n3. If an unsolicited prompt appears, stop and report possible compromise.",null);
var modelVerifier=new ModelSupportVerifier(client,"Ollama","qwen3:8b");
IAnswerVerifier verifier=args.Contains("--boundary-only")?new BoundaryVerifier(modelVerifier):modelVerifier;
var cases=new[]{
 (Id:"positive",Reports:"The approved identity verification process has been completed for this employee. The employee needs the approved reset/enrollment flow for the old phone.",Expected:"supported"),
 (Id:"missing",Reports:"The password is accepted but verification goes to the old phone.",Expected:"clarify"),
 (Id:"negative",Reports:"The approved identity verification process has not been completed.",Expected:"clarify"),
 (Id:"unknown",Reports:"I am not sure whether the approved identity verification process is completed.",Expected:"clarify"),
 (Id:"quoted",Reports:"The guide says 'the approved identity verification process is completed'.",Expected:"clarify"),
 (Id:"correction",Reports:"The approved identity verification process was completed earlier.\nCorrection: it has not been completed for this employee.",Expected:"clarify")
};
var failed=false;
foreach(var test in cases){
 var service=new TroubleshootingService(new Embedding(),new LiteralLater(),new Repository(source),"synthetic",verifier,documentationDriven:true);
 var result=await service.AskAsync(test.Reports,default);var passed=result.Status==test.Expected&&(test.Expected=="supported"?result.Steps.Count==1:result.Steps.Count==0);
 failed|=!passed;Console.WriteLine(JsonSerializer.Serialize(new{kind="case",test.Id,test.Reports,test.Expected,passed,result}));
}
Environment.ExitCode=failed?1:0;
sealed class Embedding:IEmbeddingProvider {public EmbeddingProfile Profile=>new("fixture","fixture",2,"fixture");public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> q,CancellationToken ct)=>Task.FromResult<IReadOnlyList<float[]>>([[1,0]]);}
sealed class LiteralLater:IAnswerProvider {public Task<Answer> GenerateAsync(string q,IReadOnlyList<Passage> p,CancellationToken ct){var option=AtomicSourceActions.Options(p)[1];return Task.FromResult(new Answer("supported",[],[new(option.Text,[new(option.PassageId,option.EvidenceQuote)])],[],"synthetic",null));}}
sealed class Repository(Passage source):IKnowledgeRepository {public Task<IReadOnlyList<Passage>> SearchAsync(float[] q,EmbeddingProfile p,int limit,CancellationToken ct)=>Task.FromResult<IReadOnlyList<Passage>>([source]);public Task<Passage?> GetApprovedPassageAsync(Guid id,CancellationToken ct)=>Task.FromResult<Passage?>(source);}
// Bypass only initial symptom classification to exercise the source safety boundary
// directly. Observation, independent entailment and final support remain real calls.
sealed class BoundaryVerifier(ModelSupportVerifier inner):IAnswerVerifier {
 public Task<bool?> ObservePrerequisiteAsync(string c,string f,CancellationToken ct)=>inner.ObservePrerequisiteAsync(c,f,ct);
 public Task<SupportReview> VerifyAsync(string q,Answer a,IReadOnlyList<Passage> p,CancellationToken ct)=>inner.VerifyAsync(q,a,p,ct);
 public Task<SupportReview> VerifyPrerequisiteAsync(string q,Answer a,IReadOnlyList<Passage> p,SourcePrerequisite prerequisite,CancellationToken ct)=>inner.VerifyPrerequisiteAsync(q,a,p,prerequisite,ct);
}
sealed class Trace:DelegatingHandler {
 public Trace():base(new HttpClientHandler()){}
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
  using var input=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var response=await base.SendAsync(request,ct);response.EnsureSuccessStatusCode();using var output=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
  Console.WriteLine(JsonSerializer.Serialize(new{kind="model-output",input=input.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString(),output=output.RootElement.GetProperty("message").GetProperty("content").GetString(),doneReason=output.RootElement.GetProperty("done_reason").GetString()}));return response;
 }
}
