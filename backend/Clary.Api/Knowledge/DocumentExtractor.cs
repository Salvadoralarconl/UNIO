using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
namespace Clary.Api;
public static class DocumentExtractor
{
    public const int MaxBytes=15*1024*1024;
    public const int MaxCharacters=250_000;
    public static string Extract(string filename,byte[] bytes) {
        if(bytes.Length==0||bytes.Length>MaxBytes)throw new ArgumentException("Choose a nonempty file no larger than 15 MB.");
        string text;
        try {
            switch(Path.GetExtension(filename).ToLowerInvariant()) {
                case ".txt":case ".md":text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');break;
                case ".docx":
                    using(var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read)) {
                        var entry=zip.GetEntry("word/document.xml")??throw new ArgumentException("The DOCX has no document text.");
                        if(entry.Length>5*1024*1024)throw new ArgumentException("The DOCX text is too large.");
                        using var reader=XmlReader.Create(entry.Open(),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=5*1024*1024});
                        XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        var document=XDocument.Load(reader);
                        text=string.Join("\n\n",document.Descendants(w+"p").Select(p=>string.Concat(p.Descendants().Select(e=>e.Name==w+"t"?e.Value:e.Name==w+"tab"?"\t":e.Name==w+"br"?"\n":""))).Where(p=>!string.IsNullOrWhiteSpace(p)));
                    }break;
                case ".pdf":
                    using(var pdf=PdfDocument.Open(bytes)) {
                        if(pdf.NumberOfPages>300)throw new ArgumentException("PDFs are limited to 300 pages.");
                        var result=new StringBuilder();
                        foreach(var page in pdf.GetPages()) {
                            result.AppendLine($"[PDF page {page.Number}]");result.AppendLine(ContentOrderTextExtractor.GetText(page));result.AppendLine();
                            if(result.Length>MaxCharacters)throw new ArgumentException("Extracted text exceeds 250,000 characters.");
                        }
                        text=result.ToString();
                        if(!pdf.GetPages().Any(p=>p.Letters.Count>0))throw new ArgumentException("This PDF has no selectable text. Scanned PDFs require OCR, which is not available yet.");
                    }break;
                default:throw new ArgumentException("Supported files: PDF, DOCX, TXT, and Markdown (.md).");
            }
        }catch(ArgumentException){throw;}
        catch(Exception ex)when(ex is not OutOfMemoryException){throw new ArgumentException("The file could not be read. Use a valid, unencrypted PDF/DOCX or UTF-8 text file.");}
        if(string.IsNullOrWhiteSpace(text)||text.IndexOf('\0')>=0)throw new ArgumentException("The file contains no usable text.");
        if(text.Length>MaxCharacters)throw new ArgumentException("Extracted text exceeds 250,000 characters. Upload a smaller document.");
        return text;
    }
}
