using System.Text.Json.Nodes;
using PowerShapeMcp;

namespace PowerShapeMcp.Tests;

// Complete synthetic hash-chain/receipt fixture, opaque files only; no native COM.
internal sealed class SavedReopenFixture
{
    internal readonly OwnedReopenTests.Fixture Data=new();
    internal BridgeSession Session=>Data.Target.Session;
    internal string Root=>Data.Saved.Root;
    internal JsonObject Prepared { get; }
    internal string Handle { get; }
    internal JsonObject Original { get; private set; }=null!;
    internal string Operation { get; private set; }="";
    internal string OriginalSha { get; private set; }="";
    private string? prior;private int sequence;
    private string Publish(string id,JsonObject receipt)
    {
        var hash=ArtifactProof.Freeze(Root,"responses/"+id+".pending",receipt);
        File.Move(FileArtifacts.FixedPath(Root,"responses",id+".pending"),FileArtifacts.FixedPath(Root,"responses",id+".json"));return hash;
    }
    internal SavedReopenFixture()
    {
        Prepared=Data.Prepared();Handle=Session.Reopens.Issue(Prepared,Session.SavedResults.Parameters(Data.Handle),Data.Handle)["ownedReopenHandle"]!.GetValue<string>();
    }
    private void Append(string state,JsonObject details)
    {
        var item=new JsonObject{["schemaVersion"]=1,["operationId"]=Operation,["planSha256"]=Original["planSha256"]!.DeepClone(),["sequence"]=sequence,["previousSha256"]=prior,["state"]=state,["timestampUtc"]=LeaseTime.Format(Data.Target.Data.Utc),["details"]=details};
        prior=ArtifactProof.Freeze(Root,$"operations/{Operation}/{sequence:D6}.json",item);sequence++;
    }
    internal JsonObject Execution(string id,JsonObject parameters,bool publish=true)
    {
        Operation=id;var prepared=Prepared["reopen"]!.AsObject();var plan=parameters.DeepClone().AsObject();plan["schemaVersion"]=1;plan["operationId"]=id;plan["operation"]="open_owned_saved_result";
        plan["bridgeVersion"]="0.33.0-dev";plan["createdUtc"]=LeaseTime.Format(Data.Target.Data.Utc);plan["geometryExecutionEnabled"]=false;
        foreach(var key in new[]{"copyId","ownershipSha256","workingPath"})plan[key]=prepared[key]!.DeepClone();
        var hash=ArtifactProof.Freeze(Root,"copyopenplans/"+id+".json",plan);
        Original=new(){["state"]="owned_saved_reopen_executed_unverified",["operationId"]=id,["bridgeRequestId"]=id,["planSha256"]=hash,["plan"]=plan,["contextChangeAttempted"]=true,["cadSaveAttempted"]=false,["geometryMutationAttempted"]=false,
            ["observableContentChecked"]=true,["checkpointVerified"]=true,["savedFilesUnchanged"]=true,["recordedJournalState"]="executed_unverified",["sourceSavedResultSha256"]=prepared["savedResult"]!["sha256"]!.DeepClone()};
        foreach(var key in new[]{"copyId","ownershipSha256","workingPath","reopenId","reopenSha256"})Original[key]=plan[key]!.DeepClone();
        ArtifactProof.Freeze(Root,"workingcopies/"+prepared["copyId"]!.GetValue<string>()+"/open-once.json",new(){["operationId"]=id,["planSha256"]=hash,["copyId"]=prepared["copyId"]!.DeepClone(),["ownershipSha256"]=prepared["ownershipSha256"]!.DeepClone(),["automaticReplayForbidden"]=true});
        foreach(var state in new[]{"prepared","execution_claimed","command_started","command_returned","executed_unverified"})Append(state,state=="command_returned"?new(){["nativeRequestId"]=id}:new());
        // A lost wire reply retains the original proof in the owned fixture but
        // never recreates a response under the executor's polling filename.
        OriginalSha=publish?Publish(id,Original):ArtifactProof.Freeze(Root,"responses/"+id+".unpublished",Original);return Original.DeepClone().AsObject();
    }
    internal JsonObject FreshReads(bool forwardPath=false)
    {
        Data.Target.Data.Time+=10;Data.Target.Data.Utc=Data.Target.Data.Utc.AddSeconds(10);
        var snapshot=ArtifactProof.Verified(Root,"responses",Data.Saved.Parameters["inspectionRequestId"]!.GetValue<string>(),Data.Saved.Parameters["inspectionSha256"]!.GetValue<string>(),16777216);
        snapshot["model"]!["id"]=500;snapshot["model"]!["path"]=Prepared["reopen"]!["workingPath"]!.DeepClone();snapshot["model"]!["name"]=Path.GetFileNameWithoutExtension(snapshot["model"]!["path"]!.GetValue<string>());
        if(forwardPath)snapshot["model"]!["path"]=snapshot["model"]!["path"]!.GetValue<string>().Replace('\\','/');
        foreach(var entity in snapshot["entities"]!.AsArray())entity!["nativeId"]=entity["nativeId"]!.GetValue<int>()+1000;
        var inspectionId=Guid.NewGuid().ToString("N");snapshot["bridgeRequestId"]=inspectionId;snapshot["timestampUtc"]=LeaseTime.Format(Data.Target.Data.Utc);
        var sha=ArtifactProof.Freeze(Root,"responses/"+inspectionId+".json",snapshot);var candidate=Session.Candidates.Issue(snapshot,sha)["candidateDocumentHandle"]!.GetValue<string>();
        var active=new JsonObject{["modelId"]=500,["windowNumber"]=2,["nativeWindowId"]=90,["type"]="Model",["selectedRaw"]=1,["modelName"]=snapshot["model"]!["name"]!.DeepClone(),["modelPath"]=snapshot["model"]!["path"]!.DeepClone()};
        var listingId=Guid.NewGuid().ToString("N");var listing=new JsonObject{["state"]="documents_listed_verified",["bridgeRequestId"]=listingId,["timestampUtc"]=LeaseTime.Format(Data.Target.Data.Utc),["windowCount"]=1,["activeWindow"]=active,["windows"]=new JsonArray(active.DeepClone()),
            ["documents"]=new JsonArray(new JsonObject{["modelId"]=500,["windowNumber"]=2,["activationSupported"]=true})};
        foreach(var key in new[]{"processId","processStartedUtc","windowsSessionId"})listing[key]=snapshot[key]!.DeepClone();
        var listingSha=ArtifactProof.Freeze(Root,"responses/"+listingId+".json",listing);var choice=Session.Documents.Issue(listing,listingSha)["documents"]![0]!["documentHandle"]!.GetValue<string>();
        return new(){["reopenRequestId"]=Operation,["reopenReceiptSha256"]=OriginalSha,["candidateDocumentHandle"]=candidate,["activeDocumentHandle"]=choice};
    }
    internal JsonObject Verification(string id,JsonObject parameters)
    {
        var evidence=new JsonArray();foreach(var pair in new[]{("reopenRequestId","reopenReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})evidence.Add(new JsonObject{["requestId"]=parameters[pair.Item1]!.DeepClone(),["sha256"]=parameters[pair.Item2]!.DeepClone()});
        Append("succeeded_verified",new(){["evidence"]=evidence.DeepClone(),["noCommandReplay"]=true,["ownedModelId"]=500,["sourceSavedResultSha256"]=Original["sourceSavedResultSha256"]!.DeepClone()});
        var binding=new JsonObject{["schemaVersion"]=1,["bindingId"]=id,["bridgeVersion"]="0.33.0-dev",["timestampUtc"]=LeaseTime.Format(Data.Target.Data.Utc),["observedPropertiesSha256"]=new string('a',64),["windowContextSha256"]=new string('b',64),["executionEnabled"]=false};
        foreach(var key in new[]{"copyId","ownershipSha256"})binding[key]=Original[key]!.DeepClone();foreach(var key in new[]{"inspectionRequestId","inspectionSha256","expiresUtc"})binding[key]=parameters[key]!.DeepClone();
        var bindingSha=ArtifactProof.Freeze(Root,"documentbindings/"+id+".json",binding);
        var snapshot=ArtifactProof.Verified(Root,"responses",parameters["inspectionRequestId"]!.GetValue<string>(),parameters["inspectionSha256"]!.GetValue<string>(),16777216);
        var result=new JsonObject{["state"]="owned_saved_reopen_verified_observed",["bridgeRequestId"]=id,["reopenOperationId"]=Operation,["reopenReceiptSha256"]=OriginalSha,["recordedJournalState"]="succeeded_verified",["timestampUtc"]=LeaseTime.Format(Data.Target.Data.Utc),["expiresUtc"]=parameters["expiresUtc"]!.DeepClone(),
            ["cadSaveAttempted"]=false,["contextChangeAttempted"]=false,["geometryMutationAttempted"]=false,["executionEnabled"]=false,["noCommandReplay"]=true,["savedFilesUnchanged"]=true,["observableContentChecked"]=true,["evidence"]=evidence,["model"]=snapshot["model"]!.DeepClone(),["bindingId"]=id,["bindingSha256"]=bindingSha,["binding"]=binding};
        foreach(var key in new[]{"reopenId","reopenSha256","copyId","ownershipSha256","planSha256","sourceSavedResultSha256"})result[key]=Original[key]!.DeepClone();
        Publish(id,result);return result;
    }
}
