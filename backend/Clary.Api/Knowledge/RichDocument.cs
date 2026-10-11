using System.Text;
using System.Text.Json;
namespace Clary.Api;
public static class RichDocument {
 public static (string Content,string Text) Read(string content){
  if(content.Length>1500000)throw new ArgumentException("The document is too large.");
  try{
   using var doc=JsonDocument.Parse(content,new JsonDocumentOptions{MaxDepth=40});var text=new StringBuilder();var count=0;
   void Visit(JsonElement node,int depth,string? parent=null){
    if(depth>20||++count>20000||node.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Invalid document structure.");
    var type=node.GetProperty("type").GetString();
    if(type=="inventory"){if(parent is not null||!node.TryGetProperty("category",out var category)||!(category.GetString() is "Computer" or "Printer" or "Monitor" or "Network" or "Accessory" || (category.GetString() is string code && code.StartsWith("custom-") && Guid.TryParseExact(code[7..],"N",out _)))||node.EnumerateObject().Any(p=>p.Name is not ("type" or "category")))throw new ArgumentException("Invalid linked inventory block.");text.AppendLine($"[Live {category.GetString()!.ToLowerInvariant()} inventory — view current records in Inventory.]");return;}
    if(type is not ("text" or "p" or "h1" or "h2" or "h3" or "ul" or "ol" or "li" or "strong" or "em" or "u" or "br" or "span" or "a" or "table" or "thead" or "tbody" or "tr" or "td" or "th" or "img" or "hr"))throw new ArgumentException("Unsupported text formatting.");
    if((type is "td" or "th")&&parent!="tr"||type=="tr"&&parent is not ("table" or "thead" or "tbody")||(type is "thead" or "tbody")&&parent!="table")throw new ArgumentException("Invalid table structure.");
    if(node.TryGetProperty("children",out var tableChildren)){
     if(type=="tr"&&tableChildren.GetArrayLength()>26)throw new ArgumentException("A table supports up to 26 columns.");
     if(type=="table"){var rows=0;foreach(var part in tableChildren.EnumerateArray()){var kind=part.GetProperty("type").GetString();if(kind=="tr")rows++;else if(kind is "thead" or "tbody")rows+=part.GetProperty("children").GetArrayLength();else throw new ArgumentException("Invalid table structure.");}if(rows>100)throw new ArgumentException("A table supports up to 100 rows.");}
     if((type is "thead" or "tbody" or "tr"))foreach(var child in tableChildren.EnumerateArray()){var kind=child.GetProperty("type").GetString();if(type=="tr"?kind is not ("td" or "th"):kind!="tr")throw new ArgumentException("Invalid table structure.");}
    }
    if(node.TryGetProperty("formula",out var formula)&&(type is not ("td" or "th")||formula.GetString() is not string f||f.Length>200||!f.StartsWith("=")||!System.Text.RegularExpressions.Regex.IsMatch(f,@"^[=A-Za-z0-9_:+*/().,\s!#-]+$")))throw new ArgumentException("Invalid table formula.");
    if(type=="a"){
     var href=node.GetProperty("href").GetString()??"";
     var internalLink=href.StartsWith("#document=")&&Guid.TryParseExact(href[10..],"D",out _);
     if(href.Length>2048||(!internalLink&&(!Uri.TryCreate(href,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https"))))throw new ArgumentException("Use a document link or an http/https website link.");
    }
    foreach(var key in new[]{"color","background"})if(node.TryGetProperty(key,out var c)&&!(key=="background"&&c.GetString()=="transparent")&&!System.Text.RegularExpressions.Regex.IsMatch(c.GetString()??"","^#[0-9a-fA-F]{6}$"))throw new ArgumentException("Invalid text color.");
    if(node.TryGetProperty("align",out var align)&&align.GetString() is not ("left" or "center" or "right"))throw new ArgumentException("Invalid alignment.");
    if(node.TryGetProperty("listStyle",out var style)&&style.GetString() is not ("disc" or "circle" or "square" or "dash"))throw new ArgumentException("Invalid bullet style.");
    if(type=="img"){var src=node.GetProperty("src").GetString()??"";if(!src.StartsWith("/api/library/images/")||!Guid.TryParseExact(src[20..],"D",out _))throw new ArgumentException("Invalid image source.");var alt=node.TryGetProperty("alt",out var description)?description.GetString():"Image";if(alt?.Length>500)throw new ArgumentException("Image description is too long.");text.AppendLine("[Image: "+alt+"]");return;}
    if(type=="text"){text.Append(node.GetProperty("text").GetString());return;}
    if(type=="hr"){text.AppendLine("---");return;}
    if(type=="br"){text.AppendLine();return;}
    if(type is "h1" or "h2" or "h3")text.Append(type=="h1"?"# ":type=="h2"?"## ":"### ");
    if(type=="li")text.Append("- ");
    if(node.TryGetProperty("children",out var children))foreach(var child in children.EnumerateArray())Visit(child,depth+1,type);
    if(type=="a")text.Append(" ("+node.GetProperty("href").GetString()+")");
    if(type is "td" or "th")text.Append("\t");
    if(type is "tr" or "table" or "p" or "h1" or "h2" or "h3" or "li")text.AppendLine();
   }
   foreach(var node in doc.RootElement.EnumerateArray())Visit(node,0);
   var result=text.ToString().Trim();if(string.IsNullOrWhiteSpace(result)||result.Length>250000)throw new ArgumentException("Add some text, up to 250,000 characters.");
   return (doc.RootElement.GetRawText(),result);
  }catch(Exception e)when(e is JsonException or InvalidOperationException or KeyNotFoundException){throw new ArgumentException("Invalid document content.");}
 }
}

