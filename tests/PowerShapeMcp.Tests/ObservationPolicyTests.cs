using System.Text.Json;
using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class ObservationPolicyTests
{
    public static IEnumerable<object[]> GoldenCases()
    {
        using var data=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","observations.json")));
        foreach(var value in data.RootElement.EnumerateArray())yield return [value.GetProperty("name").GetString()!,value.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void ObservationAndIdentityDigestsMatchLegacyPython(string name,string fixture)
    {
        Assert.NotEmpty(name);
        using var value=JsonDocument.Parse(fixture);
        var expected=value.RootElement;
        var observed=ObservationPolicy.Read(JsonNode.Parse(expected.GetProperty("snapshot").GetRawText())!.AsObject());
        Assert.Equal(expected.GetProperty("observedPropertiesSha256").GetString(),observed.Digest);
        Assert.Equal(expected.GetProperty("identitySha256").GetString(),ObservationPolicy.Digest(observed.Identity));
        Assert.Equal(expected.GetProperty("identity").GetProperty("modelPath").GetString(),observed.Identity["modelPath"]!.GetValue<string>());
    }
    [Theory]
    [InlineData("state","\"failed\"","fresh_inspection_failed")]
    [InlineData("processId","true","document_identity_unavailable")]
    [InlineData("windowsSessionId","-1","document_identity_unavailable")]
    [InlineData("processStartedUtc","\"\"","process_identity_unavailable")]
    [InlineData("entityCount","true","incomplete_inventory")]
    [InlineData("contextBefore","null","observed_context_invalid")]
    public void InvalidObservationsDoNotProduceDigest(string field,string json,string code)
    {
        using var data=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","observations.json")));
        var source=JsonNode.Parse(data.RootElement[0].GetProperty("snapshot").GetRawText())!.AsObject();
        source[field]=JsonNode.Parse(json);
        Assert.Equal(code,Assert.Throws<ReferenceRejected>(()=>ObservationPolicy.Read(source)).Code);
    }
}
