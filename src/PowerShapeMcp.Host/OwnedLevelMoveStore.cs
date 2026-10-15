using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal sealed class OwnedLevelMoveStore(string root,CandidateStore candidates,DocumentBindingStore bindings,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(string Set,string Binding,JsonObject Args,JsonObject Reference,double Expires);
    private sealed class Execution(JsonObject reference){internal readonly JsonObject Reference=reference;internal string? Request,Sha;}
    private readonly Dictionary<string,Lease> plans=[];
    private readonly Dictionary<string,Execution> executions=[];
    private const string Code="owned_level_move_proof_invalid";
    internal static string Text(JsonObject value,string key)=>ArtifactProof.String(value,key,Code);
    private static void Need(bool value,string code=Code){if(!value)throw new ReferenceRejected(code);}
    public void ClearPrepared()=>plans.Clear();
    public void RevokePrepared(string handle)=>plans.Remove(handle);
    public void Clear(){plans.Clear();executions.Clear();}
    internal static JsonObject Parameters(JsonObject value)
    {
        Need(ArtifactProof.Fields(value,"targetLevelNumber","targetLevelName","targetActiveRaw","selectionAfterPolicy","mode"));
        var number=JsonSerializer.SerializeToElement(value["targetLevelNumber"]);Need(number.ValueKind==JsonValueKind.Number&&number.TryGetInt32(out var n)&&n is >=0 and <=997);
        Need(!string.IsNullOrWhiteSpace(Text(value,"targetLevelName"))&&Text(value,"targetLevelName").Length<=4096);
        Need(ArtifactProof.IntegerIs(value["targetActiveRaw"],0)||ArtifactProof.IntegerIs(value["targetActiveRaw"],1));
        Need(Text(value,"selectionAfterPolicy")=="cleared_by_documented_api"&&Text(value,"mode")=="move_in_place");return value.DeepClone().AsObject();
    }
    public JsonObject Sources(string setHandle,string bindingHandle,JsonObject parameters)
    {
        var definition=Parameters(parameters);var setArgs=candidates.SetParameters(setHandle);var bindingArgs=bindings.ValidationParameters(bindingHandle);
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(bindingArgs,"bindingId"),Text(bindingArgs,"bindingSha256"),65536);
        bindings.VerifyReceipt(new(){["bindingId"]=bindingArgs["bindingId"]!.DeepClone(),["bindingSha256"]=bindingArgs["bindingSha256"]!.DeepClone(),["binding"]=binding.DeepClone()});
        var set=OperationPlanStore.ReadSet(root,setArgs,LeaseTime.PythonPrecision(utcNow()));var bound=ArtifactProof.Verified(root,"responses",Text(binding,"inspectionRequestId"),Text(binding,"inspectionSha256"),16000000);
        Need(candidates.Context==set.Context&&JsonNode.DeepEquals(OperationPlanStore.SourceObservations(set.Source),OperationPlanStore.SourceObservations(bound)),"owned_level_move_source_context_differs");
        Need(set.Plan["entityKeys"] is JsonArray keys&&keys.Count==1&&ArtifactProof.IntegerIs(keys[0]![0],40),"owned_level_move_single_solid_required");
        var result=bindingArgs.DeepClone().AsObject();foreach(var pair in setArgs)result[pair.Key]=pair.Value?.DeepClone();result["parameters"]=definition;return result;
    }
    public JsonObject AddLevelReceipt(JsonObject request,JsonObject levels)
    {
        _=LevelReadTools.Validate(levels);var id=Text(levels,"bridgeRequestId");Need(FileArtifacts.Token(id));
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("responses",id+".json"),16000000);var saved=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        Need(Text(saved,"bridgeRequestId")==id&&JsonNode.DeepEquals(saved,levels),"owned_level_move_level_receipt_bytes_differ");
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(request,"bindingId"),Text(request,"bindingSha256"),65536);
        var source=ArtifactProof.Verified(root,"responses",Text(binding,"inspectionRequestId"),Text(binding,"inspectionSha256"),16000000);
        Need(JsonNode.DeepEquals(OperationPlanStore.SourceObservations(source),OperationPlanStore.SourceObservations(levels["before"]!.AsObject()))&&JsonNode.DeepEquals(OperationPlanStore.SourceObservations(source),OperationPlanStore.SourceObservations(levels["after"]!.AsObject())),"owned_level_move_level_context_differs");
        var result=request.DeepClone().AsObject();result["levelInspectionRequestId"]=id;result["levelInspectionSha256"]=FileArtifacts.Sha256(raw);return result;
    }
    private JsonObject Packet(JsonObject result,JsonObject args)
    {
        var id=Text(result,"ownedLevelMoveId");var sha=Text(result,"ownedLevelMoveSha256");Need(FileArtifacts.Token(id)&&FileArtifacts.Token(sha,64)&&Text(result,"bridgeRequestId")==id&&Text(result,"state")=="owned_level_move_prepared_observed");
        foreach(var flag in new[]{"executionEnabled","nativeProviderQualified","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})Need(ArtifactProof.Boolean(result[flag],false));
        var packet=ArtifactProof.Verified(root,"ownedlevelmoves",id,sha,16000000);
        Need(ArtifactProof.Fields(packet,"schemaVersion","ownedLevelMoveId","operation","preparedUtc","expiresUtc","executionEnabled","nativeProviderQualified","arguments","copyId","ownershipSha256","workingSha256","entityKeys","sourceLevelNumber","nativeObservedPropertiesSha256","windowContextSha256","requiredBeforeExecution")&&ArtifactProof.IntegerIs(packet["schemaVersion"],1)&&Text(packet,"ownedLevelMoveId")==id&&Text(packet,"operation")=="move_entity_to_level");
        Need(JsonNode.DeepEquals(packet,result["ownedLevelMove"])&&JsonNode.DeepEquals(packet["arguments"],args));
        Need(ArtifactProof.Boolean(packet["executionEnabled"],false)&&ArtifactProof.Boolean(packet["nativeProviderQualified"],false));
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(args,"bindingId"),Text(args,"bindingSha256"),65536);
        foreach(var field in new[]{"copyId","ownershipSha256","windowContextSha256"})Need(JsonNode.DeepEquals(packet[field],binding[field]));
        Need(JsonNode.DeepEquals(packet["nativeObservedPropertiesSha256"],binding["observedPropertiesSha256"]));
        var set=OperationPlanStore.ReadSet(root,new(){["entitySetId"]=args["entitySetId"]!.DeepClone(),["entitySetSha256"]=args["entitySetSha256"]!.DeepClone()},LeaseTime.PythonPrecision(utcNow()));
        Need(JsonNode.DeepEquals(set.Plan["entityKeys"],packet["entityKeys"])&&set.Plan["entityKeys"]!.AsArray().Count==1&&ArtifactProof.IntegerIs(packet["entityKeys"]![0]![0],40));
        var entity=set.Source["entities"]!.AsArray().Single(item=>JsonNode.DeepEquals(item!["nativeTypeCode"],packet["entityKeys"]![0]![0])&&JsonNode.DeepEquals(item["nativeId"],packet["entityKeys"]![0]![1]));
        Need(JsonNode.DeepEquals(entity!["levelNumber"],packet["sourceLevelNumber"])&&!JsonNode.DeepEquals(packet["sourceLevelNumber"],args["parameters"]!["targetLevelNumber"]));
        var expiry=LeaseTime.Parse(Text(packet,"expiresUtc"),Code);Need(expiry==new[]{set.Expiry,LeaseTime.Parse(Text(binding,"expiresUtc"),Code)}.Min()&&expiry>LeaseTime.PythonPrecision(utcNow()));_=LeaseTime.Remaining(Text(packet,"preparedUtc"),utcNow(),Code);
        var requirements=new JsonArray("qualified_set_items_level_provider","fresh_owned_context_and_composite_mapping","once_claim_and_mutation_fence","independent_level_and_geometry_verifier","saved_result_reopen","recovery_fault_matrix");Need(JsonNode.DeepEquals(packet["requiredBeforeExecution"],requirements));
        var journal=ArtifactProof.Journal(root,id);Need(Text(journal,"state")=="prepared"&&ArtifactProof.IntegerIs(journal["eventCount"],1)&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&Text(journal,"planSha256")==sha);
        var levels=ArtifactProof.Verified(root,"responses",Text(args,"levelInspectionRequestId"),Text(args,"levelInspectionSha256"),16000000);Need(JsonNode.DeepEquals(AddLevelReceipt(args,levels),args));
        var target=args["parameters"]!["targetLevelNumber"]!.GetValue<int>();var row=levels["levelSlots"]![target]!.AsObject();Need(JsonNode.DeepEquals(row["name"],args["parameters"]!["targetLevelName"])&&JsonNode.DeepEquals(row["activeRaw"],args["parameters"]!["targetActiveRaw"]));
        Ownership(packet,set.Source);
        return packet;
    }
    private void Ownership(JsonObject packet,JsonObject source)
    {
        var id=Text(packet,"copyId");Need(FileArtifacts.Token(id)&&FileArtifacts.Token(Text(packet,"ownershipSha256"),64));
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",id,"ownership.json"),65536);Need(FileArtifacts.Sha256(raw)==Text(packet,"ownershipSha256"));var owner=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        Need(ArtifactProof.Fields(owner,"schemaVersion","copyId","bridgeVersion","createdUtc","scope","executionEnabled","source","working","checkpoint")&&(ArtifactProof.IntegerIs(owner["schemaVersion"],1)||ArtifactProof.IntegerIs(owner["schemaVersion"],2))&&Text(owner,"copyId")==id&&Text(owner,"scope")=="saved_disk_bytes_only"&&ArtifactProof.Boolean(owner["executionEnabled"],false));
        var work=owner["working"]!.AsObject();var checkpoint=owner["checkpoint"]!.AsObject();var original=owner["source"]!.AsObject();
        var path=FileArtifacts.FixedPath(root,"workingcopies",id,ArtifactProof.IntegerIs(owner["schemaVersion"],1)?"Working.psmodel":"Working_"+id+".psmodel");var cp=FileArtifacts.FixedPath(root,"workingcopies",id,"checkpoint","Before.psmodel");
        bool Same(string a,string b)=>ObservationPolicy.NormalizePath(a)==ObservationPolicy.NormalizePath(b);
        Need(Same(path,Text(work,"path"))&&Same(cp,Text(checkpoint,"path"))&&Same(path,Text(source["model"]!.AsObject(),"path"))&&!Same(path,Text(original,"path"))&&!Same(cp,Text(original,"path")));
        var checkpointSha=Text(checkpoint,"sha256");Need(FileArtifacts.Token(checkpointSha,64)&&Text(original,"sha256")==checkpointSha&&Text(work,"initialSha256")==checkpointSha&&(File.GetAttributes(cp)&FileAttributes.ReadOnly)!=0);
        long Size(JsonObject value){var n=JsonSerializer.SerializeToElement(value["length"]);Need(n.ValueKind==JsonValueKind.Number&&n.TryGetInt64(out var size)&&size is >0 and <=1073741824);return n.GetInt64();}
        Need(Size(original)==Size(checkpoint)&&Size(work)==Size(checkpoint)&&new FileInfo(cp).Length==Size(checkpoint));
        string Hash(string file,bool mutable){using var input=new FileStream(file,FileMode.Open,FileAccess.Read,mutable?FileShare.ReadWrite:FileShare.Read);Need(input.Length is >0 and <=1073741824);return Convert.ToHexStringLower(SHA256.HashData(input));}
        Need(Hash(cp,false)==checkpointSha&&Hash(path,true)==Text(packet,"workingSha256"));
    }
    public JsonObject Issue(JsonObject result,JsonObject args,string set,string binding)
    {
        var source=Sources(set,binding,args["parameters"]!.AsObject());foreach(var pair in source)Need(JsonNode.DeepEquals(pair.Value,args[pair.Key]));
        var packet=Packet(result,args);var seconds=new[]{candidates.SetRemaining(set),bindings.Remaining(binding),(LeaseTime.Parse(Text(packet,"expiresUtc"),Code)-LeaseTime.PythonPrecision(utcNow())).TotalSeconds}.Min();Need(seconds>0);
        if(plans.Count>=128)throw new ReferenceRejected("owned_level_move_handle_limit");var handle=Guid.NewGuid().ToString("N");var reference=new JsonObject{["ownedLevelMoveId"]=result["ownedLevelMoveId"]!.DeepClone(),["ownedLevelMoveSha256"]=result["ownedLevelMoveSha256"]!.DeepClone()};
        plans[handle]=new(set,binding,args.DeepClone().AsObject(),reference,now()+seconds);var output=result.DeepClone().AsObject();output["ownedLevelMoveHandle"]=handle;output["levelMoveLifetimeSeconds"]=(int)seconds;return output;
    }
    public (string ExecutionHandle,JsonObject Parameters) Consume(string handle)
    {
        Need(plans.TryGetValue(handle,out var lease)&&lease.Expires>now(),"owned_level_move_handle_unknown_or_expired");
        var current=Sources(lease!.Set,lease.Binding,lease.Args["parameters"]!.AsObject());foreach(var pair in current)Need(JsonNode.DeepEquals(pair.Value,lease.Args[pair.Key]));
        var packet=ArtifactProof.Verified(root,"ownedlevelmoves",Text(lease.Reference,"ownedLevelMoveId"),Text(lease.Reference,"ownedLevelMoveSha256"),16000000);
        Packet(new(){["state"]="owned_level_move_prepared_observed",["bridgeRequestId"]=lease.Reference["ownedLevelMoveId"]!.DeepClone(),["ownedLevelMoveId"]=lease.Reference["ownedLevelMoveId"]!.DeepClone(),["ownedLevelMoveSha256"]=lease.Reference["ownedLevelMoveSha256"]!.DeepClone(),["ownedLevelMove"]=packet,["executionEnabled"]=false,["nativeProviderQualified"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false},lease.Args);
        Need(executions.Count<128,"owned_level_move_execution_limit");plans.Remove(handle);var execution=Guid.NewGuid().ToString("N");executions[execution]=new(lease.Reference.DeepClone().AsObject());return(execution,lease.Reference.DeepClone().AsObject());
    }
    public void Record(string handle,JsonObject response)
    {
        var record=executions[handle];if(response["bridgeRequestId"] is JsonValue token&&token.TryGetValue<string>(out var id)&&FileArtifacts.Token(id))record.Request=id;
        if(record.Request is not null&&File.Exists(FileArtifacts.FixedPath(root,"responses",record.Request+".json")))record.Sha=FileArtifacts.Sha256(FileArtifacts.ReadBytes(root,Path.Combine("responses",record.Request+".json"),16000000));
    }
    public JsonObject AdoptExecution(string request,string receiptSha)
    {
        Need(FileArtifacts.Token(request)&&FileArtifacts.Token(receiptSha,64),"owned_level_move_adoption_arguments_invalid");
        Need(executions.Count<128,"owned_level_move_execution_limit");
        var receipt=ArtifactProof.Verified(root,"responses",request,receiptSha,16000000);
        Need(receipt["plan"] is JsonObject);var plan=receipt["plan"]!.AsObject();var reference=new JsonObject();
        foreach(var field in new[]{"ownedLevelMoveId","ownedLevelMoveSha256"})reference[field]=Text(plan,field);
        Need(FileArtifacts.Token(Text(reference,"ownedLevelMoveId"))&&FileArtifacts.Token(Text(reference,"ownedLevelMoveSha256"),64));
        var packet=ArtifactProof.Verified(root,"ownedlevelmoves",Text(reference,"ownedLevelMoveId"),Text(reference,"ownedLevelMoveSha256"),16000000);
        Need(ArtifactProof.IntegerIs(packet["schemaVersion"],1)&&Text(packet,"ownedLevelMoveId")==Text(reference,"ownedLevelMoveId")&&Text(packet,"operation")=="move_entity_to_level"&&ArtifactProof.Boolean(packet["executionEnabled"],false));
        foreach(var field in new[]{"copyId","ownershipSha256","workingSha256","sourceLevelNumber","windowContextSha256"})Need(JsonNode.DeepEquals(plan[field],packet[field]));
        Need(packet["entityKeys"] is JsonArray keys&&keys.Count==1&&ArtifactProof.IntegerIs(keys[0]![0],40)&&JsonNode.DeepEquals(keys[0]![1],plan["nativeId"]));
        var definition=Parameters(packet["arguments"]!["parameters"]!.AsObject());Need(JsonNode.DeepEquals(definition["targetLevelNumber"],plan["targetLevelNumber"])&&JsonNode.DeepEquals(packet["arguments"]!["bindingId"],plan["bindingId"]));
        Ownership(packet,plan["before"]!.AsObject());
        // No prepared lease is revived. Only the verification dictionary gets a handle.
        var handle=Guid.NewGuid().ToString("N");executions[handle]=new(reference){Request=request,Sha=receiptSha};
        try
        {
            _=ExecutionProof(handle);
            return new(){["state"]="owned_level_move_execution_adopted_for_verification",["levelMoveExecutionHandle"]=handle,["executionRequestId"]=request,["executionReceiptSha256"]=receiptSha,
                ["verificationOnly"]=true,["executionAuthorized"]=false,["retryAllowed"]=false,["cadMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};
        }
        catch{executions.Remove(handle);throw;}
    }
    public JsonObject ExecutionProof(string handle)
    {
        Need(executions.TryGetValue(handle,out var entry)&&entry.Request is not null,"owned_level_move_execution_unknown");var record=entry!;var bytes=FileArtifacts.ReadBytes(root,Path.Combine("responses",record.Request+".json"),16000000);var sha=FileArtifacts.Sha256(bytes);Need(record.Sha is null||record.Sha==sha);record.Sha=sha;
        var receipt=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();Need(Text(receipt,"operationId")==record.Request&&receipt["plan"] is JsonObject);
        foreach(var pair in record.Reference)Need(JsonNode.DeepEquals(pair.Value,receipt["plan"]![pair.Key]));
        Need(Text(receipt,"state") is "owned_level_move_executed_unverified" or "owned_level_move_outcome_unknown");
        Need(ArtifactProof.Boolean(receipt["cadMutationAttempted"],true)&&ArtifactProof.Boolean(receipt["contextChangeAttempted"],true)&&ArtifactProof.Boolean(receipt["cadSaveAttempted"],false)&&ArtifactProof.IntegerIs(receipt["returnCode"],1)&&Text(receipt,"nativeReturnType")=="System.Boolean");
        var plan=ArtifactProof.Verified(root,"levelmoveexecutions",record.Request!,Text(receipt,"planSha256"),16000000);Need(JsonNode.DeepEquals(plan,receipt["plan"])&&Text(plan,"operationId")==record.Request&&Text(plan,"operation")=="move_owned_solid_to_level");
        var claimRaw=FileArtifacts.ReadBytes(root,Path.Combine("ownedlevelmoves",Text(plan,"ownedLevelMoveId")+".execution-once.json"),65536);Need(FileArtifacts.Sha256(claimRaw)==Text(receipt,"claimSha256"));var claim=JsonNode.Parse(FileArtifacts.Parse(claimRaw).GetRawText())!.AsObject();
        Need(ArtifactProof.Fields(claim,"operationId","planSha256","ownedLevelMoveId","copyId","automaticReplayForbidden")&&Text(claim,"operationId")==record.Request&&Text(claim,"planSha256")==Text(receipt,"planSha256")&&Text(claim,"ownedLevelMoveId")==Text(plan,"ownedLevelMoveId")&&Text(claim,"copyId")==Text(plan,"copyId")&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true));
        var journal=ArtifactProof.Journal(root,record.Request!);Need(Text(journal,"planSha256")==Text(receipt,"planSha256")&&ArtifactProof.Boolean(journal["writerLockPresent"],false));
        var returned=journal["events"]!.AsArray().Single(item=>item!["state"]!.GetValue<string>()=="command_returned")!["details"]!.AsObject();Need(ArtifactProof.IntegerIs(returned["returnCode"],1)&&Text(returned,"nativeReturnType")=="System.Boolean");
        return new(){["executionRequestId"]=record.Request,["executionReceiptSha256"]=sha};
    }
    public void CheckExecuted(string handle)
    {
        var proof=ExecutionProof(handle);var id=Text(proof,"executionRequestId");var receipt=ArtifactProof.Verified(root,"responses",id,Text(proof,"executionReceiptSha256"),16000000);
        Need(Text(receipt,"state")=="owned_level_move_executed_unverified"&&ArtifactProof.IntegerIs(receipt["commandCalls"],1)&&ArtifactProof.Boolean(receipt["sameWorkerPostconditionsPassed"],true)&&ArtifactProof.Boolean(receipt["copyFenced"],true)&&Text(ArtifactProof.Journal(root,id),"state")=="executed_unverified");
        var plan=receipt["plan"]!.AsObject();var fencePath=FileArtifacts.FixedPath(root,"workingcopies",Text(plan,"copyId"),"mutation-fence.json");var raw=FileArtifacts.ReadBytes(root,Path.GetRelativePath(root,fencePath),65536);Need(FileArtifacts.Sha256(raw)==Text(receipt,"executionFenceSha256"));var fence=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        Need(Text(fence,"operationId")==id&&Text(fence,"planSha256")==Text(receipt,"planSha256")&&Text(fence,"copyId")==Text(plan,"copyId")&&Text(fence,"ownershipSha256")==Text(plan,"ownershipSha256"));
    }
    public void CheckVerified(string handle,JsonObject result,JsonObject parameters)
    {
        var proof=ExecutionProof(handle);Need(JsonNode.DeepEquals(result["executionOperationId"],proof["executionRequestId"])&&Text(result,"state")=="owned_level_move_verified_observed"&&Text(result,"recordedJournalState")=="succeeded_verified"&&ArtifactProof.Boolean(result["noCommandReplay"],true)&&ArtifactProof.Boolean(result["copyFenceReleased"],true));
        foreach(var flag in new[]{"cadMutationAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})Need(ArtifactProof.Boolean(result[flag],false));
        var id=Text(proof,"executionRequestId");Need(Text(ArtifactProof.Journal(root,id),"state")=="succeeded_verified");var receipt=ArtifactProof.Verified(root,"responses",id,Text(proof,"executionReceiptSha256"),16000000);
        var fence=FileArtifacts.FixedPath(root,"workingcopies",Text(receipt["plan"]!.AsObject(),"copyId"),"mutation-fence.json");Need(!File.Exists(fence)&&!Directory.Exists(fence));
        var expected=new JsonArray();foreach(var (request,sha) in new[]{("executionRequestId","executionReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256"),("levelInspectionRequestId","levelInspectionSha256")})expected.Add(new JsonObject{["requestId"]=parameters[request]!.DeepClone(),["sha256"]=parameters[sha]!.DeepClone()});Need(JsonNode.DeepEquals(expected,result["evidence"]));
    }
}
