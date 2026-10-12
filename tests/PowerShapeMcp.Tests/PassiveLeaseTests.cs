using System.Text.Json;
using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class PassiveLeaseTests
{
    private static DateTimeOffset Utc=>new(2026,10,12,10,0,0,TimeSpan.Zero);
    [Theory]
    [InlineData("relative.psmodel")]
    [InlineData("C:/part.step")]
    [InlineData("C:/a/../part.psmodel")]
    [InlineData("C:/a./part.psmodel")]
    [InlineData("C:/a /part.psmodel")]
    [InlineData("C:/part.psmodel:stream.psmodel")]
    [InlineData("C:/a?b.psmodel")]
    public void SavedSourceAmbiguitiesAreRejected(string path)=>Assert.Throws<ReferenceRejected>(()=>WorkingCopyStore.ValidateSource(new(){["sourcePath"]=path,["sourceSha256"]=new string('a',64)}));
    [Fact]
    public void CopyLeaseExpiresWithoutRenewingOrAcceptingOtherConnection()
    {
        double time=100;var store=new WorkingCopyStore(()=>time,()=>Utc);
        var result=new JsonObject{["state"]="working_copy_prepared_disk",["copyId"]=new string('a',32),["bridgeRequestId"]=new string('a',32),["ownershipSha256"]=new string('b',64),["timestampUtc"]="2026-10-12T10:00:00Z"};
        var issued=store.Issue(result);var handle=issued["workingCopyHandle"]!.GetValue<string>();
        Assert.Null(result["workingCopyHandle"]);Assert.Equal(new string('a',32),store.InspectParameters(handle)["copyId"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>new WorkingCopyStore(()=>time,()=>Utc).InspectParameters(handle));
        time=399;store.InspectParameters(handle);time=400;Assert.Throws<ReferenceRejected>(()=>store.InspectParameters(handle));
    }
    [Fact]
    public void SetLeaseExpiresAndObservedSwitchInvalidatesIt()
    {
        double time=100;var store=new CandidateStore(()=>time,()=>Utc);
        using var fixtures=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","observations.json")));
        var snapshot=JsonNode.Parse(fixtures.RootElement[0].GetProperty("snapshot").GetRawText())!.AsObject();snapshot["timestampUtc"]="2026-10-12T10:00:00Z";
        store.Issue(snapshot,new string('b',64));
        var native=new JsonObject{["state"]="entity_set_prepared_observed",["entitySetId"]=new string('c',32),["entitySetSha256"]=new string('d',64),["expiresUtc"]="2026-10-12T10:05:00Z"};
        var issued=store.IssueSet(native);var handle=issued["entitySetHandle"]!.GetValue<string>();
        Assert.Null(native["entitySetHandle"]);Assert.Equal(new string('d',64),store.SetParameters(handle)["entitySetSha256"]!.GetValue<string>());
        time=400;Assert.Throws<ReferenceRejected>(()=>store.SetParameters(handle));
        time=100;var newHandle=store.IssueSet(native)["entitySetHandle"]!.GetValue<string>();snapshot["model"]!["id"]=10;store.Observe(snapshot);
        Assert.Throws<ReferenceRejected>(()=>store.SetParameters(newHandle));
    }
}
