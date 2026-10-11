namespace Clary.Api;

public static class CitationValidator
{
    public static bool IsValid(Answer answer, IReadOnlyList<Passage> retrieved, bool allowParaphrase=false)
    {
        if (answer.Status is not ("supported" or "insufficient" or "conflicting" or "clarify")) return false;
        if (answer.Steps is null || answer.Citations is null || answer.Questions is null) return false;
        if (answer.Questions.Count>3 || answer.Questions.Any(q=>!DiagnosticQuestions.All.Contains(q,StringComparer.Ordinal))) return false;
        if (answer.Steps.Count>8) return false;
        if (answer.Status == "conflicting" && answer.Citations.Select(c=>(c.PassageId,c.Quote)).Distinct().Count()<2) return false;
        if (answer.Status != "supported" && answer.Steps.Count != 0) return false;
        if (answer.Status == "supported" && answer.Steps.Count == 0) return false;
        if (answer.Steps.Any(s => string.IsNullOrWhiteSpace(s.Text) || s.Citations is null || s.Citations.Count == 0)) return false;
        if(answer.Steps.SelectMany(s=>s.Citations).Any(c=>c is null||!CitationSupport.IsSubstantive(c.Quote)))return false;
        if (!allowParaphrase && answer.Steps.Any(s => !s.Citations.Any(c => string.Equals(s.Text,c.Quote,StringComparison.Ordinal)))) return false;
        var sources = retrieved.ToDictionary(p => p.Id);
        return answer.Citations.Concat(answer.Steps.SelectMany(s => s.Citations)).All(c =>
            c is not null && !string.IsNullOrWhiteSpace(c.Quote) &&
            sources.TryGetValue(c.PassageId, out var passage) && passage.Text.Contains(c.Quote, StringComparison.Ordinal));
    }
}
