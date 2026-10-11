using Clary.Api;
using Clary.Api.Identity;
using Xunit;

public class KnowledgeKeywordTests
{
    [Fact] public void ConversationalFillerDoesNotBecomeSearchRequirements(){
        Assert.Equal("account sign",KnowledgeKeywords.Query("can u help me with account sign in"));
        Assert.Equal("employee transfer offboarding",KnowledgeKeywords.Query("Employee transfer and offboarding"));
        Assert.Equal("",KnowledgeKeywords.Query("can u help me please"));
    }
    [Fact] public async Task LexicalFallbackFindsApprovedTitleAndCannotCrossCompanyOrApproval()=>await KnowledgeIsolationFixture.Run(async f=>{
        var ops=new LibraryOperations(f.Db);
        var own=await ops.NewPageAsync(f.A,new("Account sign-in and MFA issues","Record the exact sign-in error. Never request a password.",null),default);
        var foreign=await ops.NewPageAsync(f.B,new("Account sign-in","Foreign sentinel account sign-in.",null),default);
        var draft=await ops.NewPageAsync(f.A,new("Account sign-in draft","Draft sentinel sign-in.",null),default);
        await f.Uploads.ApproveAndIndexAsync(f.A,own.Id,default);await f.Uploads.ApproveAndIndexAsync(f.B,foreign.Id,default);
        var repository=new KnowledgeRepository(f.Db,1.1);var bound=repository.ForCompany(f.A);var profile=new KnowledgeIsolationFixture.TestEmbedding().Profile;
        Assert.Empty(await bound.SearchAsync([1,0],profile,5,default));
        var found=Assert.Single(await bound.SearchKeywordsAsync("can u help me with account sign in",profile,3,default));
        Assert.Equal(own.Id,found.RevisionId);Assert.DoesNotContain("sentinel",found.Text);
        Assert.Equal(found,await bound.GetApprovedPassageAsync(found.Id,default));
        await f.Sql($"UPDATE documents SET archived=true WHERE id='{own.Id}'");
        Assert.Empty(await bound.SearchKeywordsAsync("account sign in",profile,3,default));
        await f.Sql($"UPDATE memberships SET active=false WHERE user_id='{f.A.UserId}'");
        await Assert.ThrowsAsync<CompanyDenied>(()=>bound.SearchKeywordsAsync("account sign in",profile,3,default));
    });
    [Fact] public async Task LexicalSearchRanksSharedTermsWithoutRequiringEveryCasualWord()=>await KnowledgeIsolationFixture.Run(async f=>{
        var ops=new LibraryOperations(f.Db);
        var own=await ops.NewPageAsync(f.A,new("Account MFA replacement","Password works but the code goes to the old phone. Verify identity before replacing an authenticator.",null),default);
        var foreign=await ops.NewPageAsync(f.B,new("Old phone code","Foreign sentinel.",null),default);
        await f.Uploads.ApproveAndIndexAsync(f.A,own.Id,default);await f.Uploads.ApproveAndIndexAsync(f.B,foreign.Id,default);
        var bound=new KnowledgeRepository(f.Db,1.1).ForCompany(f.A);
        var found=Assert.Single(await bound.SearchKeywordsAsync("Password bit works fine. The next code goes to my old phone tho.",new KnowledgeIsolationFixture.TestEmbedding().Profile,3,default));
        Assert.Equal(own.Id,found.RevisionId);Assert.DoesNotContain("Foreign sentinel",found.Text);
    });
}
