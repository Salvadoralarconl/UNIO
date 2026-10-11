using System.Text.RegularExpressions;
namespace Clary.Api;

// A narrow omission rule for an identity-only check, not a semantic fact extractor.
// Unknown identity fields keep the original source choice available.
public static class ReportedIdentity
{
 public static bool AlreadySupplied(AtomicSourceActions.Option option,string userFacts){
  if(Regex.IsMatch(userFacts,@"\b(?:maybe|possibly|might|don't know|do not know|not sure|unsure)\b",RegexOptions.IgnoreCase))return false;
  var text=Regex.Replace(option.Text,@"(?m)^#{1,6}[ \t]+(?:Warnings?|Important|Cautions?)\b[^\r\n]*(?:\r?\n(?!#{1,6}[ \t])[^\r\n]*)*","",RegexOptions.IgnoreCase).Trim();
  // This check asks only which application branch applies. An explicit positive
  // observation answers it; negative or uncertain observations retain the check.
  if(Regex.IsMatch(text,@"^Confirm whether (?:the )?(?:camera|webcam) works in Windows Camera or fails in every application[.]?$",RegexOptions.IgnoreCase))
   return Regex.IsMatch(userFacts,@"\b(?:camera|webcam)\s+works\s+in\s+Windows Camera\b|\bWindows Camera\s+works\b",RegexOptions.IgnoreCase)
    && !Regex.IsMatch(userFacts,@"\b(?:doesn't|does not|not|never)\s+work(?:s)?\s+in\s+Windows Camera\b",RegexOptions.IgnoreCase);
  if(!Regex.IsMatch(text,@"^(?:Identify|Record|Confirm|Determine)\b",RegexOptions.IgnoreCase)
   ||!Regex.IsMatch(text,@"\bmodel\b",RegexOptions.IgnoreCase)||!Regex.IsMatch(text,@"\bconnection\b",RegexOptions.IgnoreCase)
   ||Regex.IsMatch(text,@"\b(?:asset|tag|serial|operating|users?|department|location|error)\b",RegexOptions.IgnoreCase))return false;
  var connection=Regex.IsMatch(userFacts,@"\b(?:USB|Ethernet|Bluetooth|Wi-Fi|serial)\b",RegexOptions.IgnoreCase);
  var model=Regex.Matches(userFacts,@"\b(?=[A-Za-z0-9-]*[A-Za-z])(?=[A-Za-z0-9-]*\d)[A-Za-z0-9-]{2,}\b")
   .Any(m=>!Regex.IsMatch(m.Value,@"^(?:Windows|macOS|Ubuntu|iOS|Android|Office)\d",RegexOptions.IgnoreCase));
  return connection&&model;
 }
}
