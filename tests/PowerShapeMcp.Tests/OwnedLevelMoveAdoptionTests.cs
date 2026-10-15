using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedLevelMoveAdoptionTests
{
    private sealed class Evidence(string fault="control")
    {
        internal readonly OwnedLevelMoveStoreTests.Data Data=new();
        internal readonly string Id=Guid.NewGuid().ToString("N");
        internal string Sha="";
        internal OwnedLevelMoveStore Fresh()=>new(Data.Root,Data.Base.Candidates,Data.Base.Bindings,()=>Data.Base.Time,()=>Data.Base.Utc);
        internal Evidence Build()
        {
            var prepared=Data.Native();var packet=prepared["ownedLevelMove"]!.AsObject();
            var plan=new JsonObject{["schemaVersion"]=1,["operationId"]=Id,["operation"]="move_owned_solid_to_level",["before"]=Data.Base.Source.DeepClone(),["nativeId"]=5,["targetLevelNumber"]=5,["bindingId"]=packet["arguments"]!["bindingId"]!.DeepClone()};
            foreach(var key in new[]{"copyId","ownershipSha256","workingSha256","sourceLevelNumber","windowContextSha256"})plan[key]=packet[key]!.DeepClone();
            foreach(var key in new[]{"ownedLevelMoveId","ownedLevelMoveSha256"})plan[key]=prepared[key]!.DeepClone();
            var planSha=ArtifactProof.Freeze(Data.Root,"levelmoveexecutions/"+Id+".json",plan);
            var claim=new JsonObject{["operationId"]=Id,["planSha256"]=planSha,["ownedLevelMoveId"]=plan["ownedLevelMoveId"]!.DeepClone(),["copyId"]=plan["copyId"]!.DeepClone(),["automaticReplayForbidden"]=true};
            if(fault=="wrong_claim_intent")claim["ownedLevelMoveId"]=Guid.NewGuid().ToString("N");
            var claimPath="ownedlevelmoves/"+plan["ownedLevelMoveId"]!.GetValue<string>()+".execution-once.json";
            var claimSha=ArtifactProof.Freeze(Data.Root,claimPath,claim);
            string? previous=null;int sequence=0;
            foreach(var state in new[]{"prepared","execution_claimed","command_started","command_returned","executed_unverified"})
            {
                var details=state=="command_returned"?new JsonObject{["returnCode"]=fault=="journal_false"?0:1,["nativeReturnType"]="System.Boolean"}:new JsonObject();
                var row=new JsonObject{["schemaVersion"]=1,["operationId"]=Id,["planSha256"]=planSha,["sequence"]=sequence,["previousSha256"]=previous,["state"]=state,["timestampUtc"]=LeaseTime.Format(Data.Base.Utc),["details"]=details};
                previous=ArtifactProof.Freeze(Data.Root,$"operations/{Id}/{sequence++:D6}.json",row);
            }
            var receipt=new JsonObject{["operationId"]=Id,["plan"]=plan,["planSha256"]=planSha,["claimSha256"]=claimSha,["state"]="owned_level_move_executed_unverified",["cadMutationAttempted"]=true,["contextChangeAttempted"]=true,["cadSaveAttempted"]=false,["returnCode"]=fault=="receipt_false"?0:1,["nativeReturnType"]=fault=="integer_return"?"System.Int32":"System.Boolean"};
            if(fault=="wrong_request")receipt["operationId"]=Guid.NewGuid().ToString("N");
            if(fault=="pre_command_state")receipt["state"]="owned_level_move_rejected_before_change";
            Sha=ArtifactProof.Freeze(Data.Root,"responses/"+Id+".json",receipt);
            if(fault=="working_changed")File.AppendAllText(Data.Base.Working,"changed");
            if(fault=="checkpoint_writable")File.SetAttributes(Data.Base.Checkpoint,FileAttributes.Normal);
            if(fault=="writer_present")File.WriteAllText(Path.Combine(Data.Root,"operations",Id,"writer.lock"),"locked");
            if(fault=="claim_missing"){var path=Path.Combine(Data.Root,claimPath);File.SetAttributes(path,FileAttributes.Normal);File.Delete(path);}
            if(fault=="receipt_sha")Sha=new string('0',64);
            return this;
        }
        internal Dictionary<string,string> Inventory()=>Directory.GetFiles(Data.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>FileArtifacts.Sha256(File.ReadAllBytes(p)));
    }

    [Fact] public void ReconnectCreatesVerificationOnlyHandleWithoutRevivingExpiredIntentOrWritingArtifacts()
    {
        var fixture=new Evidence().Build();fixture.Data.Base.Utc=fixture.Data.Base.Utc.AddDays(30);fixture.Data.Base.Time+=3000000;
        var before=fixture.Inventory();var store=fixture.Fresh();var result=store.AdoptExecution(fixture.Id,fixture.Sha);var handle=result["levelMoveExecutionHandle"]!.GetValue<string>();
        Assert.True(result["verificationOnly"]!.GetValue<bool>());Assert.False(result["executionAuthorized"]!.GetValue<bool>());
        Assert.Equal(fixture.Id,store.ExecutionProof(handle)["executionRequestId"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>store.Consume(handle));Assert.Throws<ReferenceRejected>(()=>fixture.Fresh().ExecutionProof(handle));
        Assert.Equal(before.OrderBy(x=>x.Key),fixture.Inventory().OrderBy(x=>x.Key));
    }
    [Theory][InlineData("wrong_claim_intent")][InlineData("journal_false")][InlineData("receipt_false")][InlineData("integer_return")][InlineData("wrong_request")][InlineData("pre_command_state")][InlineData("working_changed")][InlineData("checkpoint_writable")][InlineData("writer_present")][InlineData("claim_missing")][InlineData("receipt_sha")]
    public void MissingOrContradictoryEvidenceIssuesNoVerificationHandleAndChangesNoArtifacts(string fault)
    {
        var fixture=new Evidence(fault).Build();var before=fixture.Inventory();Assert.ThrowsAny<Exception>(()=>fixture.Fresh().AdoptExecution(fixture.Id,fixture.Sha));
        Assert.Equal(before.OrderBy(x=>x.Key),fixture.Inventory().OrderBy(x=>x.Key));
    }
    [Theory][InlineData("../escape",64)][InlineData("ABCDEFABCDEFABCDEFABCDEFABCDEFAB",64)][InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",63)]
    public void InvalidIdentitiesAreRejectedBeforeFileAccess(string id,int hashLength)
    {
        var fixture=new Evidence();Assert.Throws<ReferenceRejected>(()=>fixture.Fresh().AdoptExecution(id,new string('a',hashLength)));
    }
}
