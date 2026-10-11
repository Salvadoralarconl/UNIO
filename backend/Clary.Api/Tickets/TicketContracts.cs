namespace Clary.Api;
public record TicketUser(Guid Id,string DisplayName);
public record TicketAssetInput(Guid AssetId,string Outcome="Reference");
public record TicketAsset(Guid Id,string Name,string AssetTag,string AssetType,string Status,string Outcome,bool Available,string? Brand=null,string? Model=null);
public record TicketDocument(Guid? Id,string Title,bool Available);
public record TicketInput(string Title,string? Description,string Type,DateOnly? DueDate,string AssignmentMode="Users",IReadOnlyList<Guid>? AssigneeIds=null,Guid? RecipientEmployeeId=null,Guid? DepartmentId=null,Guid? LocationId=null,IReadOnlyList<TicketAssetInput>? AssetLinks=null,IReadOnlyList<Guid>? DocumentIds=null,Guid? KitId=null,int? Revision=null,IReadOnlyList<string>? Checklist=null,string? Priority=null);
public record TicketActionInput(string Action,int Revision,IReadOnlyList<Guid>? AssetIds=null,string? Priority=null);
public record TicketCommentInput(string Body);
public record TicketCommentDto(Guid Id,Guid AuthorUserId,string AuthorName,string Body,DateTime CreatedAt);
public record TicketActivityDto(Guid Id,string ActorName,string Action,string? Detail,DateTime CreatedAt);
public record TicketPage<T>(IReadOnlyList<T> Items,string? NextCursor);
public record TicketQuery(string? Search=null,string? Status=null,string? Type=null,Guid? AssigneeId=null,Guid? RecipientId=null,Guid? AssetId=null,Guid? LocationId=null,bool Overdue=false,DateOnly? Today=null,int Limit=50,string? Cursor=null,string? Priority=null);
public record TicketSettings(Guid? OnboardingDocumentId);
public sealed record TicketDto {
 public string Priority{get;init;}="Normal"; public bool IsWorkspaceSetup{get;init;} public Guid Id{get;init;} public Guid? RequestId{get;init;} public string Title{get;init;}=""; public string? Description{get;init;} public string Type{get;init;}="Support"; public string Status{get;init;}="Pending";
 public int Revision{get;init;} public DateOnly? DueDate{get;init;} public DateTime CreatedAt{get;init;} public Guid? CreatorUserId{get;init;} public string? CreatorName{get;init;} public Guid? ApproverUserId{get;init;} public string? ApproverName{get;init;} public DateTime? ApprovedAt{get;init;}
 public Guid? RecipientEmployeeId{get;init;} public string? RecipientName{get;init;} public Guid? DepartmentId{get;init;} public string? DepartmentName{get;init;} public Guid? LocationId{get;init;} public string? LocationName{get;init;} public string AssignmentMode{get;init;}="Users";
 public IReadOnlyList<TicketChecklistItem> Checklist{get;init;}=[]; public IReadOnlyList<TicketUser> Assignees{get;init;}=[]; public IReadOnlyList<TicketAsset> Assets{get;init;}=[]; public IReadOnlyList<TicketDocument> Documents{get;init;}=[]; public IReadOnlyList<EquipmentLine> Lines{get;init;}=[]; public IReadOnlyList<string> AllowedActions{get;init;}=[];
}
