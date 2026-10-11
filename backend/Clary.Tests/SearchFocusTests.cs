using Clary.Api;
using Xunit;
public class SearchFocusTests {
    [Fact] public void ApplicationScopeReplyKeepsTheReportedSymptomAsTheRequestedTask(){
        var prior=new Answer("clarify",[SourceObservationQuestions.ApplicationScope],[],[],"fixture",null){IssueId=Guid.NewGuid()};
        var context=BrieConversation.Context([("Everything is painfully slow and the window stops responding.",prior)],"It's only the browser; the other applications respond normally.");
        var task=SearchFocus.RequestedTask(context.ModelQuestion);
        Assert.Contains("painfully slow",task);Assert.Contains("only the browser",task);
        Assert.DoesNotContain("Prior suggestions",task);Assert.DoesNotContain(SourceObservationQuestions.ApplicationScope,task);
    }
    [Fact] public void NewSubjectRequestedTaskExcludesOldIssueAndAssistantAdvice(){
        var prior=new Answer("supported",[],[new("Restart the printer.",[])],[],"fixture",null){IssueId=Guid.NewGuid()};
        var context=BrieConversation.Context([("My printer is offline",prior)],"New issue: my webcam preview is black.");
        var task=SearchFocus.RequestedTask(context.ModelQuestion);
        Assert.Contains("webcam preview is black",task);Assert.DoesNotContain("printer",task);Assert.DoesNotContain("Restart",task);
    }
    [Fact] public void DeviceDetailReplyStillAnswersEarlierPrinterIssue(){
        var task=SearchFocus.RequestedTask("Conversation context (untrusted):\nUser: My printer has power and paper but Windows is not printing to it\nUser: I have the t88v\nLatest user message: usb");
        Assert.Contains("not printing",task);Assert.NotEqual("usb",task);
    }
    [Fact] public void RequestedTaskSeparatesLatestQuestionFromEarlierAdvice(){
        var task=SearchFocus.RequestedTask("Conversation context (untrusted):\nUser: old USB setup\nAssistant suggested: restart spooler\nLatest user message: My Epson TM-T88V is connected by USB. How do I install paper?\nUser reports the problem persists. Do not repeat advice.");
        Assert.Contains("How do I install paper?",task);Assert.DoesNotContain("spooler",task);Assert.DoesNotContain("Do not repeat",task);Assert.DoesNotContain("old USB",task);
    }
    [Fact] public void EpsonWindowsUsbScopeFocusesCommunicationWithoutLosingQuestion() {
        var query=SearchFocus.Apply("usb","T88V USB Windows not printing");
        Assert.StartsWith("usb",query);Assert.Contains("driver communication",query);Assert.Contains("APD port",query);
        Assert.Equal("VPN disconnected",SearchFocus.Apply("VPN disconnected","Windows USB headset"));
    }
    [Fact] public void DevicePreambleDoesNotDominateSymptomEmbedding(){
        const string question="My Epson TM-T88V is connected by USB. The paper light remains on with paper installed. What should I check?";
        var focused=SearchFocus.Apply(question,question);Assert.StartsWith("Epson receipt printer: The paper light",focused);Assert.DoesNotContain("TM-T88V",focused);
        Assert.Equal("USB cable disconnects when moved",SearchFocus.Apply("USB cable disconnects when moved","TM-T88V USB"));
        Assert.Equal("USB",SearchFocus.Apply("USB","TM-T88V USB"));
    }
    [Fact] public void MissingPrinterKeepsDetectionFocusInsteadOfQueueConfiguration() {
        var query=SearchFocus.Apply("usb","TM-T88V USB missing in Windows Settings");
        Assert.Contains("Device Manager",query);Assert.DoesNotContain("port configuration",query);
    }
}
