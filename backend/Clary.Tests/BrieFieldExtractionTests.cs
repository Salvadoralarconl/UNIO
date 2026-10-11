using Clary.Api;
using System.Net;
using System.Text.Json;
using Xunit;

public class BrieFieldExtractionTests
{
    sealed class Parser(Func<string, BrieTicketPatch> extract) : IBrieTicketParser
    {
        public readonly List<string> Inputs = [];
        public Task<BrieTicketPatch> ParseAsync(string text, CancellationToken ct)
        {
            Inputs.Add(text);
            return Task.FromResult(extract(text));
        }
    }

    sealed class Handler(BrieTicketPatch patch) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                done = true, done_reason = "stop", message = new { content = JsonSerializer.Serialize(patch) }
            })) });
    }

    static BrieMessage M(string text, Guid? task = null, Guid? conversation = null) =>
        new(text, Guid.NewGuid(), ConversationId: conversation, TaskId: task);

    [Theory]
    [InlineData("Their name is Noor Patel.", "Noor Patel")]
    [InlineData("Back to that employee: their name is Noor Patel. Please finish adding them.", "Noor Patel")]
    [InlineData("Please use Zoë O'Neill for the name.", "Zoë O'Neill")]
    public async Task NaturalPendingNameStoresExtractedValue(string reply, string name) => await KnowledgeIsolationFixture.Run(async f =>
    {
        var parser = new Parser(text => text == reply ? new(Name: name) : new());
        var service = new BrieTickets(f.Db, new(f.Db), parser);
        var first = await service.ReplyAsync(f.A, M("Create employee"), default);
        var request = M(reply, first.TaskId);
        var done = await service.ReplyAsync(f.A, request, default);
        Assert.Equal("completed", done.Status);
        await using var read = f.Db.CreateCommand("SELECT display_name FROM employees WHERE id=@id");
        read.Parameters.AddWithValue("id", done.RecordId!.Value);
        Assert.Equal(name, await read.ExecuteScalarAsync());
        Assert.Equal(done, await service.ReplyAsync(f.A, request, default));
        Assert.Equal(2, parser.Inputs.Count);
    });

    [Theory]
    [InlineData("I don't know yet.")]
    [InlineData("Maybe Noor Patel, I am not sure.")]
    [InlineData("The note says \"their name is Noor Patel\"; that is only an example.")]
    [InlineData("Do not use Noor Patel.")]
    [InlineData("Cannot recall")]
    [InlineData("Still checking")]
    public async Task UnestablishedPendingNameDoesNotCreateEmployee(string reply) => await KnowledgeIsolationFixture.Run(async f =>
    {
        var parser = new Parser(_ => new());
        var service = new BrieTickets(f.Db, new(f.Db), parser);
        var first = await service.ReplyAsync(f.A, M("Create employee"), default);
        var next = await service.ReplyAsync(f.A, M(reply, first.TaskId), default);
        Assert.Equal("clarify", next.Status);
        Assert.Null((await service.InspectAsync(f.A, first.TaskId, default)).Name);
        await using var count = f.Db.CreateCommand("SELECT count(*) FROM employees");
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    });

    [Fact]
    public async Task PendingTitleAcceptsOtherExplicitFieldsAndKeepsDescription() => await KnowledgeIsolationFixture.Run(async f =>
    {
        const string reply = "Use title Laptop display stays black. Due 2026-11-02, priority High, unassigned.";
        var parser = new Parser(text => text == reply
            ? new(Title: "Laptop display stays black", DueDate: "2026-11-02", Priority: "High", Assignee: "unassigned")
            : new(Description: "The panel has no picture."));
        var repo = new TicketRepository(f.Db);
        var service = new BrieTickets(f.Db, repo, parser);
        var first = await service.ReplyAsync(f.A, M("Create ticket. The panel has no picture."), default);
        var result = await service.ReplyAsync(f.A, M(reply, first.TaskId), default);
        Assert.Equal("completed", result.Status);
        var row = await repo.GetAsync(f.A, result.TicketId!.Value, default);
        Assert.Equal("Laptop display stays black", row!.Title);
        Assert.Equal("The panel has no picture.", row.Description);
        Assert.Equal("High", row.Priority);
        Assert.Equal(new DateOnly(2026, 11, 2), row.DueDate);
    });

    [Fact]
    public async Task CurrentCorrectionChangesOnlyExplicitFields() => await KnowledgeIsolationFixture.Run(async f =>
    {
        const string reply = "Make priority High instead of Low. Due 2026-11-03.";
        var parser = new Parser(text => text == reply ? new(Priority: "High", DueDate: "2026-11-03")
            : new(Title: "Screen flickers", Priority: "Low", Assignee: "unassigned"));
        var service = new BrieTickets(f.Db, new(f.Db), parser);
        var first = await service.ReplyAsync(f.A, M("Create ticket Screen flickers, Low, unassigned"), default);
        var result = await service.ReplyAsync(f.A, M(reply, first.TaskId), default);
        var state = await service.InspectAsync(f.A, first.TaskId, default);
        Assert.Equal("completed", result.Status);
        Assert.Equal("Screen flickers", state.Title);
        Assert.Equal("High", state.Priority);
        Assert.Equal("unassigned", state.Assignee);
    });

    [Fact]
    public async Task PendingTagReplyPreservesNameAndAddsExplicitInventoryFields() => await KnowledgeIsolationFixture.Run(async f =>
    {
        const string reply = "The asset tag is MAP-D29. Type Accessory, status Available, serial SN-204.";
        var parser = new Parser(text => text == reply ? new(AssetTag: "MAP-D29", AssetType: "Accessory", Status: "Available", SerialNumber: "SN-204")
            : new(Name: "Meeting-room spare dock"));
        var service = new BrieTickets(f.Db, new(f.Db), parser);
        var first = await service.ReplyAsync(f.A, M("Create asset named Meeting-room spare dock"), default);
        var result = await service.ReplyAsync(f.A, M(reply, first.TaskId), default);
        Assert.Equal("completed", result.Status);
        var asset = Assert.Single(await new InventoryRepository(f.Db).ListAsync(f.A, default));
        Assert.Equal("MAP-D29", asset.AssetTag);
        Assert.Equal("Meeting-room spare dock", asset.Computer);
        Assert.Equal("SN-204", asset.SerialNumber);
    });

    [Theory]
    [InlineData("create")]
    [InlineData("asset")]
    public async Task InitialActionExtractsScopedUserHistoryAndNeverAssistantFields(string operation) => await KnowledgeIsolationFixture.Run(async f =>
    {
        const string facts = "Title Screen flickers; due 2026-11-04; priority High; unassigned. Asset name Spare dock; tag DOCK-204; type Accessory; status Available.";
        var conversation = Guid.NewGuid();
        var history = new ConversationRepository(f.Db);
        await history.SaveAsync(f.A, conversation, [new(facts, new Answer("clarify", ["Employee Mallory Fabricated, tag ASSISTANT-ONLY"], [], [], "fixture", null))], default);
        var parser = new Parser(text => operation == "create"
            ? new(Title: "Screen flickers", DueDate: "2026-11-04", Priority: "High", Assignee: "unassigned")
            : new(Name: "Spare dock", AssetTag: "DOCK-204", AssetType: "Accessory", Status: "Available"));
        var service = new BrieTickets(f.Db, new(f.Db), parser);
        var result = await service.ReplyAsync(f.A, M(operation == "create" ? "Create ticket" : "Create asset", conversation: conversation), default);
        Assert.Equal("completed", result.Status);
        Assert.Contains(facts, Assert.Single(parser.Inputs));
        Assert.DoesNotContain("ASSISTANT-ONLY", parser.Inputs[0]);
    });

    [Theory]
    [InlineData("Create employee. Do not use Noor Patel.", "Noor Patel")]
    [InlineData("Create employee. Maybe Noor Patel, but I am not sure.", "Noor Patel")]
    [InlineData("Create employee. The note says \"name Noor Patel\". Ask me for the actual name.", "Noor Patel")]
    [InlineData("Create employee Alex Chen instead of Noor Patel.", "Noor Patel")]
    public async Task ModelValueNeedsAffirmativeEvidenceNotJustSubstring(string message, string value)
    {
        using var client = new HttpClient(new Handler(new(Name: value))) { BaseAddress = new("http://synthetic.invalid/") };
        var patch = await new OllamaBrieTicketParser(client, "fixture").ParseAsync(message, "employee", default);
        Assert.Null(patch.Name);
    }

    [Theory]
    [InlineData("Create employee named \"Noor Patel\".", "Noor Patel")]
    [InlineData("Create employee Alex Chen, not Noor Patel.", "Alex Chen")]
    [InlineData("Create employee. Not Noor Patel; use Alex Chen.", "Alex Chen")]
    [InlineData("Create employee Zoë O'Neill.", "Zoë O'Neill")]
    public async Task AffirmativeNamesRemainLiteral(string message, string value)
    {
        using var client = new HttpClient(new Handler(new(Name: value))) { BaseAddress = new("http://synthetic.invalid/") };
        var patch = await new OllamaBrieTicketParser(client, "fixture").ParseAsync(message, "employee", default);
        Assert.Equal(value, patch.Name);
    }

    [Fact]
    public async Task PendingExtractionReceivesOperationAndFieldContext() => await KnowledgeIsolationFixture.Run(async f =>
    {
        var parser = new ContextParser();
        var service = new BrieTickets(f.Db, new(f.Db), parser);
        var first = await service.ReplyAsync(f.A, M("Create employee"), default);
        await service.ReplyAsync(f.A, M("Noor Patel", first.TaskId), default);
        Assert.Equal("employee", parser.Seen!.Operation);
        Assert.Equal("name", parser.Seen.PendingField);
        Assert.Equal("Noor Patel", parser.Seen.Message);
    });
    sealed class ContextParser : IBrieTicketParser
    {
        public BrieFieldInput? Seen;
        public Task<BrieTicketPatch> ParseAsync(string message, CancellationToken ct) => throw new InvalidOperationException("Context required");
        public Task<BrieTicketPatch> ParseAsync(BrieFieldInput input, CancellationToken ct)
        {
            Seen = input;
            return Task.FromResult(input.PendingField == "name" ? new BrieTicketPatch(Name: input.Message) : new());
        }
    }

    [Theory]
    [InlineData("Do not leave this unassigned.")]
    [InlineData("The example says unassigned.")]
    public async Task UnassignedFallbackAlsoRequiresAffirmativeEvidence(string message)
    {
        using var client = new HttpClient(new Handler(new())) { BaseAddress = new("http://synthetic.invalid/") };
        Assert.Null((await new OllamaBrieTicketParser(client, "fixture").ParseAsync(message, "create", default)).Assignee);
    }

    sealed class JsonHandler(Func<string,string> respond) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                done=true, done_reason="stop", message=new { content=respond(body) }
            })) };
        }
    }
    sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow()=>DateTimeOffset.Parse("2026-10-11T12:00:00Z"); }

    [Fact]
    public async Task ExplicitHistoricalTimezoneCompletesRelativeDateWithoutReasking() => await KnowledgeIsolationFixture.Run(async f =>
    {
        var conversation=Guid.NewGuid();
        const string facts="Title Screen flickers; due tomorrow; priority High; unassigned; time zone America/New_York.";
        await new ConversationRepository(f.Db).SaveAsync(f.A,conversation,[new(facts,new Answer("clarify",[],[],[],"fixture",null))],default);
        using var client=new HttpClient(new JsonHandler(_=>"{\"title\":\"Screen flickers\",\"dueDate\":\"tomorrow\",\"priority\":\"High\",\"assignee\":\"unassigned\",\"timeZoneId\":\"America/New_York\"}")){BaseAddress=new("http://synthetic.invalid/")};
        var repo=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repo,new OllamaBrieTicketParser(client,"fixture"),new Clock());
        var done=await service.ReplyAsync(f.A,M("Create ticket due tomorrow",conversation:conversation),default);
        Assert.Equal("completed",done.Status);
        Assert.Equal(new DateOnly(2026,10,12),(await repo.GetAsync(f.A,done.TicketId!.Value,default))!.DueDate);
    });

    [Fact]
    public async Task NaturalTimezoneReplyUsesExistingDateAnchor() => await KnowledgeIsolationFixture.Run(async f =>
    {
        using var client=new HttpClient(new JsonHandler(body=>body.Contains("Use UTC")?"{\"timeZoneId\":\"UTC\"}":"{\"title\":\"Screen flickers\",\"dueDate\":\"tomorrow\",\"priority\":\"High\",\"assignee\":\"unassigned\"}")){BaseAddress=new("http://synthetic.invalid/")};
        var repo=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repo,new OllamaBrieTicketParser(client,"fixture"),new Clock());
        var first=await service.ReplyAsync(f.A,M("Create ticket Screen flickers due tomorrow High unassigned"),default);
        var done=await service.ReplyAsync(f.A,M("Use UTC for the time zone.",first.TaskId),default,receivedAt:DateTimeOffset.Parse("2026-10-13T12:00:00Z"));
        Assert.Equal("completed",done.Status);
        Assert.Equal(new DateOnly(2026,10,12),(await repo.GetAsync(f.A,done.TicketId!.Value,default))!.DueDate);
    });

    [Fact]
    public async Task NaturalCalendarRefinementDoesNotReplaceTheDatePhrase() => await KnowledgeIsolationFixture.Run(async f =>
    {
        using var client=new HttpClient(new JsonHandler(body=>body.Contains("Make it the 14th")?"{\"dueDay\":\"14\"}":"{\"title\":\"Screen flickers\",\"dueDate\":\"next month\",\"priority\":\"High\",\"assignee\":\"unassigned\",\"timeZoneId\":\"UTC\"}")){BaseAddress=new("http://synthetic.invalid/")};
        var repo=new TicketRepository(f.Db);var service=new BrieTickets(f.Db,repo,new OllamaBrieTicketParser(client,"fixture"),new Clock());
        var first=await service.ReplyAsync(f.A,M("Create ticket Screen flickers due next month High unassigned UTC"),default);
        var done=await service.ReplyAsync(f.A,M("Make it the 14th, please.",first.TaskId),default);
        Assert.Equal("completed",done.Status);
        Assert.Equal(new DateOnly(2026,11,14),(await repo.GetAsync(f.A,done.TicketId!.Value,default))!.DueDate);
    });

    [Theory]
    [InlineData("unknown")]
    [InlineData("TBD")]
    [InlineData("no")]
    public async Task UnknownTagIsNotAnIdentifierShortcut(string reply) => await KnowledgeIsolationFixture.Run(async f=>
    {
        var parser=new Parser(text=>text.StartsWith("Create asset")?new(Name:"Spare dock",AssetType:"Accessory",Status:"Available"):new());
        var service=new BrieTickets(f.Db,new(f.Db),parser);
        var first=await service.ReplyAsync(f.A,M("Create asset Spare dock type Accessory status Available"),default);
        var next=await service.ReplyAsync(f.A,M(reply,first.TaskId),default);
        Assert.Equal("clarify",next.Status);Assert.Null((await service.InspectAsync(f.A,first.TaskId,default)).AssetTag);
        Assert.Empty(await new InventoryRepository(f.Db).ListAsync(f.A,default));
    });

    [Theory]
    [InlineData("I don't know yet.","I don't know yet.")]
    [InlineData("Noor Patel, perhaps.","Noor Patel")]
    [InlineData("Noor Patel maybe","Noor Patel")]
    public async Task MistakenModelMustNotTurnUncertainReplyIntoAName(string message,string value)
    {
        using var client=new HttpClient(new Handler(new(Name:value))){BaseAddress=new("http://synthetic.invalid/")};
        Assert.Null((await new OllamaBrieTicketParser(client,"fixture").ParseAsync(new BrieFieldInput(message,"employee","name"),default)).Name);
    }

    [Fact]
    public async Task TagEvidenceCannotReplaceStoredAssetName() => await KnowledgeIsolationFixture.Run(async f=>
    {
        var parser=new Parser(text=>text.StartsWith("Create asset")?new(Name:"Spare dock",AssetType:"Accessory",Status:"Available"):new(Name:"MAP-D29",AssetTag:"MAP-D29"));
        var service=new BrieTickets(f.Db,new(f.Db),parser);
        var first=await service.ReplyAsync(f.A,M("Create asset Spare dock type Accessory status Available"),default);
        var done=await service.ReplyAsync(f.A,M("The tag is MAP-D29.",first.TaskId),default);
        Assert.Equal("completed",done.Status);Assert.Equal("Spare dock",(await service.InspectAsync(f.A,first.TaskId,default)).Name);
    });

    [Fact]
    public async Task HistoricalRelativeDateRequiresAReaffirmedDateEntry() => await KnowledgeIsolationFixture.Run(async f=>
    {
        var id=Guid.NewGuid();await new ConversationRepository(f.Db).SaveAsync(f.A,id,[new("Title Screen flickers; due tomorrow; priority High; unassigned; time zone UTC.",new Answer("clarify",[],[],[],"fixture",null))],default);
        var service=new BrieTickets(f.Db,new(f.Db),new Parser(_=>new(Title:"Screen flickers",DueDate:"tomorrow",Priority:"High",Assignee:"unassigned",TimeZoneId:"UTC")),new Clock());
        var result=await service.ReplyAsync(f.A,M("Create ticket",conversation:id),default,receivedAt:DateTimeOffset.Parse("2026-10-13T12:00:00Z"));
        Assert.Equal("clarify",result.Status);Assert.Null((await service.InspectAsync(f.A,result.TaskId,default)).DueDate);
        Assert.Contains("due date",result.Message);
    });

    [Fact]
    public async Task ReplacedHistoricalFieldCannotBeRevivedByModel() => await KnowledgeIsolationFixture.Run(async f=>
    {
        var id=Guid.NewGuid();var answer=new Answer("clarify",[],[],[],"fixture",null);
        await new ConversationRepository(f.Db).SaveAsync(f.A,id,[new("Name Noor Patel",answer),new("Actually, name Alex Chen",answer)],default);
        var service=new BrieTickets(f.Db,new(f.Db),new Parser(_=>new(Name:"Noor Patel")));
        var result=await service.ReplyAsync(f.A,M("Create employee",conversation:id),default);
        Assert.Equal("clarify",result.Status);Assert.Null((await service.InspectAsync(f.A,result.TaskId,default)).Name);
    });
}
