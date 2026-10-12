using System.Diagnostics;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Connection-local passive targets. No COM/queue calls, registration or SaveAs.
public sealed class OwnedSaveTargetStore(string root,DocumentBindingStore bindings,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(string Id,string Sha,string Binding,JsonObject Parameters,double Expires);
    private readonly Dictionary<string,Lease> targets=[];
    public void Clear()=>targets.Clear();
    public JsonObject PrepareParameters(string binding)=>bindings.ValidationParameters(binding);

    public JsonObject VerifyReceipt(JsonObject result,JsonObject? parameters=null)
    {
        const string code="save_target_native_receipt_invalid";
        var id=ArtifactProof.String(result,"targetId",code);var sha=ArtifactProof.String(result,"targetSha256",code);
        if(!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64))throw new ReferenceRejected(code);
        JsonObject plan;
        try
        {
            var raw=FileArtifacts.ReadBytes(root,Path.Combine("savedresults",id,"target.json"),65536);
            if(FileArtifacts.Sha256(raw)!=sha)throw new ReferenceRejected("save_target_artifact_sha_differs");
            plan=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        }
        catch(ReferenceRejected){throw;}
        if(!ArtifactProof.Fields(plan,"schemaVersion","targetId","operation","bridgeVersion","preparedUtc","expiresUtc","executionEnabled",
            "bindingId","bindingSha256","copyId","ownershipSha256","inspectionRequestId","inspectionSha256","nativeObservedPropertiesSha256",
            "beforeModel","workingPath","workingSha256","checkpointPath","checkpointSha256","resultPath","directoryIdentity")||
           !ArtifactProof.IntegerIs(plan["schemaVersion"],1)||!ArtifactProof.Boolean(plan["executionEnabled"],false)||
           ArtifactProof.String(plan,"targetId",code)!=id||ArtifactProof.String(plan,"operation",code)!="save_owned_document_as"||!JsonNode.DeepEquals(plan,result["target"]))throw new ReferenceRejected(code);
        foreach(var name in new[]{"bindingId","copyId","inspectionRequestId"})
            if(!FileArtifacts.Token(ArtifactProof.String(plan,name,code)))throw new ReferenceRejected(code);
        foreach(var name in new[]{"bindingSha256","ownershipSha256","inspectionSha256","nativeObservedPropertiesSha256","workingSha256","checkpointSha256"})
            if(!FileArtifacts.Token(ArtifactProof.String(plan,name,code),64))throw new ReferenceRejected(code);
        if(plan["directoryIdentity"] is not JsonObject directory||!ArtifactProof.Fields(directory,"volumeSerial","fileIndex","creationTime")||
           !FileArtifacts.Token(ArtifactProof.String(directory,"volumeSerial","save_target_directory_identity_invalid"),8)||
           !FileArtifacts.Token(ArtifactProof.String(directory,"fileIndex","save_target_directory_identity_invalid"),16)||
           !FileArtifacts.Token(ArtifactProof.String(directory,"creationTime","save_target_directory_identity_invalid"),16))throw new ReferenceRejected("save_target_directory_identity_invalid");
        var target=FileArtifacts.FixedPath(root,"savedresults",id,"Result_"+id+".psmodel");
        if(!SamePath(ArtifactProof.String(plan,"resultPath",code),target)||
           !SamePath(ArtifactProof.String(plan,"checkpointPath",code),FileArtifacts.FixedPath(root,"workingcopies",plan["copyId"]!.GetValue<string>(),"checkpoint","Before.psmodel")))throw new ReferenceRejected("save_target_fixed_layout_differs");
        var bindingId=plan["bindingId"]!.GetValue<string>();var bindingSha=plan["bindingSha256"]!.GetValue<string>();
        var binding=ArtifactProof.Verified(root,"documentbindings",bindingId,bindingSha,65536);
        bindings.VerifyReceipt(new(){["bindingId"]=bindingId,["bindingSha256"]=bindingSha,["binding"]=binding});
        foreach(var name in new[]{"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","expiresUtc"})
            if(!JsonNode.DeepEquals(plan[name],binding[name]))throw new ReferenceRejected("save_target_binding_dependency_differs");
        if(!JsonNode.DeepEquals(plan["nativeObservedPropertiesSha256"],binding["observedPropertiesSha256"]))throw new ReferenceRejected("save_target_native_digest_differs");
        var source=ArtifactProof.Verified(root,"responses",plan["inspectionRequestId"]!.GetValue<string>(),plan["inspectionSha256"]!.GetValue<string>(),16777216);
        if(!JsonNode.DeepEquals(plan["beforeModel"],source["model"])||source["model"] is not JsonObject model||
           !SamePath(ArtifactProof.String(model,"path",code),ArtifactProof.String(plan,"workingPath",code)))throw new ReferenceRejected("save_target_model_dependency_differs");
        var copyId=plan["copyId"]!.GetValue<string>();var ownershipRaw=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",copyId,"ownership.json"),65536);
        if(FileArtifacts.Sha256(ownershipRaw)!=plan["ownershipSha256"]!.GetValue<string>())throw new ReferenceRejected("save_target_ownership_sha_differs");
        var ownership=JsonNode.Parse(FileArtifacts.Parse(ownershipRaw).GetRawText())!.AsObject();
        if(!ArtifactProof.Fields(ownership,"schemaVersion","copyId","bridgeVersion","createdUtc","scope","executionEnabled","source","working","checkpoint")||
           !(ArtifactProof.IntegerIs(ownership["schemaVersion"],1)||ArtifactProof.IntegerIs(ownership["schemaVersion"],2))||
           ownership["copyId"]?.GetValue<string>()!=copyId||ownership["scope"]?.GetValue<string>()!="saved_disk_bytes_only"||!ArtifactProof.Boolean(ownership["executionEnabled"],false))throw new ReferenceRejected("save_target_ownership_invalid");
        var workName=ArtifactProof.IntegerIs(ownership["schemaVersion"],1)?"Working.psmodel":"Working_"+copyId+".psmodel";
        if(!SamePath(ArtifactProof.String(plan,"workingPath",code),FileArtifacts.FixedPath(root,"workingcopies",copyId,workName))||
           !SamePath(ownership["working"]!["path"]!.GetValue<string>(),plan["workingPath"]!.GetValue<string>())||
           !JsonNode.DeepEquals(ownership["checkpoint"]!["sha256"],plan["checkpointSha256"]))throw new ReferenceRejected("save_target_owned_file_dependency_differs");
        if(parameters is not null&&parameters.Any(p=>!JsonNode.DeepEquals(plan[p.Key],p.Value)))throw new ReferenceRejected("save_target_binding_identity_differs");
        return plan;
    }

    public JsonObject Issue(JsonObject result,JsonObject parameters,string bindingHandle)
    {
        if(result["state"]?.GetValue<string>()!="owned_save_target_prepared_files"||result["bridgeRequestId"]?.GetValue<string>()!=result["targetId"]?.GetValue<string>()||
           !ArtifactProof.Boolean(result["executionEnabled"],false)||!ArtifactProof.Boolean(result["cadSaveAttempted"],false)||
           !ArtifactProof.Boolean(result["geometryMutationAttempted"],false)||!ArtifactProof.Boolean(result["contextChangeAttempted"],false))throw new ReferenceRejected("save_target_native_receipt_invalid");
        if(!JsonNode.DeepEquals(PrepareParameters(bindingHandle),parameters))throw new ReferenceRejected("save_target_dependency_lost_during_preparation");
        var plan=VerifyReceipt(result,parameters);CheckUnclaimed(plan);
        var expiry=LeaseTime.Parse(ArtifactProof.String(plan,"expiresUtc","save_target_time_invalid"),"save_target_time_invalid");
        var issued=LeaseTime.Remaining(ArtifactProof.String(plan,"preparedUtc","save_target_time_invalid"),utcNow(),"save_target_time_invalid");
        var remaining=Math.Min(Math.Min(issued.Remaining,(expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds),bindings.Remaining(bindingHandle));
        if(remaining<=0)throw new ReferenceRejected("save_target_expired");
        foreach(var token in targets.Where(pair=>pair.Value.Expires<=now()).Select(pair=>pair.Key).ToArray())targets.Remove(token);
        if(targets.Count>=128)Clear();var handle=Guid.NewGuid().ToString("N");
        targets[handle]=new(plan["targetId"]!.GetValue<string>(),result["targetSha256"]!.GetValue<string>(),bindingHandle,parameters.DeepClone().AsObject(),now()+remaining);
        var output=result.DeepClone().AsObject();output["ownedSaveAsHandle"]=handle;output["saveAsTargetLifetimeSeconds"]=(int)remaining;return output;
    }

    public JsonObject ValidationParameters(string handle)
    {
        if(!targets.TryGetValue(handle,out var lease)||lease.Expires<=now()){targets.Remove(handle);throw new ReferenceRejected("save_target_handle_unknown_or_expired");}
        if(!JsonNode.DeepEquals(bindings.ValidationParameters(lease.Binding),lease.Parameters))throw new ReferenceRejected("save_target_binding_lost");
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("savedresults",lease.Id,"target.json"),65536);
        var plan=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        VerifyReceipt(new(){["targetId"]=lease.Id,["targetSha256"]=lease.Sha,["target"]=plan},lease.Parameters);
        CheckUnclaimed(plan);
        return new(){["targetId"]=lease.Id,["targetSha256"]=lease.Sha};
    }
    private void CheckUnclaimed(JsonObject plan)
    {
        var id=plan["targetId"]!.GetValue<string>();
        var result=FileArtifacts.FixedPath(root,"savedresults",id,"Result_"+id+".psmodel");
        var claim=FileArtifacts.FixedPath(root,"savedresults",id,"save-once.json");
        var fence=FileArtifacts.FixedPath(root,"workingcopies",plan["copyId"]!.GetValue<string>(),"mutation-fence.json");
        if(new[]{result,claim,fence}.Any(path=>File.Exists(path)||Directory.Exists(path)))throw new ReferenceRejected("save_target_collision_claim_or_fence");
    }
    private static bool SamePath(string a,string b)=>ObservationPolicy.NormalizePath(a)==ObservationPolicy.NormalizePath(b);
}
