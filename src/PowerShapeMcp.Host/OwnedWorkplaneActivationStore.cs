using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal sealed class OwnedWorkplaneActivationStore(string root,DocumentBindingStore bindings,CandidateStore candidates,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private const string Code="owned_workplane_activation_proof_invalid";
    private sealed record Lease(string Binding,JsonObject Target,JsonObject Args,JsonObject Reference,double Expires);
    private sealed class Execution(JsonObject reference){internal readonly JsonObject Reference=reference;internal string? Request,Sha;}
    private readonly Dictionary<string,Lease> plans=[];
    private readonly Dictionary<string,Execution> executions=[];
    internal static string Text(JsonObject value,string key)=>ArtifactProof.String(value,key,Code);
    private static void Need(bool ok,string code=Code){if(!ok)throw new ReferenceRejected(code);}
    public void ClearPrepared()=>plans.Clear();
    public void RevokePrepared(string handle)=>plans.Remove(handle);
    public void Clear(){plans.Clear();executions.Clear();}
    internal static JsonObject Parameters(JsonObject target)
    {
        string mode=Text(target,"mode");
        Need(mode=="world"?ArtifactProof.Fields(target,"mode"):mode=="existing"&&ArtifactProof.Fields(target,"mode","workplaneCandidateHandle")&&FileArtifacts.Token(Text(target,"workplaneCandidateHandle")));
        return target.DeepClone().AsObject();
    }
    public JsonObject Sources(string bindingHandle,JsonObject target)
    {
        Parameters(target);var args=bindings.ValidationParameters(bindingHandle);var binding=ArtifactProof.Verified(root,"documentbindings",Text(args,"bindingId"),Text(args,"bindingSha256"),65536);
        bindings.VerifyReceipt(new(){["bindingId"]=args["bindingId"]!.DeepClone(),["bindingSha256"]=args["bindingSha256"]!.DeepClone(),["binding"]=binding.DeepClone()});
        args["parameters"]=Text(target,"mode")=="world"?new JsonObject{["mode"]="world"}:candidates.WorkplaneTarget(Text(target,"workplaneCandidateHandle"),binding);
        return args;
    }
    private void Ownership(JsonObject packet,JsonObject before)
    {
        string id=Text(packet,"copyId");Need(FileArtifacts.Token(id)&&FileArtifacts.Token(Text(packet,"ownershipSha256"),64));
        var owner=JsonNode.Parse(FileArtifacts.Parse(FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",id,"ownership.json"),65536)).GetRawText())!.AsObject();
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",id,"ownership.json"),65536);Need(FileArtifacts.Sha256(raw)==Text(packet,"ownershipSha256")&&JsonNode.DeepEquals(owner,JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())));
        Need(ArtifactProof.Fields(owner,"schemaVersion","copyId","bridgeVersion","createdUtc","scope","executionEnabled","source","working","checkpoint")&&(ArtifactProof.IntegerIs(owner["schemaVersion"],1)||ArtifactProof.IntegerIs(owner["schemaVersion"],2))&&Text(owner,"copyId")==id&&Text(owner,"scope")=="saved_disk_bytes_only"&&ArtifactProof.Boolean(owner["executionEnabled"],false));
        var work=owner["working"]!.AsObject();var cp=owner["checkpoint"]!.AsObject();var source=owner["source"]!.AsObject();
        string path=FileArtifacts.FixedPath(root,"workingcopies",id,ArtifactProof.IntegerIs(owner["schemaVersion"],1)?"Working.psmodel":"Working_"+id+".psmodel"),checkpoint=FileArtifacts.FixedPath(root,"workingcopies",id,"checkpoint","Before.psmodel");
        bool Same(string a,string b)=>ObservationPolicy.NormalizePath(a)==ObservationPolicy.NormalizePath(b);
        Need(Same(path,Text(work,"path"))&&Same(path,Text(before["model"]!.AsObject(),"path"))&&Same(checkpoint,Text(cp,"path"))&&!Same(path,Text(source,"path"))&&!Same(checkpoint,Text(source,"path")));
        var expected=Text(cp,"sha256");Need(FileArtifacts.Token(expected,64)&&Text(source,"sha256")==expected&&Text(work,"initialSha256")==expected&&(File.GetAttributes(checkpoint)&FileAttributes.ReadOnly)!=0);
        string Hash(string file,bool mutable){using var f=new FileStream(file,FileMode.Open,FileAccess.Read,mutable?FileShare.ReadWrite:FileShare.Read);Need(f.Length is >0 and <=1073741824);return Convert.ToHexStringLower(SHA256.HashData(f));}
        Need(Hash(checkpoint,false)==expected&&Hash(path,true)==Text(packet,"workingSha256"));
    }
    private JsonObject Packet(JsonObject reply,JsonObject args,bool current)
    {
        string id=Text(reply,"activationId"),sha=Text(reply,"activationSha256");Need(FileArtifacts.Token(id)&&FileArtifacts.Token(sha,64)&&Text(reply,"bridgeRequestId")==id&&Text(reply,"state")=="owned_workplane_activation_prepared_observed");
        foreach(var flag in new[]{"executionEnabled","nativeProviderQualified","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})Need(ArtifactProof.Boolean(reply[flag],false));
        var p=ArtifactProof.Verified(root,"workplaneactivations",id,sha,65536);
        Need(ArtifactProof.Fields(p,"schemaVersion","activationId","operation","preparedUtc","expiresUtc","executionEnabled","arguments","copyId","ownershipSha256","workingSha256","nativeObservedPropertiesSha256","windowContextSha256","targetCompositeId","captureBefore")&&ArtifactProof.IntegerIs(p["schemaVersion"],1)&&Text(p,"activationId")==id&&Text(p,"operation")=="activate_existing_workplane_or_world"&&ArtifactProof.Boolean(p["executionEnabled"],false));
        Need(JsonNode.DeepEquals(p,reply["activation"])&&JsonNode.DeepEquals(p["arguments"],args));
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(args,"bindingId"),Text(args,"bindingSha256"),65536);
        foreach(var field in new[]{"copyId","ownershipSha256","windowContextSha256"})Need(JsonNode.DeepEquals(p[field],binding[field]));
        Need(JsonNode.DeepEquals(p["nativeObservedPropertiesSha256"],binding["observedPropertiesSha256"])&&LeaseTime.Parse(Text(p,"expiresUtc"),Code)==LeaseTime.Parse(Text(binding,"expiresUtc"),Code));
        if(current){Need(LeaseTime.Parse(Text(p,"expiresUtc"),Code)>LeaseTime.PythonPrecision(utcNow()));_=LeaseTime.Remaining(Text(p,"preparedUtc"),utcNow(),Code);}
        var journal=ArtifactProof.Journal(root,id);Need(Text(journal,"state")=="prepared"&&ArtifactProof.IntegerIs(journal["eventCount"],1)&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&Text(journal,"planSha256")==sha);
        var before=ArtifactProof.Verified(root,"responses",Text(binding,"inspectionRequestId"),Text(binding,"inspectionSha256"),16000000);Ownership(p,before);return p;
    }
    public JsonObject Issue(JsonObject reply,JsonObject args,string binding,JsonObject target)
    {
        Need(JsonNode.DeepEquals(Sources(binding,target),args));var p=Packet(reply,args,true);double seconds=Math.Min(bindings.Remaining(binding),(LeaseTime.Parse(Text(p,"expiresUtc"),Code)-LeaseTime.PythonPrecision(utcNow())).TotalSeconds);Need(seconds>0&&plans.Count<128);
        string handle=Guid.NewGuid().ToString("N");plans[handle]=new(binding,target.DeepClone().AsObject(),args.DeepClone().AsObject(),new(){["activationId"]=reply["activationId"]!.DeepClone(),["activationSha256"]=reply["activationSha256"]!.DeepClone()},now()+seconds);
        var result=reply.DeepClone().AsObject();result["workplaneActivationHandle"]=handle;result["activationLifetimeSeconds"]=(int)seconds;return result;
    }
    public (string ExecutionHandle,JsonObject Parameters) Consume(string handle)
    {
        Need(plans.TryGetValue(handle,out var lease)&&lease.Expires>now(),"owned_workplane_activation_handle_unknown_or_expired");
        Need(JsonNode.DeepEquals(Sources(lease!.Binding,lease.Target),lease.Args));
        var p=ArtifactProof.Verified(root,"workplaneactivations",Text(lease.Reference,"activationId"),Text(lease.Reference,"activationSha256"),65536);
        var reply=lease.Reference.DeepClone().AsObject();reply["state"]="owned_workplane_activation_prepared_observed";reply["bridgeRequestId"]=reply["activationId"]!.DeepClone();reply["activation"]=p;
        foreach(var flag in new[]{"executionEnabled","nativeProviderQualified","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})reply[flag]=false;
        Packet(reply,lease.Args,true);Need(executions.Count<128);plans.Remove(handle);string execution=Guid.NewGuid().ToString("N");executions[execution]=new(lease.Reference.DeepClone().AsObject());return(execution,lease.Reference.DeepClone().AsObject());
    }
    public void Record(string handle,JsonObject response)
    {
        var record=executions[handle];if(response["bridgeRequestId"] is JsonValue token&&token.TryGetValue<string>(out var id)&&FileArtifacts.Token(id))record.Request=id;
        if(record.Request is not null&&File.Exists(FileArtifacts.FixedPath(root,"responses",record.Request+".json")))record.Sha=FileArtifacts.Sha256(FileArtifacts.ReadBytes(root,Path.Combine("responses",record.Request+".json"),16000000));
    }
    public JsonObject ExecutionProof(string handle)
    {
        Need(executions.TryGetValue(handle,out var value)&&value.Request is not null,"owned_workplane_activation_execution_unknown");var entry=value!;
        var bytes=FileArtifacts.ReadBytes(root,Path.Combine("responses",entry.Request+".json"),16000000);string sha=FileArtifacts.Sha256(bytes);Need(entry.Sha is null||entry.Sha==sha);entry.Sha=sha;
        var receipt=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();Need(Text(receipt,"operationId")==entry.Request&&Text(receipt,"state") is "owned_workplane_activation_executed_unverified" or "owned_workplane_activation_outcome_unknown");
        foreach(var flag in new[]{"cadMutationAttempted","contextChangeAttempted"})Need(ArtifactProof.Boolean(receipt[flag],true));foreach(var flag in new[]{"geometryMutationAttempted","cadSaveAttempted"})Need(ArtifactProof.Boolean(receipt[flag],false));
        var plan=ArtifactProof.Verified(root,"workplaneactivationexecutions",entry.Request!,Text(receipt,"planSha256"),16000000);Need(JsonNode.DeepEquals(plan,receipt["plan"])&&Text(plan,"operationId")==entry.Request&&Text(plan,"operation")=="activate_owned_workplane");
        foreach(var field in entry.Reference)Need(JsonNode.DeepEquals(field.Value,plan[field.Key]));
        var packet=ArtifactProof.Verified(root,"workplaneactivations",Text(plan,"activationId"),Text(plan,"activationSha256"),65536);
        var packetReply=new JsonObject{["activationId"]=plan["activationId"]!.DeepClone(),["activationSha256"]=plan["activationSha256"]!.DeepClone(),["bridgeRequestId"]=plan["activationId"]!.DeepClone(),["activation"]=packet.DeepClone(),["state"]="owned_workplane_activation_prepared_observed"};
        foreach(var flag in new[]{"executionEnabled","nativeProviderQualified","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})packetReply[flag]=false;
        Packet(packetReply,packet["arguments"]!.AsObject(),false);
        foreach(var field in new[]{"copyId","ownershipSha256","workingSha256","windowContextSha256","captureBefore","targetCompositeId"})Need(JsonNode.DeepEquals(packet[field],plan[field]));
        Need(JsonNode.DeepEquals(packet["arguments"]!["parameters"],plan["parameters"]));
        var bindingArgs=packet["arguments"]!.AsObject();var binding=ArtifactProof.Verified(root,"documentbindings",Text(bindingArgs,"bindingId"),Text(bindingArgs,"bindingSha256"),65536);
        Need(JsonNode.DeepEquals(plan["before"],ArtifactProof.Verified(root,"responses",Text(binding,"inspectionRequestId"),Text(binding,"inspectionSha256"),16000000)));
        var claimBytes=FileArtifacts.ReadBytes(root,Path.Combine("workplaneactivations",Text(plan,"activationId")+".execution-once.json"),65536);Need(FileArtifacts.Sha256(claimBytes)==Text(receipt,"claimSha256"));var claim=JsonNode.Parse(FileArtifacts.Parse(claimBytes).GetRawText())!.AsObject();
        Need(ArtifactProof.Fields(claim,"operationId","planSha256","activationId","copyId","automaticReplayForbidden")&&Text(claim,"operationId")==entry.Request&&Text(claim,"planSha256")==Text(receipt,"planSha256")&&JsonNode.DeepEquals(claim["activationId"],plan["activationId"])&&JsonNode.DeepEquals(claim["copyId"],plan["copyId"])&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true));
        var journal=ArtifactProof.Journal(root,entry.Request!);Need(Text(journal,"planSha256")==Text(receipt,"planSha256")&&ArtifactProof.Boolean(journal["writerLockPresent"],false));
        var returned=journal["events"]!.AsArray().Single(e=>e!["state"]!.GetValue<string>()=="command_returned")!["details"]!.AsObject();
        Need(ArtifactProof.Fields(returned,"nativeRequestId","targetCompositeId","setterCalls","setterReceiptSha256")&&Text(returned,"nativeRequestId")==entry.Request&&JsonNode.DeepEquals(returned["targetCompositeId"],plan["targetCompositeId"])&&ArtifactProof.IntegerIs(returned["setterCalls"],1));
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("workplaneactivationexecutions",entry.Request+".setter-return.json"),65536);Need(FileArtifacts.Sha256(raw)==Text(returned,"setterReceiptSha256"));var setter=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        Need(ArtifactProof.Fields(setter,"schemaVersion","operationId","planSha256","targetCompositeId","activeRaw","setterCalls","setterReturned","timestampUtc")&&ArtifactProof.IntegerIs(setter["schemaVersion"],1)&&Text(setter,"operationId")==entry.Request&&Text(setter,"planSha256")==Text(receipt,"planSha256")&&JsonNode.DeepEquals(setter["targetCompositeId"],plan["targetCompositeId"])&&JsonNode.DeepEquals(setter["activeRaw"],plan["targetCompositeId"])&&ArtifactProof.IntegerIs(setter["setterCalls"],1)&&ArtifactProof.Boolean(setter["setterReturned"],true));
        return new(){["executionRequestId"]=entry.Request,["executionReceiptSha256"]=sha};
    }
    public JsonObject AdoptExecution(string request,string receiptSha)
    {
        Need(FileArtifacts.Token(request)&&FileArtifacts.Token(receiptSha,64)&&executions.Count<128);var receipt=ArtifactProof.Verified(root,"responses",request,receiptSha,16000000);var plan=receipt["plan"]!.AsObject();
        string handle=Guid.NewGuid().ToString("N");executions[handle]=new(new(){["activationId"]=plan["activationId"]!.DeepClone(),["activationSha256"]=plan["activationSha256"]!.DeepClone()}){Request=request,Sha=receiptSha};
        try{_=ExecutionProof(handle);return new(){["state"]="owned_workplane_activation_execution_adopted_for_verification",["activationExecutionHandle"]=handle,["executionRequestId"]=request,["executionReceiptSha256"]=receiptSha,["verificationOnly"]=true,["executionAuthorized"]=false,["retryAllowed"]=false,["cadMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};}
        catch{executions.Remove(handle);throw;}
    }
    public void CheckExecuted(string handle)
    {
        var proof=ExecutionProof(handle);string id=Text(proof,"executionRequestId");var receipt=ArtifactProof.Verified(root,"responses",id,Text(proof,"executionReceiptSha256"),16000000);
        Need(Text(receipt,"state")=="owned_workplane_activation_executed_unverified"&&ArtifactProof.IntegerIs(receipt["setterCalls"],1)&&ArtifactProof.Boolean(receipt["sameWorkerPostconditionsPassed"],true)&&ArtifactProof.Boolean(receipt["copyFenced"],true)&&Text(ArtifactProof.Journal(root,id),"state")=="executed_unverified");
        var plan=receipt["plan"]!.AsObject();var bytes=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",Text(plan,"copyId"),"mutation-fence.json"),65536);Need(FileArtifacts.Sha256(bytes)==Text(receipt,"executionFenceSha256"));
        var fence=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();Need(Text(fence,"operationId")==id&&Text(fence,"planSha256")==Text(receipt,"planSha256")&&Text(fence,"copyId")==Text(plan,"copyId"));
    }
    public JsonObject CheckVerified(string handle,JsonObject reply,JsonObject args)
    {
        var proof=ExecutionProof(handle);string id=Text(proof,"executionRequestId");Need(Text(reply,"state")=="owned_workplane_activation_verified_observed"&&Text(reply,"executionOperationId")==id&&Text(reply,"recordedJournalState")=="succeeded_verified"&&ArtifactProof.Boolean(reply["copyFenceReleased"],true)&&ArtifactProof.Boolean(reply["noCommandReplay"],true));
        foreach(var flag in new[]{"cadMutationAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})Need(ArtifactProof.Boolean(reply[flag],false));
        var receipt=ArtifactProof.Verified(root,"responses",id,Text(proof,"executionReceiptSha256"),16000000);Need(Text(ArtifactProof.Journal(root,id),"state")=="succeeded_verified");var fence=FileArtifacts.FixedPath(root,"workingcopies",Text(receipt["plan"]!.AsObject(),"copyId"),"mutation-fence.json");Need(!File.Exists(fence)&&!Directory.Exists(fence));
        var expected=new JsonArray();foreach(var pair in new[]{("executionRequestId","executionReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})expected.Add(new JsonObject{["requestId"]=args[pair.Item1]!.DeepClone(),["sha256"]=args[pair.Item2]!.DeepClone()});Need(JsonNode.DeepEquals(expected,reply["evidence"]));
        // Identity comes from the original frozen execution, not caller input.
        var plan=receipt["plan"]!.AsObject();return new(){["copyId"]=plan["copyId"]!.DeepClone(),["ownershipSha256"]=plan["ownershipSha256"]!.DeepClone()};
    }
}
