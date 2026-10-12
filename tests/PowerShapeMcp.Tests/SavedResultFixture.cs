using System.Text.Json.Nodes;
using PowerShapeMcp;

namespace PowerShapeMcp.Tests;

// Actual files and complete hash-chain fixture, no COM or native worker process.
internal sealed class SavedResultFixture
{
    internal OwnedSaveTargetTests.TargetFixture Target { get; }
    internal string Root=>Target.Data.Root;
    internal BridgeSession Session=>Target.Session;
    internal JsonObject ExecutionReceipt { get; private set; }=null!;
    internal string SaveId { get; private set; }="";
    internal string SaveSha { get; private set; }="";
    internal JsonObject Parameters { get; private set; }=null!;
    internal string Candidate { get; private set; }="";
    internal string Choice { get; private set; }="";
    internal JsonObject Verification { get; private set; }=null!;
    private string? previous;
    private int sequence;
    internal SavedResultFixture(OwnedSaveTargetTests.TargetFixture target){Target=target;}
    private void Append(string state,JsonObject details)
    {
        var value=new JsonObject{["schemaVersion"]=1,["operationId"]=SaveId,["planSha256"]=ExecutionReceipt["planSha256"]!.DeepClone(),["sequence"]=sequence,["previousSha256"]=previous,
            ["state"]=state,["timestampUtc"]=LeaseTime.Format(Target.Data.Utc),["details"]=details};
        previous=ArtifactProof.Freeze(Root,$"operations/{SaveId}/{sequence:D6}.json",value);sequence++;
    }
    internal JsonObject Execute(string id,bool publish=true)
    {
        SaveId=id;var parameters=new JsonObject{["targetId"]=Target.Id,["targetSha256"]=Target.Receipt["targetSha256"]!.DeepClone()};var plan=parameters.DeepClone().AsObject();
        plan["schemaVersion"]=1;plan["operationId"]=id;plan["operation"]="save_owned_document_as";plan["executionEnabled"]=true;
        foreach(var name in new[]{"copyId","ownershipSha256","resultPath"})plan[name]=Target.Plan[name]!.DeepClone();plan["createdUtc"]=LeaseTime.Format(Target.Data.Utc);
        var planSha=ArtifactProof.Freeze(Root,"saveexecutions/"+id+".json",plan);
        var path=plan["resultPath"]!.GetValue<string>();Target.Data.Files.Write(Path.GetRelativePath(Root,path),"Opaque synthetic saved result, never CAD.");var bytes=File.ReadAllBytes(path);
        var file=new JsonObject{["path"]=path,["sha256"]=FileArtifacts.Sha256(bytes),["length"]=bytes.Length,["fileIdentity"]=new JsonObject{["volumeSerial"]=new string('a',8),["fileIndex"]=new string('b',16),["creationTime"]=new string('c',16)}};
        ExecutionReceipt=new(){["state"]="owned_save_as_executed_unverified",["operationId"]=id,["bridgeRequestId"]=id,["timestampUtc"]=LeaseTime.Format(Target.Data.Utc),["plan"]=plan,["planSha256"]=planSha,
            ["cadSaveAttempted"]=true,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=true,["saveCommandReturned"]=true,["copyFenced"]=true,["commandCalls"]=1,["recordedJournalState"]="executed_unverified",["savedResult"]=file};
        foreach(var name in new[]{"copyId","ownershipSha256","resultPath"})ExecutionReceipt[name]=plan[name]!.DeepClone();
        var fence=new JsonObject{["schemaVersion"]=1,["operationId"]=id,["planSha256"]=planSha,["copyId"]=plan["copyId"]!.DeepClone(),["ownershipSha256"]=plan["ownershipSha256"]!.DeepClone(),["automaticReplayForbidden"]=true};
        ExecutionReceipt["executionFenceSha256"]=ArtifactProof.Freeze(Root,"workingcopies/"+plan["copyId"]!.GetValue<string>()+"/mutation-fence.json",fence);
        ArtifactProof.Freeze(Root,"savedresults/"+Target.Id+"/save-once.json",new(){["operationId"]=id,["planSha256"]=planSha,["targetId"]=Target.Id,["automaticReplayForbidden"]=true});
        foreach(var state in new[]{"prepared","execution_claimed","command_started","command_returned","executed_unverified"})Append(state,state=="command_returned"?new(){["nativeRequestId"]=id,["saveCommandReturned"]=true}:new());
        if(publish)PublishExecution(ExecutionReceipt);return ExecutionReceipt.DeepClone().AsObject();
    }
    internal void PublishExecution(JsonObject receipt){SaveSha=Publish(SaveId,receipt);}
    private string Publish(string id,JsonObject receipt)
    {
        var sha=ArtifactProof.Freeze(Root,"responses/"+id+".pending",receipt);
        File.Move(FileArtifacts.FixedPath(Root,"responses",id+".pending"),FileArtifacts.FixedPath(Root,"responses",id+".json"));return sha;
    }
    internal JsonObject FreshReads()
    {
        Target.Data.Utc=Target.Data.Utc.AddSeconds(10);Target.Data.Time+=10;
        var sourceId=Target.Plan["inspectionRequestId"]!.GetValue<string>();var snapshot=JsonNode.Parse(FileArtifacts.ReadBytes(Root,"responses/"+sourceId+".json",16777216))!.AsObject();
        var model=snapshot["model"]!.AsObject();model["path"]=Target.Plan["resultPath"]!.DeepClone();model["name"]=Path.GetFileNameWithoutExtension(model["path"]!.GetValue<string>());
        var inspection=Guid.NewGuid().ToString("N");snapshot["bridgeRequestId"]=inspection;snapshot["timestampUtc"]=LeaseTime.Format(Target.Data.Utc);
        var sourceSha=ArtifactProof.Freeze(Root,"responses/"+inspection+".json",snapshot);Candidate=Session.Candidates.Issue(snapshot,sourceSha)["candidateDocumentHandle"]!.GetValue<string>();
        var active=new JsonObject{["windowNumber"]=1,["nativeWindowId"]=3,["type"]="Model",["selectedRaw"]=1,["modelId"]=model["id"]!.DeepClone(),["modelName"]=model["name"]!.DeepClone(),["modelPath"]=model["path"]!.DeepClone()};
        var listingId=Guid.NewGuid().ToString("N");var listing=new JsonObject{["state"]="documents_listed_verified",["bridgeRequestId"]=listingId,["timestampUtc"]=LeaseTime.Format(Target.Data.Utc),["windowCount"]=1,["activeWindow"]=active,["windows"]=new JsonArray(active.DeepClone()),
            ["documents"]=new JsonArray(new JsonObject{["modelId"]=model["id"]!.DeepClone(),["windowNumber"]=1,["activationSupported"]=true})};
        foreach(var key in new[]{"processId","processStartedUtc","windowsSessionId"})listing[key]=snapshot[key]!.DeepClone();
        var listingSha=ArtifactProof.Freeze(Root,"responses/"+listingId+".json",listing);Choice=Session.Documents.Issue(listing,listingSha)["documents"]![0]!["documentHandle"]!.GetValue<string>();
        return new(){["saveRequestId"]=SaveId,["saveReceiptSha256"]=SaveSha,["candidateDocumentHandle"]=Candidate,["activeDocumentHandle"]=Choice};
    }
    internal JsonObject Verify(string id,JsonObject parameters,bool append=true)
    {
        Parameters=parameters.DeepClone().AsObject();var evidence=new JsonArray();
        foreach(var pair in new[]{("saveRequestId","saveReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})evidence.Add(new JsonObject{["requestId"]=parameters[pair.Item1]!.DeepClone(),["sha256"]=parameters[pair.Item2]!.DeepClone()});
        if(append)Append("succeeded_verified",new(){["evidence"]=evidence.DeepClone(),["noCommandReplay"]=true,["savedResultSha256"]=ExecutionReceipt["savedResult"]!["sha256"]!.DeepClone()});
        File.Delete(FileArtifacts.FixedPath(Root,"workingcopies",Target.Plan["copyId"]!.GetValue<string>(),"mutation-fence.json"));
        var snapshot=ArtifactProof.Verified(Root,"responses",parameters["inspectionRequestId"]!.GetValue<string>(),parameters["inspectionSha256"]!.GetValue<string>(),16777216);
        Verification=new(){["state"]="owned_save_as_verified_observed",["bridgeRequestId"]=id,["saveOperationId"]=SaveId,["saveReceiptSha256"]=SaveSha,["planSha256"]=ExecutionReceipt["planSha256"]!.DeepClone(),
            ["savedResult"]=ExecutionReceipt["savedResult"]!.DeepClone(),["recordedJournalState"]="succeeded_verified",["copyFenceReleased"]=true,["savedFilesUnchanged"]=true,["noCommandReplay"]=true,
            ["cadSaveAttempted"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["expiresUtc"]=parameters["expiresUtc"]!.DeepClone(),["timestampUtc"]=LeaseTime.Format(Target.Data.Utc),["model"]=snapshot["model"]!.DeepClone(),["evidence"]=evidence};
        foreach(var name in new[]{"targetId","targetSha256","copyId","ownershipSha256"})Verification[name]=ExecutionReceipt["plan"]![name]!.DeepClone();
        Publish(id,Verification);return Verification.DeepClone().AsObject();
    }
}
