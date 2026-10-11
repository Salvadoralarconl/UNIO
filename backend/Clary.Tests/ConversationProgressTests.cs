using Clary.Api;
using Xunit;
public class ConversationProgressTests {
    [Fact] public void RewordingOldChecklistIsNotProgress() {
        string[] old=["Check that the USB cable is properly connected to both the printer and the computer. Ensure the cable is not damaged and the connection is secure.","Ensure the printer is powered on. If the printer is not responding, try turning it off and then back on.","Open the TM-T88V Utility and verify that the printer is detected. If not, click the Add Port button.","If detected, run a test print to confirm the printer is functioning correctly."];
        string[] repeated=["Check that the USB cable is securely connected to both the printer and computer. Ensure the cable is not damaged.","Ensure the printer is powered on. If it is not responding, try turning it off and then back on.",old[2],old[3]];
        Assert.True(ConversationProgress.RepeatsChecklist(repeated,old));
        Assert.True(ConversationProgress.RepeatsChecklist([..repeated,"Restart the Windows Print Spooler if the printer appears correctly but jobs do not print."],old));
        Assert.False(ConversationProgress.RepeatsChecklist(["Open Device Manager and report whether Windows shows an unknown USB device.","Check the APD port type and compare USB versus COM configuration."],old));
    }
    [Fact] public async Task UnresolvedFollowupCarriesPriorSuggestionsSeparatelyFromReportedAttempts() {
        ConversationContext? captured=null;
        var store=new ConversationStore((q,c,ct)=>{captured=c;return Task.FromResult(new Answer("supported",[],[new("Check the USB cable.",[])],[],"Test",null));},"Test");
        var first=await store.ReplyAsync(new("TM-T88V USB Windows printer not printing"),default);
        await store.ReplyAsync(new("nothing is working it appears as unavailable in settings",first.ConversationId),default);
        Assert.True(captured!.NeedsProgress);Assert.Equal("Check the USB cable.",Assert.Single(captured.PreviousAdvice!));
        Assert.Contains("not confirmed",captured.ModelQuestion);
        await store.ReplyAsync(new("Explain how to check the USB cable",first.ConversationId),default);
        Assert.False(captured!.NeedsProgress);
    }
}
