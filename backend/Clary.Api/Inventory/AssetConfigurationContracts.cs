using System.Text.Json.Serialization;
namespace Clary.Api;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssetTypeInput(string Name,string Icon,bool Active=true,int SortOrder=0,int? Revision=null,IReadOnlyList<string>? Categories=null);
public sealed record AssetTypeDto(Guid Id,string Code,string Name,string Icon,bool Active,int SortOrder,int Revision,IReadOnlyList<string>? Categories=null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssetFieldInput(Guid? AssetTypeId,string Name,string Kind,string? HelpText=null,bool Required=false,bool Active=true,int SortOrder=0,IReadOnlyList<string>? Choices=null,int? Revision=null);
public sealed record AssetFieldDto(Guid Id,Guid? AssetTypeId,string Name,string Kind,string? HelpText,bool Required,bool Active,int SortOrder,IReadOnlyList<string> Choices,int Revision);
public sealed record AssetConfigurationDto(IReadOnlyList<AssetTypeDto> Types,IReadOnlyList<AssetFieldDto> Fields,IReadOnlyList<string> Icons);
