using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerShape.Shared;

namespace PowerShapeMcp;

internal sealed class OwnedWorkplaneStore(string root,DocumentBindingStore bindings,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private const string Code="owned_workplane_proof_invalid";
    private sealed record Lease(string Binding,JsonObject Args,JsonObject Reference,double Expires);
    private sealed class Execution(JsonObject reference){internal readonly JsonObject Reference=reference;internal string? Request,Sha;}
    private readonly Dictionary<string,Lease> plans=[];
    private readonly Dictionary<string,Execution> executions=[];
    internal static string Text(JsonObject value,string key)=>ArtifactProof.String(value,key,Code);
    private static void Need(bool ok,string code=Code){if(!ok)throw new ReferenceRejected(code);}
    public void ClearPrepared()=>plans.Clear();
    public void RevokePrepared(string handle)=>plans.Remove(handle);
    public void Clear(){plans.Clear();executions.Clear();}
    internal static JsonObject Parameters(JsonObject value)
    {
        Need(ArtifactProof.Fields(value,"originWorldRaw","zAxis","yAxis","coordinateFrame","lengthUnits","activeAfter","mode"));
        Need(Text(value,"coordinateFrame")=="world"&&Text(value,"lengthUnits")=="document_raw"&&Text(value,"activeAfter")=="world"&&Text(value,"mode")=="create_new");
        var frame=new List<double>();foreach(var name in new[]{"originWorldRaw","zAxis","yAxis"})
        {
            Need(value[name] is JsonArray a&&a.Count==3);
            foreach(var item in value[name]!.AsArray())
            {
                var number=JsonSerializer.SerializeToElement(item);Need(number.ValueKind==JsonValueKind.Number&&number.TryGetDouble(out var x)&&double.IsFinite(x));frame.Add(number.GetDouble());
            }
        }
        try{WorkplaneFrame.Validate(frame.ToArray());}catch(ArgumentException){throw new ReferenceRejected("owned_workplane_frame_invalid");}
        return value.DeepClone().AsObject();
    }
    public JsonObject Sources(string bindingHandle,JsonObject parameters)
    {
        var args=bindings.ValidationParameters(bindingHandle);var binding=ArtifactProof.Verified(root,"documentbindings",Text(args,"bindingId"),Text(args,"bindingSha256"),65536);
        bindings.VerifyReceipt(new(){["bindingId"]=args["bindingId"]!.DeepClone(),["bindingSha256"]=args["bindingSha256"]!.DeepClone(),["binding"]=binding.DeepClone()});
        var result=args.DeepClone().AsObject();result["parameters"]=Parameters(parameters);return result;
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
    private JsonObject Packet(JsonObject result,JsonObject args)
    {
        string id=Text(result,"ownedWorkplaneId"),sha=Text(result,"ownedWorkplaneSha256");Need(FileArtifacts.Token(id)&&FileArtifacts.Token(sha,64)&&Text(result,"bridgeRequestId")==id&&Text(result,"state")=="owned_workplane_prepared_observed");
        foreach(var flag in new[]{"executionEnabled","nativeProviderQualified","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})Need(ArtifactProof.Boolean(result[flag],false));
        var packet=ArtifactProof.Verified(root,"ownedworkplanes",id,sha,65536);
        Need(ArtifactProof.Fields(packet,"schemaVersion","ownedWorkplaneId","operation","preparedUtc","expiresUtc","executionEnabled","nativeProviderQualified","arguments","copyId","ownershipSha256","workingSha256","nativeObservedPropertiesSha256","windowContextSha256","requiredBeforeExecution")&&ArtifactProof.IntegerIs(packet["schemaVersion"],1)&&Text(packet,"ownedWorkplaneId")==id&&Text(packet,"operation")=="create_world_workplane");
        Need(JsonNode.DeepEquals(packet,result["ownedWorkplane"])&&JsonNode.DeepEquals(packet["arguments"],args)&&ArtifactProof.Boolean(packet["executionEnabled"],false)&&ArtifactProof.Boolean(packet["nativeProviderQualified"],false));
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(args,"bindingId"),Text(args,"bindingSha256"),65536);
        foreach(var field in new[]{"copyId","ownershipSha256","windowContextSha256"})Need(JsonNode.DeepEquals(packet[field],binding[field]));
        Need(JsonNode.DeepEquals(packet["nativeObservedPropertiesSha256"],binding["observedPropertiesSha256"]));
        Need(LeaseTime.Parse(Text(packet,"expiresUtc"),Code)==LeaseTime.Parse(Text(binding,"expiresUtc"),Code),"owned_workplane_expiry_differs");
        Need(LeaseTime.Parse(Text(packet,"expiresUtc"),Code)>LeaseTime.PythonPrecision(utcNow()));_=LeaseTime.Remaining(Text(packet,"preparedUtc"),utcNow(),Code);
        Need(JsonNode.DeepEquals(packet["requiredBeforeExecution"],new JsonArray("qualified_create_workplane_provider","once_claim_and_owned_fence","declared_autoactivation_then_world","independent_created_frame_and_preserved_entities_verifier","saved_result_reopen","recovery_fault_matrix")));
        var journal=ArtifactProof.Journal(root,id);Need(Text(journal,"state")=="prepared"&&ArtifactProof.IntegerIs(journal["eventCount"],1)&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&Text(journal,"planSha256")==sha);
        var observed=ArtifactProof.Verified(root,"responses",Text(binding,"inspectionRequestId"),Text(binding,"inspectionSha256"),16000000);Ownership(packet,observed);return packet;
    }
    public JsonObject Issue(JsonObject result,JsonObject args,string binding)
    {
        Need(JsonNode.DeepEquals(Sources(binding,args["parameters"]!.AsObject()),args));var packet=Packet(result,args);
        var seconds=Math.Min(bindings.Remaining(binding),(LeaseTime.Parse(Text(packet,"expiresUtc"),Code)-LeaseTime.PythonPrecision(utcNow())).TotalSeconds);Need(seconds>0&&plans.Count<128);
        string handle=Guid.NewGuid().ToString("N");var reference=new JsonObject{["ownedWorkplaneId"]=result["ownedWorkplaneId"]!.DeepClone(),["ownedWorkplaneSha256"]=result["ownedWorkplaneSha256"]!.DeepClone()};
        plans[handle]=new(binding,args.DeepClone().AsObject(),reference,now()+seconds);var output=result.DeepClone().AsObject();output["ownedWorkplaneHandle"]=handle;output["workplaneLifetimeSeconds"]=(int)seconds;return output;
    }
    public (string ExecutionHandle,JsonObject Parameters) Consume(string handle)
    {
        Need(plans.TryGetValue(handle,out var lease)&&lease.Expires>now(),"owned_workplane_handle_unknown_or_expired");
        Need(JsonNode.DeepEquals(Sources(lease!.Binding,lease.Args["parameters"]!.AsObject()),lease.Args));
        var packet=ArtifactProof.Verified(root,"ownedworkplanes",Text(lease.Reference,"ownedWorkplaneId"),Text(lease.Reference,"ownedWorkplaneSha256"),65536);
        var reply=new JsonObject{["state"]="owned_workplane_prepared_observed",["bridgeRequestId"]=lease.Reference["ownedWorkplaneId"]!.DeepClone(),["ownedWorkplaneId"]=lease.Reference["ownedWorkplaneId"]!.DeepClone(),["ownedWorkplaneSha256"]=lease.Reference["ownedWorkplaneSha256"]!.DeepClone(),["ownedWorkplane"]=packet};
        foreach(var flag in new[]{"executionEnabled","nativeProviderQualified","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})reply[flag]=false;
        Packet(reply,lease.Args);Need(executions.Count<128);plans.Remove(handle);string execution=Guid.NewGuid().ToString("N");executions[execution]=new(lease.Reference.DeepClone().AsObject());return(execution,lease.Reference.DeepClone().AsObject());
    }
    public void Record(string handle,JsonObject response)
    {
        var record=executions[handle];if(response["bridgeRequestId"] is JsonValue token&&token.TryGetValue<string>(out var id)&&FileArtifacts.Token(id))record.Request=id;
        if(record.Request is not null&&File.Exists(FileArtifacts.FixedPath(root,"responses",record.Request+".json")))record.Sha=FileArtifacts.Sha256(FileArtifacts.ReadBytes(root,Path.Combine("responses",record.Request+".json"),16000000));
    }
    public JsonObject ExecutionProof(string handle)
    {
        Need(executions.TryGetValue(handle,out var value)&&value.Request is not null,"owned_workplane_execution_unknown");var entry=value!;
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("responses",entry.Request+".json"),16000000);string sha=FileArtifacts.Sha256(raw);Need(entry.Sha is null||entry.Sha==sha);entry.Sha=sha;
        var receipt=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();Need(Text(receipt,"operationId")==entry.Request&&Text(receipt,"state") is "owned_workplane_executed_unverified" or "owned_workplane_outcome_unknown");
        foreach(var flag in new[]{"cadMutationAttempted","geometryMutationAttempted","contextChangeAttempted"})Need(ArtifactProof.Boolean(receipt[flag],true));Need(ArtifactProof.Boolean(receipt["cadSaveAttempted"],false));
        var plan=ArtifactProof.Verified(root,"workplaneexecutions",entry.Request!,Text(receipt,"planSha256"),16000000);Need(JsonNode.DeepEquals(plan,receipt["plan"])&&Text(plan,"operationId")==entry.Request&&Text(plan,"operation")=="create_owned_world_workplane");
        foreach(var field in entry.Reference)Need(JsonNode.DeepEquals(field.Value,plan[field.Key]));
        var claimRaw=FileArtifacts.ReadBytes(root,Path.Combine("ownedworkplanes",Text(plan,"ownedWorkplaneId")+".execution-once.json"),65536);Need(FileArtifacts.Sha256(claimRaw)==Text(receipt,"claimSha256"));
        var claim=JsonNode.Parse(FileArtifacts.Parse(claimRaw).GetRawText())!.AsObject();Need(ArtifactProof.Fields(claim,"operationId","planSha256","ownedWorkplaneId","copyId","automaticReplayForbidden")&&Text(claim,"operationId")==entry.Request&&Text(claim,"planSha256")==Text(receipt,"planSha256")&&Text(claim,"ownedWorkplaneId")==Text(plan,"ownedWorkplaneId")&&Text(claim,"copyId")==Text(plan,"copyId")&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true));
        var journal=ArtifactProof.Journal(root,entry.Request!);Need(Text(journal,"planSha256")==Text(receipt,"planSha256")&&ArtifactProof.Boolean(journal["writerLockPresent"],false));
        var returned=journal["events"]!.AsArray().Single(e=>e!["state"]!.GetValue<string>()=="command_returned")!["details"]!.AsObject();
        Need(ArtifactProof.Fields(returned,"nativeRequestId","createdCompositeId","nativeReturnType","worldActivationReturned","creationReceiptSha256")&&Text(returned,"nativeRequestId")==entry.Request&&Text(returned,"nativeReturnType")=="System.Int32"&&ArtifactProof.Boolean(returned["worldActivationReturned"],true));
        var n=JsonSerializer.SerializeToElement(returned["createdCompositeId"]);Need(n.ValueKind==JsonValueKind.Number&&n.TryGetInt32(out var id)&&id>0);
        var creationRaw=FileArtifacts.ReadBytes(root,Path.Combine("workplaneexecutions",entry.Request+".creation-return.json"),65536);Need(FileArtifacts.Sha256(creationRaw)==Text(returned,"creationReceiptSha256"));var creation=JsonNode.Parse(FileArtifacts.Parse(creationRaw).GetRawText())!.AsObject();
        Need(ArtifactProof.Fields(creation,"schemaVersion","operationId","planSha256","createdCompositeId","nativeReturnType","createdActiveRaw","createCalls","timestampUtc")&&ArtifactProof.IntegerIs(creation["schemaVersion"],1)&&creation["createdActiveRaw"] is JsonValue active&&active.TryGetValue<int>(out var activeRaw)&&activeRaw>0&&Text(creation,"operationId")==entry.Request&&Text(creation,"planSha256")==Text(receipt,"planSha256")&&JsonNode.DeepEquals(creation["createdCompositeId"],returned["createdCompositeId"])&&Text(creation,"nativeReturnType")=="System.Int32"&&ArtifactProof.IntegerIs(creation["createCalls"],1));
        return new(){["executionRequestId"]=entry.Request,["executionReceiptSha256"]=sha};
    }
    public JsonObject AdoptExecution(string request,string receiptSha)
    {
        Need(FileArtifacts.Token(request)&&FileArtifacts.Token(receiptSha,64)&&executions.Count<128);
        var receipt=ArtifactProof.Verified(root,"responses",request,receiptSha,16000000);var plan=receipt["plan"]!.AsObject();
        var packet=ArtifactProof.Verified(root,"ownedworkplanes",Text(plan,"ownedWorkplaneId"),Text(plan,"ownedWorkplaneSha256"),65536);
        Need(Text(packet,"operation")=="create_world_workplane"&&ArtifactProof.Boolean(packet["executionEnabled"],false));
        foreach(var field in new[]{"copyId","ownershipSha256","workingSha256","windowContextSha256"})Need(JsonNode.DeepEquals(packet[field],plan[field]));
        Need(JsonNode.DeepEquals(Parameters(packet["arguments"]!["parameters"]!.AsObject()),plan["parameters"]));Ownership(packet,plan["before"]!.AsObject());
        string handle=Guid.NewGuid().ToString("N");executions[handle]=new(new(){["ownedWorkplaneId"]=plan["ownedWorkplaneId"]!.DeepClone(),["ownedWorkplaneSha256"]=plan["ownedWorkplaneSha256"]!.DeepClone()}){Request=request,Sha=receiptSha};
        try{_=ExecutionProof(handle);return new(){["state"]="owned_workplane_execution_adopted_for_verification",["workplaneExecutionHandle"]=handle,["executionRequestId"]=request,["executionReceiptSha256"]=receiptSha,["verificationOnly"]=true,["executionAuthorized"]=false,["retryAllowed"]=false,["cadMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};}
        catch{executions.Remove(handle);throw;}
    }
    public void CheckExecuted(string handle)
    {
        var proof=ExecutionProof(handle);string id=Text(proof,"executionRequestId");var receipt=ArtifactProof.Verified(root,"responses",id,Text(proof,"executionReceiptSha256"),16000000);
        Need(Text(receipt,"state")=="owned_workplane_executed_unverified"&&ArtifactProof.IntegerIs(receipt["createCalls"],1)&&ArtifactProof.IntegerIs(receipt["worldActivationCalls"],1)&&ArtifactProof.IntegerIs(receipt["commandCalls"],2)&&ArtifactProof.Boolean(receipt["sameWorkerPostconditionsPassed"],true)&&ArtifactProof.Boolean(receipt["copyFenced"],true)&&Text(ArtifactProof.Journal(root,id),"state")=="executed_unverified");
        var plan=receipt["plan"]!.AsObject();var bytes=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",Text(plan,"copyId"),"mutation-fence.json"),65536);Need(FileArtifacts.Sha256(bytes)==Text(receipt,"executionFenceSha256"));
        var fence=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();Need(Text(fence,"operationId")==id&&Text(fence,"planSha256")==Text(receipt,"planSha256")&&Text(fence,"copyId")==Text(plan,"copyId"));
    }
    public void CheckVerified(string handle,JsonObject reply,JsonObject args)
    {
        var proof=ExecutionProof(handle);string id=Text(proof,"executionRequestId");Need(Text(reply,"state")=="owned_workplane_verified_observed"&&Text(reply,"executionOperationId")==id&&Text(reply,"recordedJournalState")=="succeeded_verified"&&ArtifactProof.Boolean(reply["copyFenceReleased"],true)&&ArtifactProof.Boolean(reply["noCommandReplay"],true));
        foreach(var flag in new[]{"cadMutationAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})Need(ArtifactProof.Boolean(reply[flag],false));
        var receipt=ArtifactProof.Verified(root,"responses",id,Text(proof,"executionReceiptSha256"),16000000);Need(Text(ArtifactProof.Journal(root,id),"state")=="succeeded_verified");var fence=FileArtifacts.FixedPath(root,"workingcopies",Text(receipt["plan"]!.AsObject(),"copyId"),"mutation-fence.json");Need(!File.Exists(fence)&&!Directory.Exists(fence));
        var expected=new JsonArray();foreach(var pair in new[]{("executionRequestId","executionReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})expected.Add(new JsonObject{["requestId"]=args[pair.Item1]!.DeepClone(),["sha256"]=args[pair.Item2]!.DeepClone()});Need(JsonNode.DeepEquals(expected,reply["evidence"]));
    }
}
