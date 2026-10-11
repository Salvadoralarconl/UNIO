using Clary.Api;
using Xunit;

public class GroundingTests
{
    private readonly Passage source = new(Guid.NewGuid(), Guid.NewGuid(), "VPN Guide", "2.1", "Section 3.2", "Use the Corporate Access profile. Check internal DNS resolution.", null);
    [Fact] public void ExactRetrievedQuoteIsAccepted() {
        var answer = Advice(source.Id, "Use the Corporate Access profile.");
        Assert.True(CitationValidator.IsValid(answer, [source]));
    }
    [Fact] public void ForgedPassageIsRejected() => Assert.False(CitationValidator.IsValid(Advice(Guid.NewGuid(), "Use the Corporate Access profile."), [source]));
    [Fact] public void FabricatedQuoteIsRejected() => Assert.False(CitationValidator.IsValid(Advice(source.Id, "Disable your firewall."), [source]));
    [Fact] public void UncitedAdviceIsRejected() => Assert.False(CitationValidator.IsValid(new("supported", [], [new("Restart", [])], [], "Demo", null), [source]));
    [Fact] public void AbstentionCannotContainFixes() => Assert.False(CitationValidator.IsValid(Advice(source.Id, "Use the Corporate Access profile.") with { Status = "insufficient" }, [source]));
    [Fact] public void EmptyQuoteIsRejected() => Assert.False(CitationValidator.IsValid(Advice(source.Id, ""), [source]));
    [Fact] public void RealButUnrelatedQuoteCannotSupportDisplayedStep() {
        var answer=new Answer("supported",[],[new("Disable your firewall.",[new(source.Id,"Use the Corporate Access profile.")])],[],"Live Ollama",null);
        Assert.False(CitationValidator.IsValid(answer,[source]));
    }
    [Fact] public void ConflictWithoutTwoDistinctQuotedPositionsIsRejected() {
        Assert.False(CitationValidator.IsValid(new("conflicting",[],[],[],"Live Ollama",null),[source]));
        var citation=new Citation(source.Id,"Use the Corporate Access profile.");
        Assert.False(CitationValidator.IsValid(new("conflicting",[],[],[citation,citation],"Live Ollama",null),[source]));
    }
    [Fact] public void QuestionsCannotSmuggleUncitedInstructions() {
        Assert.False(CitationValidator.IsValid(new("clarify",["Disable your firewall."],[],[],"Live Ollama",null),[]));
    }
    [Fact] public void ApplicationOwnedDiagnosticQuestionIsAllowed() {
        Assert.True(CitationValidator.IsValid(new("clarify",["What is the exact error message?"],[],[],"Live Ollama",null),[]));
    }
    private static Answer Advice(Guid id, string quote) => new("supported", [], [new(quote, [new(id, quote)])], [], "Demo", null);
}

public class ChunkerTests
{
    [Fact] public void ChunksPreserveExactOffsets() {
        var text = new string('a', 1400) + "\n" + new string('b', 1000);
        var chunks = Chunker.Split(text);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.Equal(c.Text, text.Substring(c.Start, c.Text.Length)));
        Assert.All(chunks, c => Assert.InRange(c.Text.Length, 1, 1200));
        Assert.Equal(text.Length, chunks[^1].Start + chunks[^1].Text.Length);
    }
    [Fact] public void EmptyDocumentHasNoChunks() => Assert.Empty(Chunker.Split("  \n"));
}
