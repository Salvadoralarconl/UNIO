using Clary.Api;
using Xunit;
public class GuidancePresentationTests
{
    [Fact] public void RemovesDuplicateContainedExcerptsAndPreservesSourceOrder()
    {
        var p=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Guide","1","section","Confirm the profile.\n\nCheck DNS. Record the error.\n\nRetest the service.",null);
        AdviceStep Step(string text)=>new(text,[new(p.Id,text)]);
        var answer=new Answer("supported",[],[Step("Retest the service."),Step("Record the error."),Step("Check DNS. Record the error."),Step("Confirm the profile."),Step("Confirm the profile.")],[],"Live Ollama",null);
        var result=GuidancePresentation.Prepare(answer,[p]);
        Assert.Equal(new[]{"Confirm the profile.","Check DNS. Record the error.","Retest the service."},result.Steps.Select(s=>s.Text));
    }
}
