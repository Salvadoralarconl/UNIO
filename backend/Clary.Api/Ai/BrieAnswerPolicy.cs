namespace Clary.Api;

// Presentation constraints run after evidence validation, never instead of it.
public static class BrieAnswerPolicy
{
    public const string MissingDocumentation = "I couldn’t find a documented solution for this issue.";
    public static Answer Prepare(Answer answer) => answer.Status switch
    {
        "insufficient" => answer with { Questions=[], Steps=[], Citations=[], EvidenceNote=MissingDocumentation },
        "supported" => answer with { Steps=answer.Steps.Take(1).ToArray() },
        _ => answer
    };
}
