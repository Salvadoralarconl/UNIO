using Clary.Api;
using Xunit;
public class CitationReferenceTests
{
    [Fact] public void UploadedMarkdownProcedureIsSelectableBelowAnUnspacedHeading(){
        var source=Source with{Text="# Camera guide\n\n## Procedure\n1. Close other camera applications.\n2. Choose the documented camera.\n\n## Verify\nConfirm the preview works."};
        var option=Assert.Single(CitationReferences.ProcedureOptions([source]),o=>o.Text.StartsWith("1. Close"));
        Assert.Contains("2. Choose",option.Text);
        var answer=CitationReferences.ExpandProcedures(new("supported",[],[new(option.Reference,[])],[],"Test",null),[source]);
        Assert.True(CitationValidator.IsValid(answer,[source]));
    }
    [Fact] public void OverlappingChunkCannotOfferLeadingProcedureFragment(){
        var source=Source with{IsUploaded=false,Location="Extracted text · characters 120–980",Text="inter power.\n2. Retry printing.\n\n## Fix\n\n1. Turn off the printer.\n2. Remove accessible paper.\n\n## Next section"};
        var options=CitationReferences.ProcedureOptions([source]);Assert.Single(options);Assert.StartsWith("1. Turn off",options[0].Text);
        Assert.True(ProcedureApplicability.MissingWindowsPrinter("TM-T88V USB: no printer appears in Windows Printers and scanners"));
    }
    [Fact] public void NumberedContinuationCannotLoseItsEarlierPrerequisites(){
        var source=Source with{IsUploaded=false,Text="## Clear queue\n\n1. Open Services.\n2. Stop Print Spooler.\n3. Open:\n\n`C:\\Windows\\System32\\spool\\PRINTERS`\n\n5. Delete pending spool files.\n6. Restart Print Spooler."};
        var option=Assert.Single(CitationReferences.ProcedureOptions([source]));
        Assert.Contains("Stop Print Spooler",option.Text);Assert.Contains("C:\\Windows\\System32\\spool\\PRINTERS",option.Text);Assert.Contains("Delete pending",option.Text);Assert.Contains("Restart Print Spooler",option.Text);Assert.Contains(option.Text,source.Text);
        Assert.Empty(CitationReferences.ProcedureOptions([source with{Text="## Queue\n\n5. Delete pending spool files.\n6. Restart Print Spooler."}]));
    }
    [Fact] public void ConditionalConclusionKeepsItsHeading(){
        var source=Source with{IsUploaded=false,Text="### If the Self-Test Succeeds\n\nThe printer's basic internal printing mechanism is working."};
        var option=Assert.Single(CitationReferences.ProcedureOptions([source]));Assert.StartsWith("### If the Self-Test Succeeds",option.Text);Assert.Contains(option.Text,source.Text);
    }
    [Fact] public void ExampleSymptomsAreNotOfferedAsFactsAboutTheUser(){
        var source=Source with{IsUploaded=false,Text="## Wrong port type\n\n### Symptoms\n\nThe USB printer is configured as a serial COM port.\n\n### Fix\n\n1. Select the correct USB port.\n2. Test printing."};
        Assert.DoesNotContain(CitationReferences.ProcedureOptions([source]),o=>o.Text.Contains("configured as a serial"));
        Assert.Contains(CitationReferences.ProcedureOptions([source]),o=>o.Text.Contains("Select the correct USB port"));
    }
    [Fact] public void ExtractedChunkEndingInsideAListCannotOfferThePartialList(){
        var source=Source with{IsUploaded=false,Location="Extracted text · characters 200–1400",Text="Previous fragment.\n\n## Fix\n\n1. Open Services.\n2. Stop Print Spooler."};
        Assert.Empty(CitationReferences.ProcedureOptions([source]));
    }
    [Fact] public void ContinuationsAboveNineRemainWithTheirPrerequisites(){
        var source=Source with{IsUploaded=false,Text="## Fix\n\n1. Open the panel.\n2. Open:\n\n`Diagnostic`\n\n10. Record the result.\n11. Close the panel."};
        var option=Assert.Single(CitationReferences.ProcedureOptions([source]));Assert.Contains("1. Open",option.Text);Assert.Contains("10. Record",option.Text);
    }
    private static readonly Passage Source=new(Guid.NewGuid(),Guid.NewGuid(),"Uploaded guide","1","section","Check the USB connection.",null,[],true);
    private static Answer Draft(Citation c)=>new("supported",[],[new AdviceStep("Inspect the USB connection.",[c])],[],"Test",null);
    [Fact] public void ProceduralStepsAreSelectedSourceActionsRatherThanUncitedParaphrases(){
        var reference=CitationReferences.ProcedureOptions([Source])[0].Reference;
        var selected=new Answer("supported",[],[new AdviceStep(reference,[])],[],"Test",null);
        var result=CitationReferences.ExpandProcedures(selected,[Source]);Assert.Equal(Source.Text,result.Steps[0].Text);Assert.Equal(Source.Text,result.Steps[0].Citations[0].Quote);
        Assert.Throws<InvalidOperationException>(()=>CitationReferences.ExpandProcedures(Draft(new(Source.Id,CitationReferences.FullPassage)),[Source]));
        var schema=CitationReferences.ProcedureSchema([Source]);Assert.Contains("@action:0:0",schema.GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("properties").GetProperty("text").GetRawText());
    }
    [Fact] public void ProcedureReferencesCannotCollideAcrossPassages(){
        var other=Source with{Id=Guid.NewGuid(),Text="Restart the printer."};var options=CitationReferences.ProcedureOptions([Source,other]);Assert.Equal(options.Count,options.Select(o=>o.Reference).Distinct().Count());
        var selected=new Answer("supported",[],[new AdviceStep(options.Last().Reference,[])],[],"Test",null);var result=Assert.Single(CitationReferences.ExpandProcedures(selected,[Source,other]).Steps);Assert.Equal(other.Id,Assert.Single(result.Citations).PassageId);Assert.Equal(other.Text,result.Text);
    }
    [Fact] public void SelectedProcedureDoesNotRepeatItsConstituentActions(){
        var source=Source with{IsUploaded=false,Text="Check the power. Restart the printer."};
        var options=CitationReferences.ProcedureOptions([source]);var full=Assert.Single(options);Assert.Equal(source.Text,full.Text);
        var answer=new Answer("supported",[],[new(full.Reference,[]),new(full.Reference,[])],[],"Test",null);
        Assert.Equal(source.Text,Assert.Single(CitationReferences.ExpandProcedures(answer,[source]).Steps).Text);
    }
    [Fact] public void RetrievedReferenceExpandsToExactSourceText() {
        var result=CitationReferences.Expand(Draft(new(Source.Id,CitationReferences.FullPassage)),[Source]);
        Assert.Equal(Source.Text,result.Steps[0].Citations[0].Quote);Assert.True(CitationValidator.IsValid(result,[Source],true));
    }
    [Fact] public void ArbitraryIdDoesNotGainSourceAccess() {
        var result=CitationReferences.Expand(Draft(new(Guid.NewGuid(),CitationReferences.FullPassage)),[Source]);
        Assert.False(CitationValidator.IsValid(result,[Source],true));
    }
    [Fact] public void InventedQuoteIsNotRepaired() {
        var result=CitationReferences.Expand(Draft(new(Source.Id,"Invented procedure")),[Source]);
        Assert.Equal("Invented procedure",result.Steps[0].Citations[0].Quote);Assert.False(CitationValidator.IsValid(result,[Source],true));
    }
    [Fact] public void CuratedCompactReferencePreservesCompleteConditionalParagraph() {
        var source=Source with{Title="Epson TM-T88V Troubleshooting — Curated Notes",IsUploaded=false,Text="# Guide\n\nFor a USB printer, check the cable before changing its port."};
        var result=CitationReferences.Expand(Draft(new(source.Id,"@quote:0")),[source]);
        Assert.Equal("For a USB printer, check the cable before changing its port.",result.Steps[0].Citations[0].Quote);
        Assert.True(CitationValidator.IsValid(result,[source],true));
        Assert.False(CitationValidator.IsValid(CitationReferences.Expand(Draft(new(source.Id,"@quote:999")),[source]),[source],true));
    }
    [Fact] public void CompactSchemaContainsReferencesRatherThanLongQuotes() {
        var source=Source with{Title="Epson TM-T88V Troubleshooting — Curated Notes",IsUploaded=false,Text="# Guide\n\nFor a USB printer, check the cable before changing its port."};
        var schema=CitationReferences.CompactSchema([source]).GetRawText();
        Assert.Contains("@quote:0",schema);
        Assert.DoesNotContain("check the cable",schema);
        Assert.Equal("For a USB printer, check the cable before changing its port.",Assert.Single(CitationReferences.Options(source)).Text);
    }
}
