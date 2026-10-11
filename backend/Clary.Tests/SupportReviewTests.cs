using Clary.Api;
using Xunit;
public class SupportReviewTests
{
    [Fact] public void ReviewSchemaCountsStepsRatherThanCitations() {
        var checks=ModelSupportVerifier.SchemaFor(1).GetProperty("properties").GetProperty("checks");
        Assert.Equal(1,checks.GetProperty("minItems").GetInt32());Assert.Equal(1,checks.GetProperty("maxItems").GetInt32());
        Assert.Equal(0,Assert.Single(checks.GetProperty("items").GetProperty("properties").GetProperty("step").GetProperty("enum").EnumerateArray()).GetInt32());
    }
    [Fact] public void HeadingsAndIntroductionsCannotSupportAnInstruction() {
        Assert.False(CitationSupport.IsSubstantive("If the printer is incorrectly configured as COM:"));
        Assert.False(CitationSupport.IsSubstantive("7. Restart the Windows Print Spooler"));
        Assert.True(CitationSupport.IsSubstantive("3. Change Port Type to USB."));
    }
    [Fact] public void ProvenanceOnlyChunkDoesNotCreateAnEmptyQuoteEnum() {
        var passage=new Passage(Guid.NewGuid(),Guid.NewGuid(),"Epson TM-T88V Troubleshooting — Curated Notes","1","tail","## Provenance\n\nSource: https://example.test",null);
        var schema=GroundedAnswerContract.SchemaFor([passage]).GetRawText();
        Assert.DoesNotContain("\"enum\":[]",schema);Assert.DoesNotContain("\"anyOf\":[]",schema);
    }
    [Fact] public void EveryStepMustHaveExactlyOnePositiveAssessment() {
        Assert.True(ModelSupportVerifier.Accepts("""{"checks":[{"step":0,"supported":true},{"step":1,"supported":true}]}""",2));
        Assert.False(ModelSupportVerifier.Accepts("""{"checks":[{"step":0,"supported":true}]}""",2));
        Assert.False(ModelSupportVerifier.Accepts("""{"checks":[{"step":0,"supported":true},{"step":0,"supported":true}]}""",2));
        Assert.False(ModelSupportVerifier.Accepts("""{"checks":[{"step":0,"supported":true},{"step":1,"supported":false}]}""",2));
        Assert.False(ModelSupportVerifier.Accepts("""{"checks":[{"step":4,"supported":true}]}""",1));
    }
    [Theory] [InlineData("{}")] [InlineData("not json")] [InlineData("{\"checks\":null}")]
    public void MalformedAssessmentFailsClosed(string content)=>Assert.False(ModelSupportVerifier.Accepts(content,1));
    [Fact] public void CoverageMustBeExplicitAndUsable() {
        var review=ModelSupportVerifier.Review("""{"checks":[{"step":0,"supported":true}],"coverage":"partial"}""",1);
        Assert.True(review.Supported);Assert.Equal("partial",review.Coverage);Assert.Equal(0,Assert.Single(review.SupportedSteps!));
        Assert.False(ModelSupportVerifier.Review("""{"checks":[{"step":0,"supported":true}]}""",1).Supported);
        Assert.False(ModelSupportVerifier.Review("""{"checks":[{"step":0,"supported":true}],"coverage":"none"}""",1).Supported);
    }
    [Fact] public void MixedSupportRejectsTheDraftToPreservePrerequisites() {
        var review=ModelSupportVerifier.Review("""{"checks":[{"step":0,"supported":true},{"step":1,"supported":false}],"coverage":"partial"}""",2);
        Assert.False(review.Supported);
    }
}
