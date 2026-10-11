using System.Globalization;
using System.Text.Json;
using Clary.Api.Identity;
using Clary.Api.Directories;
using Npgsql;
namespace Clary.Api;

public sealed partial class BrieTickets
{
 async Task<(State,BrieTicketResult)> Inventory(NpgsqlConnection conn,NpgsqlTransaction tx,CompanyContext c,Guid id,State s,BrieMessage input,CancellationToken ct){
  (State,BrieTicketResult) Ask(string field,string question,IReadOnlyList<BrieChoice>? choices=null)=>(s with{Need=field},new(id,"clarify",question,choices));
  if(s.Operation=="employee"){
   await AccountPermissions.RevalidateAsync(conn,tx,c,CompanyCapability.Settings,ct);
   if(string.IsNullOrWhiteSpace(s.Name)||s.Name.Length>100)return Ask("name","What is the employee's name? This creates an inventory directory record, not a login account.");
   var existing=await Resolve(conn,tx,c,"employee",s.Name,ct);
   if(existing.Count>0&&input.Question!="create another"){
    if(s.Need=="employeeDuplicate"&&Guid.TryParse(input.Question,out var selected)&&existing.Any(e=>e.Id==selected))return Complete(s,"employee",selected,"Existing employee selected; no duplicate was created.");
    return Ask("employeeDuplicate","An employee with this name already exists. Choose that record, or say create another if you mean a different person.",existing);
   }
   Guid? department=null;
   if(!string.IsNullOrWhiteSpace(s.Department)&&!None(s.Department)){
    var found=await Resolve(conn,tx,c,"department",s.Department,ct);if(found.Count!=1)return Ask("department","Choose the intended active department, or say none.",found);department=found[0].Id;
   }
   var employee=await new DirectoryRepository(db).SaveEmployeeWithinAsync(conn,tx,c,null,new(s.Name,s.Email,department),ct);
   return Complete(s,"employee",employee.Id,"Inventory employee created. No login account or access permissions were created.");
  }
  var inventory=new InventoryRepository(db);
  if(s.Operation=="assign"){
   if(string.IsNullOrWhiteSpace(s.Target))return Ask("target","Which asset should I assign? Give its asset tag, exact name or ID.");
   var assets=await Resolve(conn,tx,c,"asset",s.Target,ct);if(assets.Count!=1)return Ask("target","Choose the exact accessible asset to assign.",assets);
   var asset=await inventory.GetAsync(c,assets[0].Id,ct)??throw new CompanyResourceMissing();
   if(string.IsNullOrWhiteSpace(s.Employee))return Ask("employee","Who should receive this asset? Give their inventory employee name or ID.");
   var people=await Resolve(conn,tx,c,"employee",s.Employee,ct);if(people.Count!=1)return Ask("employee","Choose the intended active inventory employee.",people);
   if(asset.Status is "Retired" or "In repair")throw new ArgumentException("This asset is retired or in repair. Resolve its status in Inventory before assigning it through Brie.");
   if(s.Token is not null||(asset.EmployeeId is Guid current&&current!=people[0].Id)){
    if(!Confirmed(s,input)||s.RecordId!=asset.Id||s.Revision!=asset.Revision)return InventoryPreview(id,s,asset,people[0]);
   }
   // The user's explicit recipient is resolved above; all unrelated current fields are preserved.
   var updated=await inventory.SaveWithinAsync(conn,tx,c,asset.Id,new(asset.AssetTag,asset.Computer,asset.SerialNumber,asset.IpAddress,Status:"Assigned",Revision:asset.Revision,Category:asset.Category,CustomCategory:asset.CustomCategory,EmployeeId:people[0].Id,DepartmentId:asset.DepartmentId,LocationId:asset.LocationId,AssetTypeId:asset.AssetTypeId,CustomFields:asset.CustomFields),true,ct,provisionDocumentation:false);
   return Complete(s,"asset",updated.Id,"Asset assigned to the selected employee.");
  }
  if(!string.IsNullOrWhiteSpace(s.IpAddress)&&!System.Net.IPAddress.TryParse(s.IpAddress,out _))return Ask("ipAddress","What is the valid IPv4 or IPv6 IP address? Say none to leave it blank.");
  if(string.IsNullOrWhiteSpace(s.AssetTag)||s.AssetTag.Length>100)return Ask("assetTag","What is the new asset's tag? (Up to 100 characters.)");
  if(string.IsNullOrWhiteSpace(s.Name)||s.Name.Length>100)return Ask("name","What is the asset's name? (Up to 100 characters.)");
  var definitions=await AssetConfigurationRepository.Read(conn,tx,c,ct);
  var types=definitions.Types.Where(t=>t.Active).ToArray();
  var type=types.Where(t=>t.Id.ToString()==s.AssetType||t.Name.Equals(s.AssetType,StringComparison.OrdinalIgnoreCase)||t.Code.Equals(s.AssetType,StringComparison.OrdinalIgnoreCase)).ToArray();
  if(type.Length!=1)return Ask("assetType","Which existing asset type should I use?",types.Select(t=>new BrieChoice(t.Id,t.Name)).ToArray());
  var status=new[]{"Available","Assigned","In repair","Retired"}.FirstOrDefault(x=>x.Equals(s.Status,StringComparison.OrdinalIgnoreCase));
  if(status is null)return Ask("status","What is its recorded status: Available, Assigned, In repair or Retired?");
  Guid? recipient=null,departmentId=null,locationId=null;
  if(status=="Assigned"&&string.IsNullOrWhiteSpace(s.Employee))return Ask("employee","Who is this asset assigned to? Give the inventory employee's name or ID.");
  if(!string.IsNullOrWhiteSpace(s.Employee)&&!None(s.Employee)){
   var matches=await Resolve(conn,tx,c,"employee",s.Employee,ct);if(matches.Count!=1)return Ask("employee","Choose the intended active inventory employee.",matches);recipient=matches[0].Id;
  }
  if(status=="Assigned"&&recipient is null)return Ask("employee","Assigned assets need an explicit recipient. What is the employee's name or ID?");
  if(status=="Available"&&recipient is not null)return Ask("status","You supplied a recipient and Available status. What status should be recorded for this asset?");
  if(!string.IsNullOrWhiteSpace(s.Department)&&!None(s.Department)){var matches=await Resolve(conn,tx,c,"department",s.Department,ct);if(matches.Count!=1)return Ask("department","Choose the intended active department, or say none.",matches);departmentId=matches[0].Id;}
  if(!string.IsNullOrWhiteSpace(s.Location)&&!None(s.Location)){var matches=await Resolve(conn,tx,c,"location",s.Location,ct);if(matches.Count!=1)return Ask("location","Choose the intended active location, or say none.",matches);locationId=matches[0].Id;}
  var fields=new Dictionary<string,JsonElement>(s.CustomFields??new Dictionary<string,JsonElement>());
  var required=definitions.Fields.Where(f=>f.Active&&f.Required&&(f.AssetTypeId is null||f.AssetTypeId==type[0].Id)).ToArray();
  if(s.Need?.StartsWith("field:",StringComparison.Ordinal)==true&&Guid.TryParse(s.Need[6..],out var fieldId)&&required.FirstOrDefault(f=>f.Id==fieldId) is {} field){
   try{fields[field.Id.ToString()]=FieldValue(field,input.Question);}catch(ArgumentException){return Ask(s.Need,$"Provide a valid {field.Kind} value for {field.Name}."+(field.Kind=="choice"?" Choices: "+string.Join(", ",field.Choices):""));}
  }
  s=s with{CustomFields=fields};
  var missing=required.FirstOrDefault(f=>!fields.ContainsKey(f.Id.ToString()));
  if(missing is not null)return(s with{Need="field:"+missing.Id},new(id,"clarify",$"What is {missing.Name}?"+(missing.Kind=="choice"?" Choose: "+string.Join(", ",missing.Choices):" Expected: "+missing.Kind+".")));
  var created=await inventory.SaveWithinAsync(conn,tx,c,Guid.NewGuid(),new(s.AssetTag,s.Name,s.SerialNumber,s.IpAddress,Status:status,Category:type[0].Code,EmployeeId:recipient,DepartmentId:departmentId,LocationId:locationId,AssetTypeId:type[0].Id,CustomFields:fields),false,ct,provisionDocumentation:false);
  return Complete(s,"asset",created.Id,"Inventory asset created. No documentation was created or changed.");

  (State,BrieTicketResult) Complete(State state,string kind,Guid key,string message)=>(state with{Done=true,Need=null,Token=null,RecordKind=kind,RecordId=key},new(id,"completed",message,RecordKind:kind,RecordId:key));
 }
 static bool None(string value)=>value.Equals("none",StringComparison.OrdinalIgnoreCase)||value.Equals("unassigned",StringComparison.OrdinalIgnoreCase);
 static (State,BrieTicketResult) InventoryPreview(Guid id,State s,InventoryAsset asset,BrieChoice recipient){var token=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));return(s with{Token=token,Expires=DateTime.UtcNow.AddMinutes(10),RecordId=asset.Id,RecordKind="asset",Revision=asset.Revision,Need=null,Employee=recipient.Id.ToString()},new(id,"confirmation",$"Reassign asset {asset.AssetTag} ({asset.Id}) from {asset.Assignee} to {recipient.Label} ({recipient.Id})? Its status will be Assigned.",ConfirmationToken:token,RecordKind:"asset",RecordId:asset.Id));}
 static JsonElement FieldValue(AssetFieldDto field,string text){
  object value=field.Kind switch{
   "number" when decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out var number)=>number,
   "boolean" when bool.TryParse(text,out var boolean)=>boolean,
   "date" when DateOnly.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)=>text,
   "choice" when field.Choices.Contains(text,StringComparer.Ordinal)=>text,
   "shortText" when text.Length is >0 and <=200=>text,
   "longText" when text.Length is >0 and <=4000=>text,
   _=>throw new ArgumentException("Invalid field value.")};
  return JsonSerializer.SerializeToElement(value);
 }
 static async Task<IReadOnlyList<BrieChoice>> Resolve(NpgsqlConnection conn,NpgsqlTransaction tx,CompanyContext c,string kind,string value,CancellationToken ct){
  var (table,name,extra)=kind switch{"employee"=>("employees","display_name"," AND active"),"department"=>("inventory_departments","name"," AND active"),"location"=>("locations","name"," AND active"),"asset"=>("inventory_assets","computer",""),_=>throw new ArgumentException("Unknown record type.")};
  var tag=kind=="asset"?" OR lower(asset_tag)=lower(@value)":kind=="employee"?" OR lower(email)=lower(@value)":"";
  await using var q=Q(conn,tx,c,$"SELECT id,{name}"+(kind=="asset"?"||' · '||asset_tag":"")+$" FROM {table} WHERE company_id=@company{extra} AND (id::text=@value OR lower({name})=lower(@value){tag}) ORDER BY id LIMIT 20");q.Parameters.AddWithValue("value",value);
  var rows=new List<BrieChoice>();await using(var r=await q.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))rows.Add(new(r.GetGuid(0),r.GetString(1)));
  if(kind=="asset"&&rows.Count==0){
   // A model may retain a kind prefix ("dock TAG"). Resolve only literal, complete existing tags, never fuzzy names.
   await using var tags=Q(conn,tx,c,"SELECT id,computer,asset_tag FROM inventory_assets WHERE company_id=@company AND asset_tag<>'' AND strpos(lower(@value),lower(asset_tag))>0 ORDER BY id");tags.Parameters.AddWithValue("value",value);
   await using var reader=await tags.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct)){
    var tagValue=reader.GetString(2);if(System.Text.RegularExpressions.Regex.IsMatch(value,@"(?<![\p{L}\p{N}_-])"+System.Text.RegularExpressions.Regex.Escape(tagValue)+@"(?![\p{L}\p{N}_-])",System.Text.RegularExpressions.RegexOptions.IgnoreCase))rows.Add(new(reader.GetGuid(0),reader.GetString(1)+" — "+tagValue));
    if(rows.Count>20)break;
   }
  }
  return rows;
 }
 static async Task ValidateRecord(NpgsqlConnection conn,NpgsqlTransaction tx,CompanyContext c,string? kind,Guid? id,CancellationToken ct){
  if(kind is null||id is null)return;
  if(kind=="employee")await AccountPermissions.RevalidateAsync(conn,tx,c,CompanyCapability.Settings,ct);
  var table=kind switch{"employee"=>"employees","asset"=>"inventory_assets",_=>throw new CompanyResourceMissing()};
  await using var q=Q(conn,tx,c,$"SELECT 1 FROM {table} WHERE company_id=@company AND id=@id");q.Parameters.AddWithValue("id",id.Value);if(await q.ExecuteScalarAsync(ct) is null)throw new CompanyResourceMissing();
 }
}



