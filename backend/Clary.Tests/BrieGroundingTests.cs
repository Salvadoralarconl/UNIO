using Clary.Api;
using Clary.Api.Identity;
using Xunit;

public class BrieGroundingTests
{
    [Fact] public async Task ShortUploadedGuideKeepsWholeProcedureAndCurrentCitation()=>await KnowledgeIsolationFixture.Run(async f=>
    {
        const string text="# Camera\n\n## Procedure\n1. Close other camera applications.\n2. Choose the documented camera.\n3. Confirm the preview works.";
        using var stream=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        var uploaded=await f.Uploads.UploadAsync(f.A,new Microsoft.AspNetCore.Http.FormFile(stream,0,stream.Length,"file","camera.md"),"Camera guide","1",default);
        await f.Uploads.ApproveAndIndexAsync(f.A,uploaded.Id,default);
        var repository=new KnowledgeRepository(f.Db,.3);var profile=new KnowledgeIsolationFixture.TestEmbedding().Profile;
        var passage=Assert.Single(await repository.SearchAsync(f.A,[1,0],profile,8,default));
        Assert.True(passage.IsUploaded);Assert.Equal("Full document",passage.Location);
        var option=Assert.Single(CitationReferences.ProcedureOptions([passage]),o=>o.Text.StartsWith("1. Close"));
        Assert.Contains("3. Confirm",option.Text);
        Assert.Equal(passage,await repository.GetApprovedPassageAsync(f.A,passage.Id,default));
    });
    [Fact] public async Task ShortWikiProceduresRemainCompleteAcrossChunkBoundaries()=>await KnowledgeIsolationFixture.Run(async f=>
    {
        var text="# Troubleshooting\n\n"+new string('x',700)+"\n\n## Procedure\n"+
            string.Join("\n",Enumerable.Range(1,6).Select(n=>$"{n}. Check the documented device indicator {n} and record the result before continuing. "+new string('a',100)))+"\n\n## Verification\n\nConfirm the device works.";
        var page=await new LibraryOperations(f.Db).NewPageAsync(f.A,new("Complete procedure",text,null),default);
        await f.Uploads.ApproveAndIndexAsync(f.A,page.Id,default);
        var repository=new KnowledgeRepository(f.Db,.3);
        var hits=await repository.SearchAsync(f.A,[1,0],new KnowledgeIsolationFixture.TestEmbedding().Profile,8,default);
        var passage=Assert.Single(hits);
        Assert.Equal(text,passage.Text);
        Assert.Contains(CitationReferences.ProcedureOptions(hits),p=>p.Text.Contains("1. Check")&&p.Text.Contains("6. Check"));
        Assert.Equal(passage,await repository.GetApprovedPassageAsync(f.A,passage.Id,default));
    });
    [Fact] public async Task LongWikiGuidesKeepBoundedChunks()=>await KnowledgeIsolationFixture.Run(async f=>
    {
        var text=string.Join("\n\n",Enumerable.Range(1,20).Select(n=>$"Section {n}. Check the documented indicator. "+new string('a',250)));
        var page=await new LibraryOperations(f.Db).NewPageAsync(f.A,new("Long guide",text,null),default);
        await f.Uploads.ApproveAndIndexAsync(f.A,page.Id,default);
        var repository=new KnowledgeRepository(f.Db,.3);
        var hits=await repository.SearchAsync(f.A,[1,0],new KnowledgeIsolationFixture.TestEmbedding().Profile,8,default);
        Assert.NotEmpty(hits);
        Assert.All(hits,p=>Assert.True(p.Text.Length<=1200));
        Assert.All(hits,p=>Assert.NotEqual("Full document",p.Location));
    });
    [Fact] public void OneStepRetainsItsCompleteSupportingReferences()
    {
        var citation=new Citation(Guid.NewGuid(),"Check the documented status.");
        var result=BrieAnswerPolicy.Prepare(new("supported",[],[new(citation.Quote,[citation]),new("Next check",[citation])],[],"Test",null));
        Assert.Equal(citation,Assert.Single(Assert.Single(result.Steps).Citations));
    }
    [Fact] public void MissingDocumentationUsesExactRequiredMessage()
    {
        var result=BrieAnswerPolicy.Prepare(new("insufficient",[DiagnosticQuestions.All[0]],[],[],"Test",null,"Provider prose"));
        Assert.Equal("I couldn’t find a documented solution for this issue.",result.EvidenceNote);
        Assert.Empty(result.Steps);
        Assert.Empty(result.Questions);
    }
    [Fact] public async Task ArchivedDocumentsCannotBeSearchedOrCitedEvenIfApprovalWasLeftReady()=>await KnowledgeIsolationFixture.Run(async f=>
    {
        var operations=new LibraryOperations(f.Db);
        var page=await operations.NewPageAsync(f.A,new("Fixture procedure","Check the documented indicator.",null),default);
        await f.Uploads.ApproveAndIndexAsync(f.A,page.Id,default);
        var repository=new KnowledgeRepository(f.Db,.3);
        var profile=new KnowledgeIsolationFixture.TestEmbedding().Profile;
        var passage=Assert.Single(await repository.SearchAsync(f.A,[1,0],profile,8,default));
        await f.Sql($"UPDATE documents SET archived=true WHERE id='{page.Id}'");
        Assert.Empty(await repository.SearchAsync(f.A,[1,0],profile,8,default));
        Assert.Empty(await repository.ForCompany(f.A,true).SearchAsync([1,0],profile,32,default));
        Assert.Null(await repository.GetApprovedPassageAsync(f.A,passage.Id,default));
        // A stale archived profile must not poison another current search.
        await f.Sql($"UPDATE documents SET model='old-profile' WHERE id='{page.Id}'");
        Assert.Empty(await repository.SearchAsync(f.A,[1,0],profile,8,default));
    });
    [Fact] public async Task LiveMembershipRevocationDeniesRetrievalAndCitationWithStaleContext()=>await KnowledgeIsolationFixture.Run(async f=>
    {
        var page=await new LibraryOperations(f.Db).NewPageAsync(f.A,new("Fixture","Check the documented indicator.",null),default);
        await f.Uploads.ApproveAndIndexAsync(f.A,page.Id,default);
        var repository=new KnowledgeRepository(f.Db,.3);
        var profile=new KnowledgeIsolationFixture.TestEmbedding().Profile;
        var passage=Assert.Single(await repository.SearchAsync(f.A,[1,0],profile,8,default));
        await f.Sql($"UPDATE memberships SET active=false WHERE user_id='{f.A.UserId}'");
        await Assert.ThrowsAsync<CompanyDenied>(()=>repository.SearchAsync(f.A,[1,0],profile,8,default));
        await Assert.ThrowsAsync<CompanyDenied>(()=>repository.GetApprovedPassageAsync(f.A,passage.Id,default));
        await Assert.ThrowsAsync<CompanyDenied>(()=>repository.DocumentsAsync(f.A,default));
    });
    [Fact] public async Task EditsRequireFreshApprovalAndDeletionInvalidatesCitations()=>await KnowledgeIsolationFixture.Run(async f=>
    {
        var operations=new LibraryOperations(f.Db);
        var page=await operations.NewPageAsync(f.A,new("Fixture","Check the original indicator.",null),default);
        await f.Uploads.ApproveAndIndexAsync(f.A,page.Id,default);
        var repository=new KnowledgeRepository(f.Db,.3);
        var profile=new KnowledgeIsolationFixture.TestEmbedding().Profile;
        var original=Assert.Single(await repository.SearchAsync(f.A,[1,0],profile,8,default));
        var revision=await operations.EditAsync(f.A,page.Id,new("Fixture revised","",null,"[{\"type\":\"p\",\"children\":[{\"type\":\"text\",\"text\":\"Check the revised indicator.\"}]}]"),default);
        Assert.Empty(await repository.SearchAsync(f.A,[1,0],profile,8,default));
        Assert.Null(await repository.GetApprovedPassageAsync(f.A,original.Id,default));
        await f.Uploads.ApproveAndIndexAsync(f.A,revision.Id,default);
        var current=Assert.Single(await repository.SearchAsync(f.A,[1,0],profile,8,default));
        Assert.Equal(revision.Id,current.RevisionId);
        Assert.DoesNotContain("original",current.Text);
        // Delete an independent source: replacement-chain deletion is an existing library issue outside Brie scope.
        await f.Sql($"UPDATE documents SET archived=true WHERE id='{revision.Id}'");
        var deletable=await operations.NewPageAsync(f.A,new("Deletable","Check the removable indicator.",null),default);
        await f.Uploads.ApproveAndIndexAsync(f.A,deletable.Id,default);
        current=Assert.Single(await repository.SearchAsync(f.A,[1,0],profile,8,default));
        await operations.FileAsync(f.A,deletable.Id,new("delete",null,null),default);
        Assert.Empty(await repository.SearchAsync(f.A,[1,0],profile,8,default));
        Assert.Null(await repository.GetApprovedPassageAsync(f.A,current.Id,default));
    });
}
