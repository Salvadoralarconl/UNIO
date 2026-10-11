using System.Text.RegularExpressions;
namespace Clary.Api;

// Present a source-selected scope observation as a neutral question, not a fix.
public static class SourceObservationQuestions
{
 public const string ApplicationScope="Does this happen in one application or across the whole computer?";
 public static bool IsApplicationScopeCheck(string text)=>Regex.IsMatch(text,@"^(?:Determine|Confirm|Identify) whether [^.!?\r\n]*\bone application\b[^.!?\r\n]*\bwhole computer\b[^.!?\r\n]*[.]?$",RegexOptions.IgnoreCase);
 public static bool HasApplicationScope(string facts)=>!Regex.IsMatch(facts,@"\b(?:maybe|possibly|might|unsure|not sure|don't know|do not know|not only)\b",RegexOptions.IgnoreCase)&&Regex.IsMatch(facts,@"\b(?:only one application|the whole computer)\s+(?:is|runs|feels)\s+slow\b|\b(?:happens|occurs)\s+(?:only )?in one application\b|\bonly (?:the )?browser\b[^.!?\r\n]*\bother applications\s+(?:respond|work|run) normally\b",RegexOptions.IgnoreCase);
}
