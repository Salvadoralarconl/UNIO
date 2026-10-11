using System.Text.RegularExpressions;
namespace Clary.Api;

public static class KnowledgeKeywords
{
    private static readonly HashSet<string> Filler=new(StringComparer.OrdinalIgnoreCase){
        "a","an","the","i","im","my","me","we","our","you","your","u","it","its","is","are","was","be","been",
        "can","could","would","will","please","pls","help","with","for","to","of","and","or","in","on","at","do","does",
        "how","what","why","when","where","need","want","some","about","hi","hello","hey","not","no"};
    // Only removes conversational filler; never invents device facts or domain terms.
    public static string Query(string question)=>string.Join(" ",Regex.Matches(question.ToLowerInvariant(),@"[\p{L}\p{N}]+")
        .Select(m=>m.Value).Where(word=>word.Length>1&&!Filler.Contains(word)).Distinct().Take(12));
}
