using Clary.Api;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;
public class BrieParserTests {
 sealed class Handler:HttpMessageHandler {public JsonElement Payload;public string Response="{\"title\":\"Issue\",\"assignee\":null,\"description\":\"Invented description\"}";protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Payload=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{done=true,done_reason="stop",message=new{content=Response}}),Encoding.UTF8,"application/json")};}}
 [Fact] public async Task UsesRelevantSchemaDropsInventedFieldsAndKeepsExplicitUnassigned(){var handler=new Handler();var parser=new OllamaBrieTicketParser(new HttpClient(handler){BaseAddress=new("http://fixture/")},"fixture");var p=await parser.ParseAsync("Create ticket Issue unassigned",default);Assert.Equal("Issue",p.Title);Assert.Equal("unassigned",p.Assignee);Assert.Null(p.Description);var fields=handler.Payload.GetProperty("format").GetProperty("properties");Assert.Equal(5,fields.EnumerateObject().Count());Assert.False(fields.TryGetProperty("assetTag",out _));}
 [Fact] public async Task ExactCancelUuidIsAnExactTargetWithoutModelInterpretation(){var handler=new Handler();var parser=new OllamaBrieTicketParser(new HttpClient(handler){BaseAddress=new("http://fixture/")},"fixture");const string id="94abebd9-deca-48b1-9def-bc832de92b8b";var patch=await parser.ParseAsync("Cancel ticket "+id,default);Assert.Equal(id,patch.Target);Assert.Equal(JsonValueKind.Undefined,handler.Payload.ValueKind);} [Theory]
 [InlineData("Create asset Demo laptop",null)]
 [InlineData("Create asset Demo laptop tag Demo laptop","Demo laptop")]
 [InlineData("Create asset Demo laptop asset tag: Demo laptop","Demo laptop")]
 public async Task DuplicateNameIsNotAnImplicitAssetTag(string message,string? expected){var handler=new Handler{Response=JsonSerializer.Serialize(new BrieTicketPatch(Name:"Demo laptop",AssetTag:"Demo laptop"),new JsonSerializerOptions(JsonSerializerDefaults.Web))};var parser=new OllamaBrieTicketParser(new HttpClient(handler){BaseAddress=new("http://fixture/")},"fixture");var result=await parser.ParseAsync(message,default);Assert.Equal("Demo laptop",result.Name);Assert.Equal(expected,result.AssetTag);} [Fact] public async Task SemanticAssetOperationStillRequiresSeparateExplicitTag(){var handler=new Handler{Response="{\"name\":\"Synthetic laptop\",\"assetTag\":\"Synthetic laptop\"}"};using var client=new HttpClient(handler){BaseAddress=new Uri("http://fixture.invalid/")};var patch=await new OllamaBrieTicketParser(client,"fixture").ParseAsync("Please put Synthetic laptop into inventory","asset",default);Assert.Equal("Synthetic laptop",patch.Name);Assert.Null(patch.AssetTag);}}


