using Clary.Api;
using Xunit;

public class AtomicSourceActionTests
{
 static Passage Source(string text)=>new(Guid.NewGuid(),Guid.NewGuid(),"Fixture guide","1","Full document",text,null);
 [Fact] public void GlobalMarkdownWarningBeforeProcedureIsAttachedWithoutBecomingAnAction(){
  var source=Source("# Troubleshooting\n\n## Warning\nDo not install or remove drivers during these checks.\n\n## Procedure\n1. Identify the device model and connection.\n2. Inspect the USB connection.");
  var options=AtomicSourceActions.Options([source]);Assert.Equal(2,options.Count);
  Assert.All(options,option=>{Assert.Contains("Do not install or remove drivers",option.Text);Assert.Contains(option.EvidenceQuote,source.Text);});
 }
 [Fact] public void LowercaseSentencesDoNotBypassUnsegmentedGuard(){
  Assert.Empty(AtomicSourceActions.Options([Source("## Procedure\n\nOpen the panel. stop the service. record the result.")]));
 }
 [Fact] public void MarkdownWarningSectionStaysWithEachStep(){
  var source=Source("## Procedure\n\n1. Open the panel.\n2. Record the result.\n\n## Warning\nDisconnect the affected device before opening its service cover.");
  var options=AtomicSourceActions.Options([source]);Assert.Equal(2,options.Count);
  Assert.All(options,option=>Assert.Contains("Disconnect the affected device",option.Text));
 }
 [Fact] public void UnnumberedMultiSentenceProcedureIsNotOfferedAsOneAction(){
  var source=Source("## Procedure\n\nOpen Services. Stop the documented service. Delete the pending files. Restart the service.");
  Assert.Empty(AtomicSourceActions.Options([source]));
 }
 [Fact] public void MandatoryWarningBeforeListStaysWithEveryAction(){
  var source=Source("Warning: Never disable protection.\n\n1. Open the panel.\n2. Record the result.");
  var options=AtomicSourceActions.Options([source]);Assert.Equal(2,options.Count);
  Assert.All(options,option=>Assert.Contains("Warning: Never disable protection.",option.Text));
 }
 [Fact] public void PlainCautionRemainsOnEachAction(){
  var source=Source("## Procedure\n\n1. Open the panel.\n2. Stop the documented service.\n\nCaution: Never remove unrelated files.");
  Assert.All(AtomicSourceActions.Options([source]),option=>Assert.Contains("Caution: Never remove unrelated files.",option.Text));
 }
 [Theory] [InlineData(".")] [InlineData(")")]
 public void PlainWarningsAndNumberingRemainBound(string delimiter){
  var source=Source($"## Procedure\n\n1{delimiter} Open the panel.\n2{delimiter} Stop the documented service.\n\nWarning: Never remove unrelated files.");
  var options=AtomicSourceActions.Options([source]);Assert.Equal(2,options.Count);
  foreach(var option in options){Assert.Contains("Warning: Never remove unrelated files.",option.Text);Assert.Contains(option.EvidenceQuote,source.Text);}
 }
 [Fact] public void NestedItemsStayWithinTheirParentSourceStep(){
  var source=Source("## Procedure\n1. Inspect the connection.\n   1. Read the label.\n   2. Compare the documented label.\n2. Record the result.");
  var options=AtomicSourceActions.Options([source]);Assert.Equal(2,options.Count);Assert.Contains("Read the label",options[0].Text);Assert.DoesNotContain("Record the result",options[0].Text);
 }
 [Fact] public void EachStepKeepsItsConditionalScopeAndWarning(){
  var source=Source("If the preview works:\n\n1. Close the application.\n2. Choose the documented camera.\n\n**Warning:** Never disable endpoint protection.");
  var options=AtomicSourceActions.Options([source]);Assert.Equal(2,options.Count);
  foreach(var option in options){Assert.Contains("If the preview works:",option.Text);Assert.Contains("Never disable endpoint protection",option.Text);Assert.Contains(option.EvidenceQuote,source.Text);}
 }
 [Fact] public void BoundSourceRecognitionDoesNotAcceptAddedInstructions(){
  var source=Source("## Procedure\n1. Open Services.\n2. Stop the documented service.");var option=AtomicSourceActions.Options([source])[0];
  var step=new AdviceStep(option.Text,[new Citation(source.Id,option.EvidenceQuote)]);Assert.True(AtomicSourceActions.IsExactBoundStep(step,[source]));Assert.False(AtomicSourceActions.IsExactBoundStep(step with{Text=step.Text+" Delete system files."},[source]));
 }
 [Fact] public void LeadingContinuationAndCutOffProcedureAreNotAtomicChoices(){
  var source=Source("## Procedure\n\n5. Delete the pending files.\n6. Restart the documented service.");Assert.Empty(AtomicSourceActions.Options([source]));
  var truncated=source with{Location="Extracted text · characters 100–1300",Text="Previous fragment.\n\n## Procedure\n\n1. Open Services.\n2. Stop the documented service."};Assert.Empty(AtomicSourceActions.Options([truncated]));
 }
}
