using Clary.Api;
using Xunit;
public class BrieConversationContextTests
{
    [Fact] public void TopicSwitchSearchesLatestTaskWithoutPriorIssueFacts(){
        var old=new Answer("insufficient",[],[],[],"Test",null);
        var context=BrieConversation.Context([("can u help me with account sign in",old)],"Employee transfer and offboarding");
        Assert.Equal("Employee transfer and offboarding",context.RetrievalQuestion);
        Assert.DoesNotContain("account sign in",context.ModelQuestion);
        Assert.EndsWith("Latest user message: Employee transfer and offboarding",context.ModelQuestion);
    }
    [Fact] public void ClarificationReplyKeepsEarlierUserDetails(){
        var old=new Answer("clarify",[DiagnosticQuestions.All[0]],[],[],"Test",null);
        var context=BrieConversation.Context([("My webcam has a black preview",old)],"Windows 11");
        Assert.Contains("black preview",context.RetrievalQuestion);Assert.Contains("Windows 11",context.RetrievalQuestion);
    }
    [Fact] public void ReportedFailureKeepsPreviousAdviceForProgressChecks(){
        var old=new Answer("supported",[],[new("Check the camera preview.",[])],[],"Test",null);
        var context=BrieConversation.Context([("My webcam is unavailable",old)],"I tried that and it did not work");
        Assert.True(context.NeedsProgress);Assert.Contains("webcam",context.RetrievalQuestion);
        Assert.Equal("Check the camera preview.",Assert.Single(context.PreviousAdvice!));
    }
}
