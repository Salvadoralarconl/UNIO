using Clary.Api;
using Xunit;
public class ProcedureApplicabilityTests
{
    private const string Missing="TM-T88V USB printer not appearing in Windows Settings";
    private static Passage Passage(string text)=>new(Guid.NewGuid(),Guid.NewGuid(),"Guide","1","section",text,null);
    [Theory]
    [InlineData("My USB printer doesn't show up in Windows Settings")]
    [InlineData("Windows Settings cannot find my USB printer")]
    [InlineData("Windows can't find my USB printer")]
    public void EquivalentMissingDeviceDescriptionsEnforcePrerequisites(string question) {
        Assert.True(ProcedureApplicability.MissingWindowsPrinter(question));
        var draft=new Answer("supported",[],[new AdviceStep("Restart the Print Spooler.",[])],[],"Test",null);
        Assert.False(ProcedureApplicability.IsValid(question,draft));
    }
    [Fact] public void MissingDeviceDoesNotRetrieveQueueOnlyProcedures() {
        var detection=Passage("Check Device Manager for the USB printer.");
        var queue=Passage("If the printer appears correctly, restart the Windows Print Spooler.");
        Assert.Equal(detection,Assert.Single(ProcedureApplicability.Select(Missing,[detection,queue])));
        Assert.Equal(2,ProcedureApplicability.Select("USB printer appears but print jobs are stuck",[detection,queue]).Count);
    }
    [Fact] public void ModelCannotBypassMissingDevicePrerequisite() {
        var draft=new Answer("supported",[],[new AdviceStep("Restart the Print Spooler.",[])],[],"Test",null);
        Assert.False(ProcedureApplicability.IsValid(Missing,draft));
        Assert.False(ProcedureApplicability.IsValid(Missing,draft with{Steps=[new AdviceStep("Ensure the printer is not marked as offline or paused and cancel stuck print jobs.",[])]}));
        Assert.True(ProcedureApplicability.IsValid("USB printer has stuck jobs",draft));
    }
}
