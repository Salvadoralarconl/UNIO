using Clary.Api;
using Xunit;

public class CitationSchemaTests
{
    [Fact] public void CitationChoicesPreserveSourceCaseAndBelongToRetrievedPassage()
    {
        var id=Guid.NewGuid();
        var source=new Passage(id,Guid.NewGuid(),"Guide","1","section","If connected, confirm your assigned VPN profile. Do not disable security software.",null);
        var schema=GroundedAnswerContract.SchemaFor([source]);
        var properties=schema.GetProperty("$defs").GetProperty("citation").GetProperty("anyOf")[0].GetProperty("properties");
        Assert.Equal(id.ToString(),properties.GetProperty("passageId").GetProperty("enum")[0].GetString());
        var quotes=properties.GetProperty("quote").GetProperty("enum").EnumerateArray().Select(q=>q.GetString()!).ToArray();
        Assert.Contains("If connected, confirm your assigned VPN profile.",quotes);
        Assert.DoesNotContain("Confirm your assigned VPN profile.",quotes);
        Assert.All(quotes,quote=>Assert.Contains(quote,source.Text,StringComparison.Ordinal));
    }
}
