using Clary.Api;
using Xunit;

public class EpsonEvidenceTests
{
    [Fact] public void NamedModelExcludesOtherModelsAndGenericEvidence() {
        var matching=Source("Epson TM-T20III Troubleshooting — Curated Notes");
        var result=EpsonEvidence.Select("Run a TM-T20III self-test",[matching,Source("Epson TM-m30 Troubleshooting — Curated Notes"),Source("Printer Queue")]);
        Assert.False(result.NeedsModel); Assert.Equal(matching,Assert.Single(result.Passages));
    }
    [Theory]
    [InlineData("My Epson printer has an error")]
    [InlineData("Compare TM-T88V and TM-T20II")]
    [InlineData("My Epson TM-m30III has an error")]
    public void UnknownOrMultipleModelsRequireClarification(string question) {
        Assert.True(EpsonEvidence.Select(question,[Source("Epson TM-m30 Troubleshooting — Curated Notes")]).NeedsModel);
    }
    [Fact] public void GenericQuestionsRetainExistingEvidence() {
        var source=Source("VPN"); Assert.Equal(source,Assert.Single(EpsonEvidence.Select("VPN issue",[source]).Passages));
    }
    [Fact] public void UsbQuestionIncludesUserProvidedSharedProcedure() {
        var shared=Source("Epson USB/APD — User-provided — Check Epson APD Printer Configuration");
        Assert.Equal(shared,Assert.Single(EpsonEvidence.Select("My TM-T88V USB port is wrong",[shared]).Passages));
        Assert.Empty(EpsonEvidence.Select("My TM-T88V paper is jammed",[shared]).Passages);
        Assert.Empty(EpsonEvidence.Select("TM-T88V Ethernet: Windows printer not printing",[shared]).Passages);
        Assert.False(EpsonEvidence.IsCurated(shared));
    }
    [Fact] public void CuratedStepsMustBeCompleteProcedureParagraphs() {
        Assert.False(EpsonEvidence.IsProcedure("e the roll cover, press Feed."));
        Assert.False(EpsonEvidence.IsProcedure("For an Epson TM-m30 turn"));
        Assert.True(EpsonEvidence.IsProcedure("For an Epson TM-m30 check the cover."));
    }
    private static Passage Source(string title)=>new(Guid.NewGuid(),Guid.NewGuid(),title,"1","test","text",null);
}
