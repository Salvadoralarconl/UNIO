using System.Net;
using System.Text;
using System.Text.Json;
using Clary.Api;
using Xunit;

public class OllamaProviderTests
{
    [Theory] [InlineData(null,true)] [InlineData("one",false)] [InlineData("whole",false)]
    public async Task ScopeSelectionHintSkipsOnlyTheScopeQuestionAndKeepsBranchVerification(string? hint,bool questionAllowed){
        var payload="""{"done":true,"done_reason":"stop","message":{"content":"{\"status\":\"supported\",\"questions\":[],\"steps\":[{\"text\":\"@action:0:1\",\"citations\":[]}],\"citations\":[],\"origin\":\"ignored\",\"error\":null}"}}""";
        var handler=new Handler(payload);var provider=new OllamaAnswerProvider(Client(handler),"fixture");
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Scope guide","1","Full document","## Procedure\n1. Determine whether slowness affects only one application or the whole computer.\n2. If only one application is slow, record its name.",null);
        var context=BrieConversation.Context([],"The browser window is freezing; other programs remain responsive.") with{ApplicationScopeHint=hint};
        var answer=await provider.GenerateWithContextAsync(context.ModelQuestion,[source],context,default);using var request=JsonDocument.Parse(handler.Body!);
        var allowed=request.RootElement.GetProperty("format").GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("properties").GetProperty("text").GetProperty("enum").EnumerateArray().Select(v=>v.GetString()).ToArray();
        Assert.Equal(questionAllowed,allowed.Contains("@action:0:0"));Assert.Contains("If only one application is slow",Assert.Single(answer.Steps).Text);
    }
    [Theory]
    [InlineData("My webcam works in Windows Camera but not the desktop meeting app",true)]
    [InlineData("My webcam does not work in Windows Camera",false)]
    [InlineData("Maybe my webcam works in Windows Camera",false)]
    [InlineData("My webcam has a black preview",false)]
    public void ReportedCameraApplicationResultSkipsOnlyTheAnsweredCheck(string facts,bool expected){
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Camera","1","Full document","## Procedure\n1. Confirm whether the camera works in Windows Camera or fails in every application.\n2. If Windows Camera works, open the meeting application's device settings and record which camera is selected.",null);
        var options=AtomicSourceActions.Options([source]);
        Assert.Equal(expected,ReportedIdentity.AlreadySupplied(options[0],facts));
        Assert.False(ReportedIdentity.AlreadySupplied(options[1],facts));
    }
    [Theory] [InlineData("Windows11 connected by USB",true)] [InlineData("Printer connected by USB",true)] [InlineData("Maybe Model Z7 connected by USB",true)]
    public async Task MissingOrUncertainIdentityKeepsTheIdentityCheck(string facts,bool expected){
        var payload="""{"done":true,"done_reason":"stop","message":{"content":"{\"status\":\"supported\",\"questions\":[],\"steps\":[{\"text\":\"@action:0:1\",\"citations\":[]}],\"citations\":[],\"origin\":\"ignored\",\"error\":null}"}}""";
        var handler=new Handler(payload);var provider=new OllamaAnswerProvider(Client(handler),"fixture");
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Device guide","1","Full document","## Procedure\n1. Identify the device model and connection.\n2. Inspect the USB connection.",null);
        await provider.GenerateAsync(facts,[source],default);using var request=JsonDocument.Parse(handler.Body!);
        var allowed=request.RootElement.GetProperty("format").GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("properties").GetProperty("text").GetProperty("enum").EnumerateArray().Select(v=>v.GetString());
        Assert.Equal(expected,allowed.Contains("@action:0:0"));
    }
    [Fact] public async Task ExcludedIdentityReferenceCannotBeDisplayedEvenIfModelReturnsIt(){
        var payload="""{"done":true,"done_reason":"stop","message":{"content":"{\"status\":\"supported\",\"questions\":[],\"steps\":[{\"text\":\"@action:0:0\",\"citations\":[]}],\"citations\":[],\"origin\":\"ignored\",\"error\":null}"}}""";
        var provider=new OllamaAnswerProvider(Client(new Handler(payload)),"fixture");var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Device guide","1","Full document","## Procedure\n1. Identify the device model and connection.\n2. Inspect the USB connection.",null);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>provider.GenerateAsync("Model Z7 USB no output",[source],default));
    }
    [Fact] public async Task KnownModelAndConnectionAreExcludedFromNewStepChoices(){
        var payload="""{"done":true,"done_reason":"stop","message":{"content":"{\"status\":\"supported\",\"questions\":[],\"steps\":[{\"text\":\"@action:0:1\",\"citations\":[]}],\"citations\":[],\"origin\":\"ignored\",\"error\":null}"}}""";
        var handler=new Handler(payload);var provider=new OllamaAnswerProvider(Client(handler),"fixture");
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Device guide","1","Full document","## Procedure\n1. Identify the device model and connection.\n2. Inspect the USB connection.",null);
        await provider.GenerateAsync("Model Z7 connected by USB, no output",[source],default);
        using var request=JsonDocument.Parse(handler.Body!);
        var allowed=request.RootElement.GetProperty("format").GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("properties").GetProperty("text").GetProperty("enum").EnumerateArray().Select(v=>v.GetString()).ToArray();
        Assert.DoesNotContain("@action:0:0",allowed);Assert.Contains("@action:0:1",allowed);
    }
    [Fact] public async Task UnsegmentedParagraphClarifiesWithoutDisplayingAllActions(){
        var handler=new Handler("must not be called");var provider=new OllamaAnswerProvider(Client(handler),"fixture");
        var passage=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Guide","1","Full document","## Procedure\n\nOpen Services. Stop the documented service. Delete the pending files. Restart the service.",null);
        var answer=await provider.GenerateAsync("Clear the queue",[passage],default);
        Assert.Equal("clarify",answer.Status);Assert.Empty(answer.Steps);Assert.Single(answer.Questions);Assert.Null(handler.Body);
    }
    [Theory]
    [InlineData("Printer", "Verify the intended USB connection.", "For network printers, record the configured address.")]
    [InlineData("Webcam", "Close the other camera application.", "Change the documented camera permission.")]
    public async Task NumberedGuideProducesOneSourceStepInsteadOfTheEntireChecklist(string title,string first,string second){
        var payload="""{"done":true,"done_reason":"stop","message":{"content":"{\"status\":\"supported\",\"questions\":[],\"steps\":[{\"text\":\"@action:0:0\",\"citations\":[]}],\"citations\":[],\"origin\":\"ignored\",\"error\":null}"}}""";
        var provider=new OllamaAnswerProvider(Client(new Handler(payload)),"fixture");
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),title,"1","Full document","## Procedure\n1. "+first+"\n2. "+second,null);
        var result=await provider.GenerateAsync("Help with the documented issue",[source],default);
        var step=Assert.Single(result.Steps);Assert.Equal(first,step.Text);Assert.DoesNotContain(second,step.Text);Assert.True(CitationValidator.IsValid(result,[source],true));
    }
    [Fact] public async Task AtomicStepKeepsItsConditionAndMandatoryWarning(){
        var payload="""{"done":true,"done_reason":"stop","message":{"content":"{\"status\":\"supported\",\"questions\":[],\"steps\":[{\"text\":\"@action:0:0\",\"citations\":[]}],\"citations\":[],\"origin\":\"ignored\",\"error\":null}"}}""";
        var provider=new OllamaAnswerProvider(Client(new Handler(payload)),"fixture");
        var source=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Conditional guide","1","Full document","If the local preview works:\n\n1. Close other camera applications.\n2. Choose the documented camera.\n\n**Warning:** Do not disable endpoint protection.",null);
        var result=await provider.GenerateAsync("The local preview works",[source],default);var step=Assert.Single(result.Steps);
        Assert.Contains("If the local preview works:",step.Text);Assert.Contains("Close other camera",step.Text);Assert.Contains("Do not disable endpoint protection",step.Text);Assert.DoesNotContain("Choose the documented camera",step.Text);Assert.True(CitationValidator.IsValid(result,[source],true));
    }
    [Fact] public async Task IncompleteProcedureOnlyEvidenceReturnsInsufficientWithoutGeneration(){
        var handler=new Handler("must not be called");var provider=new OllamaAnswerProvider(Client(handler),"qwen3:8b");
        var passage=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Guide","1","section","## Queue\n\n5. Delete pending spool files.\n6. Restart Print Spooler.",null);
        var answer=await provider.GenerateAsync("Clear the queue",[passage],default);
        Assert.Equal("insufficient",answer.Status);Assert.Null(handler.Body);
    }
    [Fact] public async Task GenerationUsesSchemaAndApplicationAssignedOrigin() {
        var handler=new Handler("""{"model":"qwen3:8b","done":true,"done_reason":"stop","message":{"role":"assistant","content":"{\"status\":\"insufficient\",\"questions\":[],\"steps\":[],\"citations\":[],\"origin\":\"forged\",\"error\":null}"}}""");
        var provider=new OllamaAnswerProvider(Client(handler),"qwen3:8b");
        var answer=await provider.GenerateAsync("vpn",[],default);
        Assert.Equal("Live Ollama",answer.Origin);Assert.Equal("insufficient",answer.Status);
        using var request=JsonDocument.Parse(handler.Body!);
        Assert.Equal("qwen3:8b",request.RootElement.GetProperty("model").GetString());
        Assert.False(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(request.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal("object",request.RootElement.GetProperty("format").GetProperty("type").GetString());
        Assert.False(request.RootElement.TryGetProperty("tools",out _));
        Assert.Equal(600,request.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
        Assert.False(request.RootElement.GetProperty("format").TryGetProperty("$defs",out _));
    }
    [Fact] public async Task TruncatedGenerationIsRejected() {
        var provider=new OllamaAnswerProvider(Client(new Handler("""{"done":true,"done_reason":"length","message":{"content":"{}"}}""")),"qwen3:8b");
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>provider.GenerateAsync("vpn",[],default));
        Assert.Contains("output limit",error.Message);
    }
    [Fact] public async Task MalformedGenerationHasSafeSpecificError() {
        var provider=new OllamaAnswerProvider(Client(new Handler("""{"done":true,"done_reason":"stop","message":{"content":"private-invalid-json"}}""")),"qwen3:8b");
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>provider.GenerateAsync("vpn",[],default));
        Assert.Contains("unreadable",error.Message);
        Assert.DoesNotContain("private-invalid-json",error.Message);
    }
    [Fact] public async Task WrongEmbeddingModelOrDimensionsRejectsIndex() {
        var profile=new EmbeddingProfile("Ollama","embeddinggemma",768,"chars-1200-160-v1");
        var provider=new OllamaEmbeddingProvider(Client(new Handler("""{"model":"wrong","embeddings":[[1,2]]}""")),profile);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>provider.EmbedAsync(["vpn"],default));
    }
    [Fact] public async Task ServerErrorsDoNotExposeBody() {
        var provider=new OllamaAnswerProvider(Client(new Handler("private-server-data",HttpStatusCode.InternalServerError)),"qwen3:8b");
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>provider.GenerateAsync("vpn",[],default));
        Assert.DoesNotContain("private-server-data",error.Message);
    }
    private static HttpClient Client(Handler handler)=>new(handler){BaseAddress=new("http://localhost:11434/")};
    private sealed class Handler(string response,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler {
        public string? Body{get;private set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            Body=await request.Content!.ReadAsStringAsync(ct);
            return new(status){Content=new StringContent(response,Encoding.UTF8,"application/json")};
        }
    }
}
