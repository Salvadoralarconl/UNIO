using Clary.Api.Identity;
namespace Clary.Api;
// Model proposes one intent. Only application services validate and perform an operation.
public sealed class BrieOrchestrator(IBrieIntentRouter router,BrieTickets actions,ConversationStore conversations,ConversationRepository history,TimeProvider? clock=null){
 public async Task<Answer> ReplyAsync(CompanyContext company,BrieMessage input,CancellationToken ct){
  var receivedAt=(clock??TimeProvider.System).GetUtcNow();
  if(input.RequestId==Guid.Empty||string.IsNullOrWhiteSpace(input.Question)||input.Question.Length>4000)throw new ArgumentException("A request ID and message up to 4,000 characters are required.");
  Answer Result(BrieTicketResult task)=>new("action",[],[],[],"Brie",null,task.Message,input.ConversationId){TicketTask=task};
  if(await actions.ReplayAsync(company,input,ct) is {} saved)return Result(saved);
  var pending=input.TaskId is Guid id?await actions.InspectAsync(company,id,ct):null;
  if(pending is not null&&(pending.Done||BrieConversation.IsGreeting(input.Question)||input.ConfirmationToken is not null||input.Question=="Confirm"))return Result(await actions.ReplyAsync(company,input,ct,receivedAt:receivedAt));
  // Explicit nonmutating task control is deterministic, after live task/receipt authorization.
  var pause=System.Text.RegularExpressions.Regex.Match(input.Question,@"^\s*(?:leave that for now|pause (?:that|this)(?: task)?|set that aside)(?:[.!;]\s*|\s*$)(?<issue>.*)$",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
  if(pending is not null&&pause.Success){
   var issueText=pause.Groups["issue"].Value.Trim();
   var answer=issueText.Length==0?new Answer("clarify",["What would you like help with?"],[],[],"Brie",null,ConversationId:input.ConversationId):await conversations.ReplyAsync(company,new(issueText,input.ConversationId),ct);
   return answer with{TicketTask=new(input.TaskId!.Value,"paused","The unfinished task is paused. Pausing did not create or change a business record.")};
  }
  if(pending is null&&BrieConversation.IsGreeting(input.Question))return await conversations.ReplyAsync(company,new(input.Question,input.ConversationId),ct);
  IReadOnlyList<SavedTurn> turns=[];
  if(input.ConversationId is Guid conversation)turns=await history.LoadAsync(company,conversation,ct)??throw new CompanyResourceMissing();
  var issue=turns.LastOrDefault()?.Answer.IssueId;
  // A saved, current-issue prerequisite reply is documentation context, never action consent.
  var active=turns.LastOrDefault(t=>(issue is null||t.Answer.IssueId==issue)&&!BrieConversation.IsGreeting(t.Question));
  if(pending is null&&active?.Answer.Prerequisite is {Confirmed:null}&&active.Answer.Questions.Contains(SourcePrerequisites.QuestionFor(active.Answer.Prerequisite.Condition))&&SourcePrerequisites.IsReply(input.Question))return await conversations.ReplyAsync(company,new(input.Question,input.ConversationId),ct);
  var userFacts=turns.Where(t=>(issue is null||t.Answer.IssueId==issue)&&!BrieConversation.IsGreeting(t.Question)).Select(t=>t.Question).TakeLast(8).ToArray();
  var decision=await router.InterpretAsync(new(input.Question,userFacts,pending?.Operation,pending?.Need,pending?.Token is not null),ct);
  if(pending is null&&OllamaBrieIntentRouter.AdviceQuestion(input.Question))return await conversations.ReplyAsync(company,new(input.Question,input.ConversationId),ct);
  var operation=decision.Intent switch{"create_ticket"=>"create","create_asset"=>"asset","create_employee"=>"employee","assign_asset"=>"assign","cancel_ticket"=>"cancel",_=>null};
  if(operation is not null){
   // Defense in depth: even an alternate provider cannot turn quotation or vague trouble into authorization.
   if(pending is not null||!decision.Authorizes(input.Question))return await RememberAsync(company,input,Clarify(input),turns,ct);
   return Result(await actions.ReplyAsync(company,input,ct,operation,receivedAt));
  }
  if(decision.Intent=="continue_task")return pending is null||OllamaBrieIntentRouter.AmbiguousFieldReply(input.Question)?await RememberAsync(company,input,Clarify(input),turns,ct):Result(await actions.ReplyAsync(company,input,ct,receivedAt:receivedAt));
  if(decision.Intent=="pause_task"&&pending is not null){
   var answer=await conversations.ReplyAsync(company,new(input.Question,input.ConversationId),ct);
   return answer with{TicketTask=new(input.TaskId!.Value,"paused","The unfinished task is paused. Pausing did not create or change a business record.")};
  }
  if(decision.Intent=="unsupported")return new("clarify",[],[],[],"Brie",null,"That operation is not available through Brie. I can use approved documentation, create support tickets, cancel independent support tickets, create inventory assets or employee directory records, and assign assets when your permissions allow. Record search, export, email, account changes and documentation writes are not available.",input.ConversationId);
  if(decision.Intent is "documented_help" or "advice"){
   if(pending is not null)return await RememberAsync(company,input,Clarify(input),turns,ct);
   return await conversations.ReplyAsync(company,new(input.Question,input.ConversationId),ct);
  }
  return await RememberAsync(company,input,Clarify(input),turns,ct);
 }
 async Task<Answer> RememberAsync(CompanyContext company,BrieMessage input,Answer answer,IReadOnlyList<SavedTurn> turns,CancellationToken ct){
  var id=input.ConversationId??Guid.NewGuid();
  var prior=turns.Select(t=>(t.Question,t.Answer)).ToArray();
  var context=BrieConversation.Context(prior,input.Question);
  answer=answer with{ConversationId=id,IssueId=context.IssueId};
  var savedTurns=turns.ToArray();
  // Bind only the continuing legacy issue; retain the original history for CAS.
  if((context.IsContinuation||BrieConversation.CanInitializeLegacyFacts(prior,input.Question))&&turns.LastOrDefault()?.Answer.IssueId is null){
   var start=turns.Count-BrieConversation.ActiveTurnCount(prior);
    if(savedTurns.Skip(start).All(t=>t.Answer.IssueId is null))
     for(var index=start;index<savedTurns.Length;index++)savedTurns[index]=savedTurns[index] with{Answer=savedTurns[index].Answer with{IssueId=context.IssueId}};
  }
  await history.SaveAsync(company,id,savedTurns.Append(new SavedTurn(input.Question,answer)).ToArray(),ct,input.ConversationId is null?null:turns);
  return answer;
 }
 static Answer Clarify(BrieMessage input)=>new("clarify",[input.TaskId is null?"Do you want documented guidance, or a ticket or inventory action? Please tell me which task to handle first.":"Should I continue the unfinished task, or leave it for now and help with a different issue?"],[],[],"Brie",null,ConversationId:input.ConversationId);
}




