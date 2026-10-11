using System.Text.RegularExpressions;

namespace Clary.Api;
// Explicit demonstration provider, not an AI model. Profile is incompatible with OpenAI indexes.
public sealed class DeterministicProvider : IEmbeddingProvider,IAnswerProvider
{
    private static readonly string[] Terms=["vpn","profile","dns","internal","service","printer","queue","job","paper","timeout","policy","label","alignment","access","group","toner"];
    public EmbeddingProfile Profile=>new("Demo","lexical-fixture-v1",17,"chars-1200-160-v1");
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,CancellationToken ct)=>Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t=>{
        var words=Regex.Matches(t.ToLowerInvariant(),"[a-z]+").Select(m=>m.Value).ToHashSet();
        var vector=Terms.Select(term=>words.Contains(term)?1f:0f).Append(.01f).ToArray();return vector;
    }).ToArray());
    public Task<Answer> GenerateAsync(string question,IReadOnlyList<Passage> passages,CancellationToken ct)
    {
        var source=passages.FirstOrDefault(p=>p.Text.Contains("If you are connected to the VPN",StringComparison.Ordinal));
        if(source is null) return Task.FromResult(new Answer("insufficient",[],[],[],"Demo workspace",null));
        const string quote="Internal services require the Corporate Access profile.";
        return Task.FromResult(new Answer("supported",[],[new(quote,[new(source.Id,quote)])],[],"Demo workspace",null));
    }
}
