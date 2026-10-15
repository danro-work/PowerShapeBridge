using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class VerifiedWorkplaneCopyLeaseTests
{
    private static JsonObject Owner()=>new(){["copyId"]=new string('a',32),["ownershipSha256"]=new string('b',64)};
    private static JsonObject Receipt(DateTimeOffset now)=>new()
    {
        ["state"]="owned_workplane_verified_observed",["recordedJournalState"]="succeeded_verified",
        ["bridgeRequestId"]=new string('c',32),["executionOperationId"]=new string('d',32),
        ["copyFenceReleased"]=true,["noCommandReplay"]=true,["cadMutationAttempted"]=false,
        ["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,
        ["timestampUtc"]=LeaseTime.Format(now)
    };
    [Fact] public void LeaseIsConnectionScopedBoundedByFreshVerificationAndNeverAnOpenProof()
    {
        double tick=0;var now=DateTimeOffset.UtcNow;var copies=new WorkingCopyStore(()=>tick,()=>now);
        var receipt=Receipt(now);var owner=Owner();var issued=copies.IssueVerifiedWorkplane(receipt,owner,now.AddSeconds(75));
        string handle=issued["workingCopyHandle"]!.GetValue<string>();Assert.True(JsonNode.DeepEquals(owner,copies.InspectParameters(handle)));
        Assert.Equal(75,issued["workingCopyLifetimeSeconds"]!.GetValue<int>());Assert.Null(issued["openOperationId"]);Assert.Null(issued["executionAuthorized"]);
        Assert.Throws<ReferenceRejected>(()=>new WorkingCopyStore().InspectParameters(handle));
        tick=75;Assert.Throws<ReferenceRejected>(()=>copies.InspectParameters(handle));Assert.Null(receipt["workingCopyHandle"]);
    }
    [Theory][InlineData("state")][InlineData("fence")][InlineData("return")][InlineData("mutation")][InlineData("owner")][InlineData("expired")][InlineData("extended")]
    public void UnknownOrUnverifiedResultsAndExtendedLifetimesGrantNoCopyLease(string fault)
    {
        var now=DateTimeOffset.UtcNow;var receipt=Receipt(now);var owner=Owner();var expiry=now.AddSeconds(60);
        switch(fault)
        {
            case "state":receipt["state"]="owned_workplane_outcome_unknown";break;
            case "fence":receipt["copyFenceReleased"]=false;break;
            case "return":receipt["noCommandReplay"]=false;break;
            case "mutation":receipt["contextChangeAttempted"]=true;break;
            case "owner":owner["copyId"]="../foreign";break;
            case "expired":expiry=now.AddSeconds(-1);break;
            case "extended":expiry=now.AddSeconds(301);break;
        }
        Assert.Throws<ReferenceRejected>(()=>new WorkingCopyStore(utcClock:()=>now).IssueVerifiedWorkplane(receipt,owner,expiry));
    }
}
