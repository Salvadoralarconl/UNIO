using System.Text.Json;
using System.Text.Json.Serialization;
namespace Clary.Api;
public sealed record EquipmentLine(string Category,string? CustomCategory,int Quantity,Guid? AssetTypeId=null){
 [JsonExtensionData] public Dictionary<string,JsonElement>? AdditionalProperties {get;init;}
}
public sealed record EquipmentAvailability(string Category,string? CustomCategory,int Quantity,int Available,Guid? AssetTypeId=null);
public sealed record EquipmentKitInput(string Name,Guid? DepartmentId,bool Active,IReadOnlyList<EquipmentLine> Lines,int? Revision=null);
public sealed record EquipmentKit(Guid Id,string Name,Guid? DepartmentId,bool Active,int Revision,IReadOnlyList<EquipmentLine> Lines,IReadOnlyList<EquipmentAvailability> Availability,bool CanFulfill,int FulfillableCount);
public sealed record EquipmentRequestInput(string Title,Guid EmployeeId,Guid? DepartmentId,Guid KitId,DateOnly? NeededDate,string? Notes,bool ImmediateAssignment=false,string Purpose="Equipment",Guid? LocationId=null);
public sealed record EquipmentAction(string Action,int Revision,IReadOnlyList<Guid>? AssetIds=null);
public sealed record EquipmentRequest(Guid Id,string Title,Guid EmployeeId,Guid? DepartmentId,Guid KitId,string KitName,DateOnly? NeededDate,string? Notes,string State,Guid TicketId,int Revision,DateTime CreatedAt,IReadOnlyList<EquipmentLine> Lines,IReadOnlyList<Guid> AssetIds,string Purpose="Equipment",Guid? LocationId=null);
public sealed record EquipmentTicket(Guid Id,Guid RequestId,string Title,string State,int Revision,DateTime CreatedAt,DateTime UpdatedAt);
public sealed record EquipmentKitDeletion(int Revision);

