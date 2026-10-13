using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

// Synthetic durable return proof only. This fixture never executes native code
// or models CAD postconditions; native executor/verification faults have their
// own separate suite. Here the real SDK must preserve this evidence on timeout.
internal static class LostReplyFixture
{
    internal static JsonObject Return(string root,string id,JsonObject parameters,JsonObject binding,bool translation,DateTimeOffset now)
    {
        var plan=parameters.DeepClone().AsObject();
        plan["schemaVersion"]=1;plan["operationId"]=id;
        plan["operation"]=translation?"translate_owned_solid":"open_working_copy";
        foreach(var key in new[]{"copyId","ownershipSha256"})plan[key]=binding[key]!.DeepClone();
        if(translation)plan["executionEnabled"]=true;else plan["geometryExecutionEnabled"]=false;
        var planSha=ArtifactProof.Freeze(root,(translation?"ownedexecutions/":"copyopenplans/")+id+".json",plan);
        var claim=new JsonObject{["operationId"]=id,["planSha256"]=planSha,["copyId"]=plan["copyId"]!.DeepClone(),["automaticReplayForbidden"]=true};
        if(translation)
        {
            var packet=ArtifactProof.Verified(root,"ownedoperations",parameters["ownedOperationId"]!.GetValue<string>(),parameters["ownedOperationSha256"]!.GetValue<string>(),65536);
            claim["ownedOperationId"]=parameters["ownedOperationId"]!.DeepClone();
            ArtifactProof.Freeze(root,"operationplans/"+packet["operationPlanId"]!.GetValue<string>()+".execution-once.json",claim);
            var fence=claim.DeepClone().AsObject();fence["ownershipSha256"]=plan["ownershipSha256"]!.DeepClone();
            ArtifactProof.Freeze(root,"workingcopies/"+plan["copyId"]!.GetValue<string>()+"/mutation-fence.json",fence);
        }
        else
        {
            claim["ownershipSha256"]=plan["ownershipSha256"]!.DeepClone();
            ArtifactProof.Freeze(root,"workingcopies/"+plan["copyId"]!.GetValue<string>()+"/open-once.json",claim);
        }
        string? previous=null;var sequence=0;
        foreach(var state in new[]{"prepared","execution_claimed","command_started","command_returned","executed_unverified"})
        {
            var details=new JsonObject();if(state=="command_returned"){details["nativeRequestId"]=id;if(translation)details["returnCode"]=1;}
            previous=ArtifactProof.Freeze(root,$"operations/{id}/{sequence:D6}.json",new()
            {
                ["schemaVersion"]=1,["operationId"]=id,["planSha256"]=planSha,["sequence"]=sequence++,
                ["previousSha256"]=previous,["state"]=state,["timestampUtc"]=LeaseTime.Format(now),["details"]=details
            });
        }
        var receipt=new JsonObject
        {
            ["state"]=translation?"owned_translation_executed_unverified":"working_copy_opened_observed",
            ["bridgeRequestId"]=id,["operationId"]=id,["planSha256"]=planSha,["plan"]=plan,
            ["copyId"]=plan["copyId"]!.DeepClone(),["ownershipSha256"]=plan["ownershipSha256"]!.DeepClone(),
            ["geometryMutationAttempted"]=translation,["contextChangeAttempted"]=!translation,["cadSaveAttempted"]=false,
            ["recordedJournalState"]="executed_unverified",["commandCalls"]=1
        };
        if(translation){receipt["returnCode"]=1;receipt["copyFenced"]=true;}
        ArtifactProof.Freeze(root,"responses/"+id+".unpublished",receipt);return receipt;
    }

    internal static void PublishOriginal(string root,string id)=>File.Move(
        FileArtifacts.FixedPath(root,"responses",id+".unpublished"),FileArtifacts.FixedPath(root,"responses",id+".json"));

    internal static void OneAuthoritativeReturn(string root,string id)
    {
        var records=Directory.GetFiles(FileArtifacts.FixedPath(root,"operations",id),"*.json")
            .Select(path=>JsonNode.Parse(File.ReadAllText(path))!.AsObject()).OrderBy(value=>value["sequence"]!.GetValue<int>()).ToArray();
        Assert.Equal(5,records.Length);Assert.Single(records,value=>value["state"]!.GetValue<string>()=="command_started");
        Assert.Single(records,value=>value["state"]!.GetValue<string>()=="command_returned");
        string? previous=null;
        foreach(var record in records)
        {
            Assert.Equal(previous,record["previousSha256"]?.GetValue<string>());
            var path=FileArtifacts.FixedPath(root,"operations",id,record["sequence"]!.GetValue<int>().ToString("D6")+".json");
            previous=FileArtifacts.Sha256(File.ReadAllBytes(path));
        }
    }
}
