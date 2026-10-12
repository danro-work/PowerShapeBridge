using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedExecutionProofTests
{
    [Fact]
    public void OwnedOpenConsumesDocumentChoiceAndUsesMinimumExpiry()
    {
        var fixture=new SafetyFixture();var documents=new DocumentHandleStore(()=>fixture.Time,()=>fixture.Utc);
        var listing=new JsonObject{["state"]="documents_listed_verified",["bridgeRequestId"]=new string('a',32),["timestampUtc"]=LeaseTime.Format(fixture.Utc),["documents"]=new JsonArray(new JsonObject{["modelId"]=9,["windowNumber"]=4,["activationSupported"]=true})};
        var issued=documents.Issue(listing,new string('b',64));var handle=issued["documents"]![0]!["documentHandle"]!.GetValue<string>();
        var args=new JsonObject{["workingCopyHandle"]=fixture.Copy,["candidateDocumentHandle"]=fixture.Document,["activeDocumentHandle"]=handle};
        var parameters=OwnedExecutionProof.OpenParameters(args,fixture.Candidates,fixture.Copies,documents);
        Assert.Equal("2026-10-12T10:05:00+00:00",parameters["expiresUtc"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>OwnedExecutionProof.OpenParameters(args,fixture.Candidates,fixture.Copies,documents));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
    }
    [Theory]
    [InlineData("bool_return")]
    [InlineData("zero_return")]
    [InlineData("unfenced")]
    [InlineData("verified_state")]
    public void ReturnedExecutionDoesNotBecomeVerified(string fault)
    {
        var fixture=new Fixture();var id=new string('a',32);var parameters=new JsonObject{["ownedOperationId"]=new string('b',32),["ownedOperationSha256"]=new string('c',64)};
        var plan=parameters.DeepClone().AsObject();plan["operationId"]=id;plan["operation"]="translate_owned_solid";plan["executionEnabled"]=true;
        var sha=ArtifactProof.Freeze(fixture.Root,"ownedexecutions/"+id+".json",plan);
        var result=new JsonObject{["state"]="owned_translation_executed_unverified",["operationId"]=id,["planSha256"]=sha,["bridgeRequestId"]=id,["geometryMutationAttempted"]=true,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,["returnCode"]=1,["copyFenced"]=true,["recordedJournalState"]="executed_unverified",["plan"]=plan};
        OwnedExecutionProof.TranslationReceipt(fixture.Root,result,parameters);
        switch(fault){case "bool_return":result["returnCode"]=true;break;case "zero_return":result["returnCode"]=0;break;case "unfenced":result["copyFenced"]=false;break;case "verified_state":result["recordedJournalState"]="succeeded_verified";break;}
        Assert.Throws<ReferenceRejected>(()=>OwnedExecutionProof.TranslationReceipt(fixture.Root,result,parameters));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
    }
}
