using Clary.Api;
using System.Net;
using System.Text.Json;
using Xunit;

public class BrieFieldHistoryTests
{
    const string Facts = "For the asset: asset type Accessory, status Available, no assigned employee, department none, location none.";
    const string Request = "It works. I need the inventory record: name Studio printer, tag MAP-P49, serial PR4901.";
    sealed class Router : IBrieIntentRouter
    {
        public async Task<BrieIntentDecision> InterpretAsync(BrieIntentContext input, CancellationToken ct)
        {
            if(input.Message != Request) return new("clarify");
            using var client = new HttpClient(new IntentModel()) { BaseAddress = new("http://fixture.invalid/") };
            return await new OllamaBrieIntentRouter(client, "fixture").InterpretAsync(input, ct);
        }
    }
    sealed class IntentModel : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body=await request.Content!.ReadAsStringAsync(ct);
            const string evidence="I need the inventory record: name Studio printer, tag MAP-P49, serial PR4901.";
            var content=body.Contains("requestKind")
                ? JsonSerializer.Serialize(new { operation="create_asset", requestKind="execute", evidence })
                : JsonSerializer.Serialize(new { intent="create_asset", evidence });
            return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new { done=true, done_reason="stop", message=new { content } })) };
        }
    }
    sealed class Parser : IBrieTicketParser
    {
        public BrieFieldInput? Input;
        public Task<BrieTicketPatch> ParseAsync(string text, CancellationToken ct) => throw new InvalidOperationException("Typed context required");
        public Task<BrieTicketPatch> ParseAsync(BrieFieldInput input, CancellationToken ct)
        {
            Input = input;
            var known = input.UserHistory?.Contains(Facts) == true;
            return Task.FromResult(new BrieTicketPatch(Name:"Studio printer", AssetTag:"MAP-P49", SerialNumber:"PR4901",
                AssetType:known ? "Accessory" : null, Status:known ? "Available" : null));
        }
    }
    [Theory]
    [InlineData("Can you sort out the new printer?", true)]
    [InlineData("New issue: Can you sort out the new printer?", false)]
    [InlineData("My camera failed", false)]
    public async Task ActionParserReceivesLegacyFactsOnlyWithinFirstIssue(string clarification, bool preserved) => await KnowledgeIsolationFixture.Run(async f =>
    {
        var history = new ConversationRepository(f.Db);
        var id = Guid.NewGuid();
        await history.SaveAsync(f.A, id, [new(Facts, new Answer("clarify", [], [], [], "Synthetic visible context", null))], default);
        var parser = new Parser();
        var actions = new BrieTickets(f.Db, new(f.Db), parser);
        var conversations = new ConversationStore((c,q,ctx,ct) => throw new InvalidOperationException("No retrieval needed"), "fixture", history);
        var orchestrator = new BrieOrchestrator(new Router(), actions, conversations, history);
        var first = await orchestrator.ReplyAsync(f.A, new(clarification, Guid.NewGuid(), id), default);
        Assert.Equal("clarify", first.Status);
        Assert.Null(first.TicketTask);
        Assert.NotNull(first.IssueId);
        var answer = await orchestrator.ReplyAsync(f.A, new(Request, Guid.NewGuid(), id), default);
        Assert.NotNull(parser.Input);
        Assert.Equal(preserved, parser.Input.UserHistory!.Contains(Facts));
        if (preserved)
        {
            Assert.Equal("completed", answer.TicketTask!.Status);
            var asset = Assert.Single(await new InventoryRepository(f.Db).ListAsync(f.A, default));
            Assert.Equal("Available", asset.Status);
            Assert.Equal("Studio printer", asset.Computer);
            Assert.Equal("MAP-P49", asset.AssetTag);
            Assert.Equal("PR4901", asset.SerialNumber);
        }
        else Assert.Empty(await new InventoryRepository(f.Db).ListAsync(f.A, default));
    });
    [Fact] public async Task TrailingLegacyTurnCannotRebindAnExistingIssue() => await KnowledgeIsolationFixture.Run(async f =>
    {
        var history=new ConversationRepository(f.Db);var id=Guid.NewGuid();var existingIssue=Guid.NewGuid();
        await history.SaveAsync(f.A,id,[new(Facts,new Answer("clarify",[],[],[],"fixture",null){IssueId=existingIssue}),new("Those are the details",new Answer("clarify",[],[],[],"fixture",null))],default);
        var actions=new BrieTickets(f.Db,new(f.Db),new Parser());
        var conversations=new ConversationStore((c,q,ctx,ct)=>throw new InvalidOperationException("No retrieval needed"),"fixture",history);
        await new BrieOrchestrator(new Router(),actions,conversations,history).ReplyAsync(f.A,new("Please use those details",Guid.NewGuid(),id),default);
        var saved=(await history.LoadAsync(f.A,id,default))!;
        Assert.Equal(existingIssue,saved[0].Answer.IssueId);Assert.Null(saved[1].Answer.IssueId);
    });
}
