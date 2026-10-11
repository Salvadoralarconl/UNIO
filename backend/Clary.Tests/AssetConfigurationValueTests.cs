using Clary.Api;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
public class AssetConfigurationValueTests {
 static JsonElement Value(string json)=>JsonDocument.Parse(json).RootElement.Clone();
 static Dictionary<string,JsonElement> Values(Guid id,string json)=>new(){{id.ToString(),Value(json)}};
 [Fact] public async Task NewRequiredFieldsForeignDefinitionsAndAuditRollback()=>await Run(async f=>{
  var c=InventoryAuditTests.Context;var cfg=new AssetConfigurationRepository(f.Database);var inv=new InventoryRepository(f.Database);var existing=await inv.CreateAsync(c,new("LEGACY","Asset"),default);var required=await cfg.SaveFieldAsync(c,null,new(null,"New required","boolean",Required:true),default);
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,existing.Id,new("LEGACY","Asset",Revision:1),default));Assert.Empty((await inv.GetAsync(c,existing.Id,default))!.CustomFields!);
  var foreign=c with{CompanyId=Guid.NewGuid(),UserId=Guid.NewGuid()};await f.Sql($"INSERT INTO companies(id,name) VALUES('{foreign.CompanyId}','Foreign')");await FixtureCompany.AddActorAsync(f.Database,foreign);var foreignType=await cfg.SaveTypeAsync(foreign,null,new("Foreign type","Box"),default);var foreignField=await cfg.SaveFieldAsync(foreign,null,new(null,"Foreign field","boolean"),default);
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,existing.Id,new("LEGACY","Asset",Revision:1,AssetTypeId:foreignType.Id),default));await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,existing.Id,new("LEGACY","Asset",Revision:1,CustomFields:Values(foreignField.Id,"true")),default));
  await f.Sql("CREATE FUNCTION reject_asset_value_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.resource_type='asset' THEN RAISE EXCEPTION 'forced'; END IF; RETURN NEW; END $$; CREATE TRIGGER reject_asset_value_audit BEFORE INSERT ON inventory_audit FOR EACH ROW EXECUTE FUNCTION reject_asset_value_audit();");
  await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>inv.UpdateAsync(c,existing.Id,new("LEGACY","Asset",Revision:1,CustomFields:Values(required.Id,"false")),default));Assert.Empty((await inv.GetAsync(c,existing.Id,default))!.CustomFields!);Assert.Equal(1,(await inv.GetAsync(c,existing.Id,default))!.Revision);
 });
 [Fact] public async Task AssetValuesValidateAllSixKindsAndRequiredRules()=>await Run(async f=>{
  var c=InventoryAuditTests.Context;var cfg=new AssetConfigurationRepository(f.Database);var inv=new InventoryRepository(f.Database);var values=new Dictionary<string,JsonElement>();
  var cases=new[]{("shortText","\"Text\"","123"),("longText","\"Long text\"","true"),("number","12.5","1e1000"),("date","\"2024-02-29\"","\"2025-02-29\""),("boolean","false","\"false\""),("choice","\"One\"","\"Missing\"")};var fields=new List<AssetFieldDto>();
  foreach(var (kind,valid,invalid) in cases){var field=await cfg.SaveFieldAsync(c,null,new(null,kind,kind,Required:true,Choices:kind=="choice"?["One","Two"]:null),default);fields.Add(field);values[field.Id.ToString()]=Value(valid);}
  var asset=await inv.CreateAsync(c,new("SIX","Asset",CustomFields:values),default);Assert.False(asset.CustomFields![fields[4].Id.ToString()].GetBoolean());
  for(var i=0;i<cases.Length;i++){var bad=new Dictionary<string,JsonElement>(values);bad[fields[i].Id.ToString()]=Value(cases[i].Item3);await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,asset.Id,new("SIX","Asset",Revision:1,CustomFields:bad),default));}
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,asset.Id,new("SIX","Asset",Revision:1,CustomFields:Values(fields[0].Id,"null")),default));
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,asset.Id,new("SIX","Asset",Revision:1,CustomFields:Values(Guid.NewGuid(),"true")),default));
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,asset.Id,new("SIX","Asset",Revision:1,CustomFields:Values(fields[0].Id,JsonSerializer.Serialize(new string('x',201)))),default));
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,asset.Id,new("SIX","Asset",Revision:1,CustomFields:Values(fields[1].Id,JsonSerializer.Serialize(new string('x',4001)))),default));
  Assert.Equal(1,(await inv.GetAsync(c,asset.Id,default))!.Revision);
  // Total JSON size is enforced after merging retained values, even on an unrelated save.
  await f.Sql($"UPDATE inventory_assets SET custom_fields=jsonb_set(custom_fields,ARRAY['{fields[1].Id}'],to_jsonb(repeat(chr(34),32000))) WHERE id='{asset.Id}'");
  var extra=await cfg.SaveFieldAsync(c,null,new(null,"Extra","longText"),default);
  var sizeError=await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,asset.Id,new("SIX","Asset",Revision:1,CustomFields:Values(extra.Id,JsonSerializer.Serialize(new string('x',4000)))),default));Assert.Contains("64KB",sizeError.Message);
 });
 [Fact] public async Task ArchivedFieldsAndTypeSwitchPreserveHistoricalValues()=>await Run(async f=>{
  var c=InventoryAuditTests.Context;var cfg=new AssetConfigurationRepository(f.Database);var inv=new InventoryRepository(f.Database);var type=await cfg.SaveTypeAsync(c,null,new("Tools","Wrench"),default);var detail=await cfg.SaveFieldAsync(c,null,new(type.Id,"Tool detail","shortText",Required:true),default);var choice=await cfg.SaveFieldAsync(c,null,new(null,"Choice","choice",Choices:["Old","New"]),default);
  var values=Values(detail.Id,"\"history\"");values[choice.Id.ToString()]=Value("\"Old\"");var asset=await inv.CreateAsync(c,new("HISTORY","Asset",AssetTypeId:type.Id,CustomFields:values),default);
  await Assert.ThrowsAsync<InventoryConflict>(()=>cfg.SaveFieldAsync(c,detail.Id,new(null,"Tool detail","shortText",Revision:1),default));
  await cfg.SaveFieldAsync(c,choice.Id,new(null,"Choice","choice",Choices:["New"],Revision:1),default);
  var switched=await inv.UpdateAsync(c,asset.Id,new("HISTORY","Asset",Revision:1,Category:"Printer",CustomFields:values),default);Assert.Equal("history",switched.CustomFields![detail.Id.ToString()].GetString());Assert.Equal("Old",switched.CustomFields[choice.Id.ToString()].GetString());
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,asset.Id,new("HISTORY","Asset",Revision:2,Category:"Printer",CustomFields:Values(detail.Id,"\"changed\"")),default));
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.CreateAsync(c,new("BADCHOICE","Asset",CustomFields:Values(choice.Id,"\"Old\"")),default));
  await cfg.SaveFieldAsync(c,detail.Id,new(type.Id,"Tool detail","shortText",Required:true,Active:false,Revision:1),default);
  switched=await inv.UpdateAsync(c,asset.Id,new("HISTORY","Asset",Revision:2,Category:"Printer",CustomFields:Values(choice.Id,"null")),default);Assert.False(switched.CustomFields!.ContainsKey(choice.Id.ToString()));Assert.True(switched.CustomFields.ContainsKey(detail.Id.ToString()));
  var old=await inv.CreateAsync(c,new("ARCHIVED","Asset",AssetTypeId:type.Id),default);await cfg.SaveTypeAsync(c,type.Id,new("Renamed tools","Wrench",Active:false,Revision:1),default);
  var updated=await inv.UpdateAsync(c,old.Id,new("ARCHIVED","Asset",Revision:1,Category:type.Code),default);Assert.Equal("Renamed tools",updated.AssetTypeName);Assert.Equal(type.Code,updated.Category);
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.CreateAsync(c,new("NOARCHIVE","Asset",AssetTypeId:type.Id),default));
  await Assert.ThrowsAsync<ArgumentException>(()=>inv.UpdateAsync(c,asset.Id,new("HISTORY","Asset",Revision:3,AssetTypeId:type.Id),default));
 });
 [Fact] public async Task JsonbSizeBoundaryReturnsValidationAndPreservesAssetAndAudit()=>await Run(async f=>{
  var c=InventoryAuditTests.Context;var cfg=new AssetConfigurationRepository(f.Database);var inv=new InventoryRepository(f.Database);
  var asset=await inv.CreateAsync(c,new("BOUNDARY","Asset"),default);var values=new Dictionary<string,JsonElement>();
  for(var i=0;i<17;i++){var field=await cfg.SaveFieldAsync(c,null,new(null,"Boundary "+i,"longText"),default);values[field.Id.ToString()]=Value(JsonSerializer.Serialize(new string('x',3812)));}
  Assert.Equal(65519,System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(values)));
  var snapshot=await f.Scalar("SELECT to_jsonb(a)::text FROM inventory_assets a");var audits=await f.Scalar("SELECT count(*) FROM inventory_audit WHERE resource_type='asset'");
  await f.Login();var response=await f.Send(HttpMethod.Put,"/api/inventory/assets/"+asset.Id,new{assetTag="BOUNDARY",computer="Changed",revision=1,customFields=values});
  Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Contains("64KB",await response.Content.ReadAsStringAsync());
  Assert.Equal(snapshot,await f.Scalar("SELECT to_jsonb(a)::text FROM inventory_assets a"));Assert.Equal(audits,await f.Scalar("SELECT count(*) FROM inventory_audit WHERE resource_type='asset'"));
  foreach(var key in values.Keys.ToArray())values[key]=Value(JsonSerializer.Serialize(new string('x',3811)));
  var valid=await f.Send(HttpMethod.Put,"/api/inventory/assets/"+asset.Id,new{assetTag="BOUNDARY",computer="Changed",revision=1,customFields=values});Assert.Equal(HttpStatusCode.OK,valid.StatusCode);
  Assert.Equal(65535L,Convert.ToInt64(await f.Scalar("SELECT octet_length(custom_fields::text) FROM inventory_assets")));Assert.Equal(2,(await inv.GetAsync(c,asset.Id,default))!.Revision);
 });
 static Task Run(Func<IdentityFixture,Task> test)=>IdentityFixture.Run(test,b=>b.Services.AddSingleton<AssetConfigurationRepository>());
 [Fact] public async Task RequiredConfiguredValuesCannotBeOmitted()=>await Run(async f=>{
  await new AssetConfigurationRepository(f.Database).SaveFieldAsync(InventoryAuditTests.Context,null,new(null,"Required","shortText",Required:true),default);
  await f.Login();Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Post,"/api/inventory/assets",new{assetTag="REQUIRED",computer="Asset"})).StatusCode);
 });
 [Fact] public async Task CustomTypeAndValuesAreReturned()=>await Run(async f=>{
  var c=InventoryAuditTests.Context;var config=new AssetConfigurationRepository(f.Database);var type=await config.SaveTypeAsync(c,null,new("Tools","Wrench"),default);var field=await config.SaveFieldAsync(c,null,new(type.Id,"Detail","shortText"),default);await f.Login();
  var response=await f.Send(HttpMethod.Post,"/api/inventory/assets",new{assetTag="CUSTOM",computer="Tool",category=type.Code,assetTypeId=type.Id,customFields=new Dictionary<string,object>{{field.Id.ToString(),"Value"}}});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  var body=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(type.Id,body.GetProperty("assetTypeId").GetGuid());Assert.Equal("Tools",body.GetProperty("assetTypeName").GetString());Assert.Equal("Value",body.GetProperty("customFields").GetProperty(field.Id.ToString()).GetString());Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM inventory_documentation"));
  Assert.Equal(HttpStatusCode.BadRequest,(await f.Send(HttpMethod.Post,"/api/inventory/assets",new{assetTag="PROTECTED",computer="Asset",companyId=Guid.NewGuid()})).StatusCode);
 });
}
