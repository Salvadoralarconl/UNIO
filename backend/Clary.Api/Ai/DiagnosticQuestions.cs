namespace Clary.Api;
public static class DiagnosticQuestions
{
    public static IReadOnlyList<string> All {get;}=Array.AsReadOnly(new[]{
        "What device and operating system are you using?",
        "What is the exact error message?",
        "When did the issue start?",
        "Does this affect one device or multiple devices?",
        "Which VPN profile is active?",
        "Which internal service or hostname is affected?",
        "What printer model and connection type are you using?",
        "What happens when you try to print, and which lights are on or flashing?",
        "How is the printer connected: USB, Ethernet, or another connection?",
        "Can you describe what you see on the screen or printer when the problem happens?",
        "What label dimensions are you using?",
        "Which troubleshooting steps have you already tried, and what happened?",
        "What did Device Manager show: an Epson device, an unknown USB device, a warning symbol, or no device appearing when you connected the printer?"
        ,BrieConversation.SymptomQuestion,BrieConversation.ClearerProcedureQuestion,
        SourceObservationQuestions.ApplicationScope
    });
}
