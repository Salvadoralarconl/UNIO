using System.Globalization;
using System.Text;
using System.Text.Json;
namespace Clary.Api;
internal static class AssetFieldValidation {
 internal static AssetTypeDto Resolve(AssetConfigurationDto config,Guid? id,string code,Guid? previousId=null,string? previousCode=null){
  var type=(id.HasValue?config.Types.FirstOrDefault(t=>t.Id==id):config.Types.FirstOrDefault(t=>t.Code==code))??throw new ArgumentException("Choose an asset type in this company.");
  if(!type.Active&&type.Id!=previousId&&type.Code!=previousCode)throw new ArgumentException("Choose an active asset type.");
  return type;
 }
 internal static IReadOnlyDictionary<string,JsonElement> Merge(AssetConfigurationDto config,Guid typeId,IReadOnlyDictionary<string,JsonElement>? posted,IReadOnlyDictionary<string,JsonElement>? previous){
  var result=new Dictionary<string,JsonElement>(previous??new Dictionary<string,JsonElement>());
  var definitions=config.Fields.ToDictionary(f=>f.Id);
  var seen=new HashSet<Guid>();
  foreach(var pair in posted??new Dictionary<string,JsonElement>()){
   if(!Guid.TryParse(pair.Key,out var id)||!seen.Add(id)||!definitions.TryGetValue(id,out var field))throw new ArgumentException("Choose fields in this company.");
   var key=id.ToString();var value=pair.Value;var unchanged=result.TryGetValue(key,out var old)&&JsonElement.DeepEquals(old,value);
   var applicable=field.Active&&(field.AssetTypeId is null||field.AssetTypeId==typeId);
   if(!applicable){if(!unchanged)throw new ArgumentException("Historical fields cannot be changed for this asset type.");continue;}
   if(value.ValueKind==JsonValueKind.Null||(value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(value.GetString()))){result.Remove(key);continue;}
   // Removed choices are historical values; only an unchanged roundtrip is accepted.
   if(!(unchanged&&field.Kind=="choice"))Validate(field,value);
   result[key]=value.Clone();
  }
  foreach(var field in config.Fields.Where(f=>f.Active&&(f.AssetTypeId is null||f.AssetTypeId==typeId)&&f.Required)){
   if(!result.TryGetValue(field.Id.ToString(),out var value)||value.ValueKind==JsonValueKind.Null||(value.ValueKind==JsonValueKind.String&&string.IsNullOrWhiteSpace(value.GetString())))throw new ArgumentException(field.Name+" is required.");
  }
  if(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result))>65536)throw new ArgumentException("Custom fields must be at most 64KB.");
  return result;
 }
 static void Validate(AssetFieldDto field,JsonElement value){
  var valid=field.Kind switch{
   "shortText"=>value.ValueKind==JsonValueKind.String&&value.GetString()!.Length<=200,
   "longText"=>value.ValueKind==JsonValueKind.String&&value.GetString()!.Length<=4000,
   "number"=>value.ValueKind==JsonValueKind.Number&&value.TryGetDecimal(out _),
   "date"=>value.ValueKind==JsonValueKind.String&&DateOnly.TryParseExact(value.GetString(),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),
   "boolean"=>value.ValueKind is JsonValueKind.True or JsonValueKind.False,
   "choice"=>value.ValueKind==JsonValueKind.String&&field.Choices.Contains(value.GetString(),StringComparer.Ordinal),
   _=>false};
  if(!valid)throw new ArgumentException("Invalid value for "+field.Name+".");
 }
}
