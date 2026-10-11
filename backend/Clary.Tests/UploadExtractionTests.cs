using Clary.Api;
using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Xunit;
public class UploadExtractionTests
{
    [Theory][InlineData("guide.txt")][InlineData("guide.md")]
    public void PlainTextPreservesSource(string filename)=>Assert.Equal("Check the USB cable.",DocumentExtractor.Extract(filename,Encoding.UTF8.GetBytes("Check the USB cable.")));
    [Fact] public void DocxIncludesParagraphsAndTableCellText() {
        using var buffer=new MemoryStream();
        using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true)) {
            using var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>First check</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Table check</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>");
        }
        Assert.Equal("First check\n\nTable check",DocumentExtractor.Extract("guide.docx",buffer.ToArray()));
    }
    [Fact] public void PdfExtractsSelectableText() {
        var builder=new PdfDocumentBuilder();var page=builder.AddPage(300,200);
        page.AddText("Check the printer cable.",12,new PdfPoint(20,100),builder.AddStandard14Font(Standard14Font.Helvetica));
        Assert.Contains("Check the printer cable.",DocumentExtractor.Extract("guide.pdf",builder.Build()));
    }
    [Theory][InlineData("guide.exe")][InlineData("empty.txt")]
    public void UnsupportedOrEmptyFilesAreRejected(string filename)=>Assert.Throws<ArgumentException>(()=>DocumentExtractor.Extract(filename,[]));
    [Fact] public void ImageOnlyPdfCannotBePresentedAsIndexedText() {
        var builder=new PdfDocumentBuilder();builder.AddPage(300,200);
        Assert.Throws<ArgumentException>(()=>DocumentExtractor.Extract("scan.pdf",builder.Build()));
    }
    [Fact] public void DocxEntityInstructionsAreNotProcessed() {
        using var buffer=new MemoryStream();
        using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true)) {
            using var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            writer.Write("<!DOCTYPE doc [<!ENTITY instruction 'Unexpected expanded text'>]><doc>&instruction;</doc>");
        }
        Assert.Throws<ArgumentException>(()=>DocumentExtractor.Extract("hostile.docx",buffer.ToArray()));
    }
}
