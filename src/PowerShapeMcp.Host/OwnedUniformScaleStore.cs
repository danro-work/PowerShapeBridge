using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Passive native packet correlation. No execution authorization or CAD calls.
internal sealed class OwnedUniformScaleStore(string root,UniformScalePlanStore plans,DocumentBindingStore bindings,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(double Expires,string Plan,string Binding,JsonObject Source,JsonObject Reference);
    private readonly Dictionary<string,Lease> packets=[];
    private static readonly string[] Requirements=["qualified_uniform_scale_recipe","qualified_native_measurements","fresh_owned_context_guard","exact_target_set","primitive_remapping_proof","once_claim_and_mutation_fence","independent_postconditions","saved_result_reopen","recovery_fault_matrix"];
    private static string Text(JsonObject data,string key)=>ArtifactProof.String(data,key,"owned_scale_proof_invalid");
    private static void Require(bool value,string error){if(!value)throw new ReferenceRejected(error);}
    public void Clear()=>packets.Clear();
    public void Revoke(string handle)=>packets.Remove(handle);
    public JsonObject PrepareParameters(string planHandle,string bindingHandle)
    {
        var plan=plans.Inspect(planHandle);var definition=UniformScaleDefinition.Read(plan["parameters"]!.AsObject());
        var bindingArgs=bindings.ValidationParameters(bindingHandle);
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(bindingArgs,"bindingId"),Text(bindingArgs,"bindingSha256"),65536);
        bindings.VerifyReceipt(new(){["bindingId"]=bindingArgs["bindingId"]!.DeepClone(),["bindingSha256"]=bindingArgs["bindingSha256"]!.DeepClone(),["binding"]=binding.DeepClone()});
        var source=OperationPlanStore.ReadSet(root,plan["entitySet"]!.AsObject(),LeaseTime.PythonPrecision(utcNow()));
        var bound=ArtifactProof.Verified(root,"responses",Text(binding,"inspectionRequestId"),Text(binding,"inspectionSha256"),16000000);
        Require(JsonNode.DeepEquals(OperationPlanStore.SourceObservations(source.Source),OperationPlanStore.SourceObservations(bound)),"owned_scale_plan_binding_observations_differ");
        return new(){["entitySetId"]=plan["entitySet"]!["entitySetId"]!.DeepClone(),["entitySetSha256"]=plan["entitySet"]!["entitySetSha256"]!.DeepClone(),
            ["bindingId"]=bindingArgs["bindingId"]!.DeepClone(),["bindingSha256"]=bindingArgs["bindingSha256"]!.DeepClone(),["parameters"]=definition.Parameters()};
    }
    private static double Length(JsonObject value,string field)
    {
        var node=value[field];Require(node is not null,"owned_scale_owned_size_invalid");
        var raw=System.Text.Json.JsonSerializer.SerializeToElement(node);
        Require(raw.ValueKind==System.Text.Json.JsonValueKind.Number&&raw.TryGetInt64(out var n)&&n>0&&n<=1073741824,"owned_scale_owned_size_invalid");
        return raw.GetInt64();
    }
    private static string HashHeld(string path,out long length)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        length=file.Length;Require(length>0&&length<=1073741824,"owned_scale_owned_size_invalid");
        var sha=Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
        Require(file.Length==length,"owned_scale_owned_bytes_changed");return sha;
    }
    private void Ownership(JsonObject packet,JsonObject source)
    {
        var id=Text(packet,"copyId");var raw=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",id,"ownership.json"),65536);
        Require(FileArtifacts.Sha256(raw)==Text(packet,"ownershipSha256"),"owned_scale_ownership_sha_differs");
        var owner=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        Require(ArtifactProof.Fields(owner,"schemaVersion","copyId","bridgeVersion","createdUtc","scope","executionEnabled","source","working","checkpoint")&&
            (ArtifactProof.IntegerIs(owner["schemaVersion"],1)||ArtifactProof.IntegerIs(owner["schemaVersion"],2))&&Text(owner,"copyId")==id&&
            Text(owner,"scope")=="saved_disk_bytes_only"&&ArtifactProof.Boolean(owner["executionEnabled"],false),"owned_scale_ownership_invalid");
        var original=owner["source"]!.AsObject();var work=owner["working"]!.AsObject();var checkpoint=owner["checkpoint"]!.AsObject();
        var workName=ArtifactProof.IntegerIs(owner["schemaVersion"],1)?"Working.psmodel":"Working_"+id+".psmodel";
        var workPath=FileArtifacts.FixedPath(root,"workingcopies",id,workName);var checkpointPath=FileArtifacts.FixedPath(root,"workingcopies",id,"checkpoint","Before.psmodel");
        bool Same(string a,string b)=>string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
        Require(Same(Text(work,"path"),workPath)&&Same(Text(checkpoint,"path"),checkpointPath)&&Same(Text(source["model"]!.AsObject(),"path"),workPath)&&
            !Same(Text(original,"path"),workPath)&&!Same(Text(original,"path"),checkpointPath),"owned_scale_owned_path_differs");
        var sourceSha=Text(original,"sha256");Require(FileArtifacts.Token(sourceSha,64)&&Text(work,"initialSha256")==sourceSha&&Text(checkpoint,"sha256")==sourceSha&&
            Length(original,"length")==Length(work,"length")&&Length(original,"length")==Length(checkpoint,"length"),"owned_scale_initial_owned_bytes_differ");
        Require((File.GetAttributes(checkpointPath)&FileAttributes.ReadOnly)!=0,"owned_scale_checkpoint_writable");
        Require(HashHeld(workPath,out _)==Text(packet,"workingSha256")&&HashHeld(checkpointPath,out var cpLength)==sourceSha&&cpLength==Length(checkpoint,"length"),"owned_scale_owned_bytes_differ");
        foreach(var name in new[]{"mutation-fence.json","geometry-export-fence.json"}) {
            var path=FileArtifacts.FixedPath(root,"workingcopies",id,name);Require(!File.Exists(path)&&!Directory.Exists(path),"owned_scale_copy_fenced");
        }
        // Native file identity/retained CAD guard remains native responsibility.
        // These reads validate bytes/layout, never authorize later mutation.
    }
    public JsonObject Receipt(JsonObject result,JsonObject parameters)
    {
        var id=Text(result,"ownedScaleId");var sha=Text(result,"ownedScaleSha256");
        Require(FileArtifacts.Token(id)&&FileArtifacts.Token(sha,64)&&Text(result,"bridgeRequestId")==id&&Text(result,"state")=="owned_uniform_scale_prepared_observed","owned_scale_native_identity_invalid");
        var packet=ArtifactProof.Verified(root,"ownedscales",id,sha,65536);
        Require(ArtifactProof.Fields(packet,"entitySetId","entitySetSha256","bindingId","bindingSha256","parameters","schemaVersion","ownedScaleId","operation","preparedUtc","expiresUtc","executionEnabled", "copyId","ownershipSha256","workingSha256","entityKeys","setInspectionRequestId","setInspectionSha256","bindingInspectionRequestId","bindingInspectionSha256","nativeObservedPropertiesSha256","windowContextSha256","lengthUnitsRaw","lengthUnitFactorRaw","nativeProviderQualified","requiredBeforeExecution")&&
            JsonNode.DeepEquals(packet,result["ownedScale"])&&ArtifactProof.IntegerIs(packet["schemaVersion"],1)&&Text(packet,"ownedScaleId")==id&&Text(packet,"operation")=="uniform_scale_entities"&&
            parameters.All(p=>JsonNode.DeepEquals(p.Value,packet[p.Key])),"owned_scale_native_packet_differs");
        UniformScaleDefinition.Read(packet["parameters"]!.AsObject());
        foreach(var flag in new[]{"executionEnabled","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","nativeProviderQualified"})Require(ArtifactProof.Boolean(result[flag],false),"owned_scale_action_policy_invalid");
        Require(ArtifactProof.Boolean(packet["executionEnabled"],false)&&ArtifactProof.Boolean(packet["nativeProviderQualified"],false)&&JsonNode.DeepEquals(result["expiresUtc"],packet["expiresUtc"])&&
            ArtifactProof.IntegerIs(result["guardPasses"],2)&&ArtifactProof.IntegerIs(result["sameContextObservationReads"],4)&&
            JsonNode.DeepEquals(packet["requiredBeforeExecution"],new JsonArray(Requirements.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray())),"owned_scale_qualification_policy_invalid");
        foreach(var field in new[]{"copyId","setInspectionRequestId","bindingInspectionRequestId"})Require(FileArtifacts.Token(Text(packet,field)),"owned_scale_identity_invalid");
        foreach(var field in new[]{"workingSha256","ownershipSha256","setInspectionSha256","bindingInspectionSha256","nativeObservedPropertiesSha256","windowContextSha256"})Require(FileArtifacts.Token(Text(packet,field),64),"owned_scale_digest_invalid");
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(packet,"bindingId"),Text(packet,"bindingSha256"),65536);
        foreach(var pair in new[]{("copyId","copyId"),("ownershipSha256","ownershipSha256"),("windowContextSha256","windowContextSha256"),("nativeObservedPropertiesSha256","observedPropertiesSha256"),("bindingInspectionRequestId","inspectionRequestId"),("bindingInspectionSha256","inspectionSha256")})Require(JsonNode.DeepEquals(packet[pair.Item1],binding[pair.Item2]),"owned_scale_binding_differs");
        var set=OperationPlanStore.ReadSet(root,new(){["entitySetId"]=packet["entitySetId"]!.DeepClone(),["entitySetSha256"]=packet["entitySetSha256"]!.DeepClone()},LeaseTime.PythonPrecision(utcNow()));
        Require(JsonNode.DeepEquals(set.Plan["entityKeys"],packet["entityKeys"])&&Text(set.Plan,"inspectionRequestId")==Text(packet,"setInspectionRequestId")&&Text(set.Plan,"inspectionSha256")==Text(packet,"setInspectionSha256"),"owned_scale_set_differs");
        var source=ArtifactProof.Verified(root,"responses",Text(packet,"bindingInspectionRequestId"),Text(packet,"bindingInspectionSha256"),16000000);
        Require(JsonNode.DeepEquals(OperationPlanStore.SourceObservations(source),OperationPlanStore.SourceObservations(set.Source))&&
            JsonNode.DeepEquals(source["lengthUnitsRaw"],packet["lengthUnitsRaw"])&&JsonNode.DeepEquals(source["lengthUnitFactorRaw"],packet["lengthUnitFactorRaw"]),"owned_scale_source_units_differ");
        var journal=ArtifactProof.Journal(root,id);
        Require(journal["state"]?.GetValue<string>()=="prepared"&&ArtifactProof.IntegerIs(journal["eventCount"],1)&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&Text(journal,"planSha256")==sha,"owned_scale_journal_not_prepared");
        Ownership(packet,source);return packet;
    }
    public JsonObject Issue(JsonObject result,JsonObject parameters,string planHandle,string bindingHandle)
    {
        Require(JsonNode.DeepEquals(PrepareParameters(planHandle,bindingHandle),parameters),"owned_scale_dependency_changed");
        var packet=Receipt(result,parameters);var plan=plans.Inspect(planHandle);
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(parameters,"bindingId"),Text(parameters,"bindingSha256"),65536);
        var expiry=LeaseTime.Parse(Text(packet,"expiresUtc"),"owned_scale_lifetime_invalid");var prepared=LeaseTime.Parse(Text(packet,"preparedUtc"),"owned_scale_lifetime_invalid");
        var minimum=new[]{LeaseTime.Parse(Text(plan,"expiresUtc"),"owned_scale_lifetime_invalid"),LeaseTime.Parse(Text(binding,"expiresUtc"),"owned_scale_lifetime_invalid")}.Min();
        var source=ArtifactProof.Verified(root,"responses",Text(packet,"bindingInspectionRequestId"),Text(packet,"bindingInspectionSha256"),16000000);
        Require(expiry==minimum&&prepared>=LeaseTime.Parse(Text(source,"timestampUtc"),"owned_scale_lifetime_invalid")&&prepared<=LeaseTime.PythonPrecision(utcNow()).AddSeconds(5)&&prepared<expiry,"owned_scale_lifetime_differs");
        var remaining=Math.Min((expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds,Math.Min(plans.Remaining(planHandle),bindings.Remaining(bindingHandle)));
        Require(remaining>0&&remaining<=300,"owned_scale_expired");
        foreach(var stale in packets.Where(p=>p.Value.Expires<=now()).Select(p=>p.Key).ToArray())packets.Remove(stale);
        Require(packets.Count<128,"owned_scale_handle_limit");var handle=Guid.NewGuid().ToString("N");
        packets[handle]=new(now()+remaining,planHandle,bindingHandle,parameters.DeepClone().AsObject(),new(){["ownedScaleId"]=packet["ownedScaleId"]!.DeepClone(),["ownedScaleSha256"]=result["ownedScaleSha256"]!.DeepClone()});
        var output=result.DeepClone().AsObject();output["ownedScaleHandle"]=handle;output["ownedScaleLifetimeSeconds"]=(int)remaining;return output;
    }
    public JsonObject ValidationParameters(string handle)
    {
        Require(packets.TryGetValue(handle,out var lease)&&lease.Expires>now(),"owned_scale_handle_unknown_or_expired");
        Require(JsonNode.DeepEquals(PrepareParameters(lease!.Plan,lease.Binding),lease.Source),"owned_scale_dependency_changed");
        var packet=ArtifactProof.Verified(root,"ownedscales",Text(lease.Reference,"ownedScaleId"),Text(lease.Reference,"ownedScaleSha256"),65536);
        Receipt(new(){["ownedScaleId"]=lease.Reference["ownedScaleId"]!.DeepClone(),["ownedScaleSha256"]=lease.Reference["ownedScaleSha256"]!.DeepClone(),["ownedScale"]=packet,["bridgeRequestId"]=lease.Reference["ownedScaleId"]!.DeepClone(),["state"]="owned_uniform_scale_prepared_observed",["expiresUtc"]=packet["expiresUtc"]!.DeepClone(),["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,["nativeProviderQualified"]=false,["guardPasses"]=2,["sameContextObservationReads"]=4},lease.Source);
        return lease.Reference.DeepClone().AsObject();
    }
}
