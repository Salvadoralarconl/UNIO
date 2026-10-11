using Clary.Api;
using System.Text.Json;

// Synthetic interpretation diagnostics only. No documents, tools or database writes.
using var client=new HttpClient(new Trace()){BaseAddress=new Uri("http://100.79.42.5:11434/"),Timeout=TimeSpan.FromSeconds(90)};
var verifier=new ModelSupportVerifier(client,"Ollama","qwen3:8b");
var controls=args.Contains("--controls");
var interpretationReports=controls?new[]{
 "Nothing on the computer responds, including the desktop and all programs.",
 "The editor freezes. Other programs are not affected and remain usable.",
 "The editor freezes. Perhaps the other programs work; I have not checked them.",
 "Only the editor froze and other programs worked earlier. Correction: now every application and the desktop are unresponsive.",
 "The guide says 'Only the editor freezes; other applications work'. My editor freezes but I have not checked anything else."
}:new[]{
 "My computer keeps hanging when I work.\nIt is only the browser window. My other programs are responsive.",
 "The laptop is dragging and the printer has stopped too. Where do we start?\nStart with the laptop. Only the browser is freezing; the other apps work.",
 "The browser is freezing. I have not tested the other applications."
};
foreach(var reports in interpretationReports)Console.WriteLine(JsonSerializer.Serialize(new{kind="interpretation",reports,result=await verifier.InterpretHelpAsync(reports,default)}));
const string condition="both input devices work directly but not through the dock";
var observationReports=controls?new[]{
 "Keyboard and mouse fail through the dock and also fail directly on the laptop.",
 "Both worked directly earlier. Correction: the keyboard now fails directly, and the mouse works directly. Both fail through the dock.",
 "The guide says 'both input devices work directly but not through the dock'. I have only checked the dock connection; both fail there."
}:new[]{
 "Keyboard and mouse both fail through the dock; both work when plugged straight into the laptop. What is the next check?",
 "Keyboard and mouse both fail through the dock. I have not tested them plugged directly into the laptop."
};
foreach(var reports in observationReports)Console.WriteLine(JsonSerializer.Serialize(new{kind="observation",condition,reports,result=await verifier.ObservePrerequisiteAsync(condition,reports,default)}));

sealed class Trace:DelegatingHandler{
 public Trace():base(new HttpClientHandler()){}
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
  var body=await request.Content!.ReadAsStringAsync(ct);using var input=JsonDocument.Parse(body);
  var response=await base.SendAsync(request,ct);response.EnsureSuccessStatusCode();using var output=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
  Console.WriteLine(JsonSerializer.Serialize(new{kind="model-output",input=input.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString(),output=output.RootElement.GetProperty("message").GetProperty("content").GetString(),doneReason=output.RootElement.GetProperty("done_reason").GetString()}));
  return response;
 }
}
