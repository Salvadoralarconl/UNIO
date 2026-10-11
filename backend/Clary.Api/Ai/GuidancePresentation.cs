namespace Clary.Api;
public static class GuidancePresentation
{
    public static Answer Prepare(Answer answer,IReadOnlyList<Passage> passages)
    {
        var distinct=answer.Steps.GroupBy(s=>s.Text,StringComparer.Ordinal)
            .Select(g=>new AdviceStep(g.Key,g.SelectMany(s=>s.Citations).Distinct().ToArray())).ToArray();
        var complete=distinct.Where(step=>!distinct.Any(other=>other.Text!=step.Text &&
            other.Text.Contains(step.Text,StringComparison.Ordinal) &&
            other.Citations.Any(c=>step.Citations.Any(s=>s.PassageId==c.PassageId))));
        var ordered=complete.OrderBy(step=>Position(step,passages)).ToArray();
        return answer with{Steps=ordered};
    }
    private static (int Passage,int Offset) Position(AdviceStep step,IReadOnlyList<Passage> passages)
    {
        for(var i=0;i<passages.Count;i++)
            if(step.Citations.Any(c=>c.PassageId==passages[i].Id)) {
                var offset=passages[i].Text.IndexOf(step.Text,StringComparison.Ordinal);
                if(offset>=0)return (i,offset);
            }
        return (int.MaxValue,int.MaxValue);
    }
}
