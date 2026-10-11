namespace Clary.Api;
public static class WikiImages {
 public static string? ContentType(byte[] b){
 if(b.Length>=8&&b.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))return "image/png";
 if(b.Length>=3&&b[0]==255&&b[1]==216&&b[2]==255)return "image/jpeg";
 if(b.Length>=6&&(System.Text.Encoding.ASCII.GetString(b,0,6) is "GIF87a" or "GIF89a"))return "image/gif";
 if(b.Length>=12&&System.Text.Encoding.ASCII.GetString(b,0,4)=="RIFF"&&System.Text.Encoding.ASCII.GetString(b,8,4)=="WEBP")return "image/webp";
 return null;
 }
}
