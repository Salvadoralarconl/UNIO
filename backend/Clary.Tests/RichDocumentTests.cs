using Clary.Api;
using Xunit;
public class RichDocumentTests {
 [Fact] public void ExtractsHeadingsAndListTextWithoutLosingFormatting(){
  var content="""[{"type":"h1","children":[{"type":"strong","children":[{"type":"text","text":"USB guide"}]}]},{"type":"ul","listStyle":"square","children":[{"type":"li","children":[{"type":"text","text":"Check cable"}]}]}]""";
  var result=RichDocument.Read(content);Assert.Contains("USB guide",result.Text);Assert.Contains("Check cable",result.Text);Assert.Contains("square",result.Content);
 }
 [Fact] public void SupportsSafeColorsAndRejectsCssExpressions(){
  var good="""[{"type":"span","color":"#cc2222","background":"#ffee44","children":[{"type":"text","text":"Colored text"}]}]""";
  Assert.Equal("Colored text",RichDocument.Read(good).Text);Assert.Equal("Colored text",RichDocument.Read(good.Replace("#ffee44","transparent")).Text);
  Assert.Throws<ArgumentException>(()=>RichDocument.Read(good.Replace("#cc2222","url(unsafe)")));
 }
 [Fact] public void SupportsNamedLinksAndRejectsExecutableUrls(){
  var content="""[{"type":"a","href":"https://epson.com/support","children":[{"type":"text","text":"Epson support"}]}]""";
  Assert.Contains("Epson support (https://epson.com/support)",RichDocument.Read(content).Text);
  Assert.Throws<ArgumentException>(()=>RichDocument.Read(content.Replace("https://epson.com/support","javascript:alert(1)")));
  Assert.Contains("Epson support",RichDocument.Read(content.Replace("https://epson.com/support","#document=11111111-1111-1111-1111-111111111111")).Text);
 }
 [Theory][InlineData("[{\"type\":\"script\",\"children\":[]}]" )][InlineData("[{\"type\":\"p\",\"align\":\"bad\",\"children\":[]}]" )]
 public void RejectsUnsupportedContent(string content)=>Assert.Throws<ArgumentException>(()=>RichDocument.Read(content));
 [Fact] public void PreservesTableHeadersColorsFormulasAndExtractsRows(){
 var content="""[{"type":"table","children":[{"type":"tbody","children":[{"type":"tr","children":[{"type":"th","background":"#ddeeff","children":[{"type":"text","text":"Total"}]},{"type":"td","formula":"=SUM(A2:A3)","children":[{"type":"text","text":"35"}]}]}]}]}]""";
 var result=RichDocument.Read(content);Assert.Contains("Total\t35",result.Text);Assert.Contains("=SUM(A2:A3)",result.Content);Assert.Contains("#ddeeff",result.Content);
 Assert.Throws<ArgumentException>(()=>RichDocument.Read(content.Replace("=SUM(A2:A3)","=javascript:alert('x')")));
 Assert.Throws<ArgumentException>(()=>RichDocument.Read("""[{"type":"td","children":[{"type":"text","text":"Invalid"}]}]"""));
 }
 [Fact] public void ImagesPersistWithSafeLocalSourcesAndExtractDescriptions(){var content="""[{"type":"img","src":"/api/library/images/11111111-1111-1111-1111-111111111111","alt":"Network diagram"}]""";Assert.Contains("Network diagram",RichDocument.Read(content).Text);Assert.Throws<ArgumentException>(()=>RichDocument.Read(content.Replace("/api/library/images/11111111-1111-1111-1111-111111111111","javascript:alert(1)")));Assert.Equal("image/png",WikiImages.ContentType(new byte[]{137,80,78,71,13,10,26,10}));Assert.Null(WikiImages.ContentType(System.Text.Encoding.UTF8.GetBytes("<svg>unsafe</svg>")));}
 [Fact] public void PreservesDividerLines(){var result=RichDocument.Read("""[{"type":"hr"}]""");Assert.Equal("---",result.Text);Assert.Contains("hr",result.Content);}
}
