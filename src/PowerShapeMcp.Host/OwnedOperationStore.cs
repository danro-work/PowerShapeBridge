using System.Diagnostics;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public sealed class OwnedOperationStore(string root,OperationPlanStore plans,DocumentBindingStore bindings,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private static readonly string[] Fields=["schemaVersion","ownedOperationId","operationPlanId","operationPlanSha256","bindingId","bindingSha256","preparedUtc","expiresUtc","executionEnabled","nativeObservedPropertiesSha256","windowContextSha256","workingSha256","copyId","ownershipSha256"];
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(double Expires,string Plan,string Binding,JsonObject Source,JsonObject Parameters);
    private readonly Dictionary<string,Lease> packets=[];
    public void Clear()=>packets.Clear();
    public JsonObject PrepareParameters(string planHandle,string bindingHandle)
    {
        var local=plans.Inspect(planHandle);var bindingArgs=bindings.ValidationParameters(bindingHandle);
        var binding=ArtifactProof.Verified(root,"documentbindings",bindingArgs["bindingId"]!.GetValue<string>(),bindingArgs["bindingSha256"]!.GetValue<string>(),65536);
        var plan=local["plan"]!.AsObject();var journal=local["journal"]!.AsObject();var timestamp=LeaseTime.PythonPrecision(utcNow());
        if(journal["state"]?.GetValue<string>()!="prepared"||!ArtifactProof.Boolean(journal["writerLockPresent"],false))throw new ReferenceRejected("owned_operation_plan_journal_not_prepared");
        var entries=plan["entitySets"]!.AsArray().Select(n=>n!.AsObject()).ToArray();
        var sets=entries.Select(entry=>OperationPlanStore.ReadSet(root,new(){["entitySetId"]=entry["entitySetId"]!.DeepClone(),["entitySetSha256"]=entry["entitySetSha256"]!.DeepClone()},timestamp)).ToArray();
        OperationPlanStore.ValidateArguments(new(){["operation"]=plan["operation"]!.DeepClone(),["entitySetHandles"]=new JsonArray(entries.Select(e=>e["entitySetId"]!.DeepClone()).ToArray()),["parameters"]=plan["parameters"]!.DeepClone()});
        var observed=OperationPlanStore.SourceObservations(sets[0].Source);var identity=new JsonObject();
        foreach(var key in new[]{"processId","processStartedUtc","windowsSessionId","productVersion","model"})identity[key]=observed[key]?.DeepClone();
        var bindingSource=ArtifactProof.Verified(root,"responses",binding["inspectionRequestId"]!.GetValue<string>(),binding["inspectionSha256"]!.GetValue<string>(),16000000);
        if(!ArtifactProof.Boolean(plan["executionEnabled"],false)||!JsonNode.DeepEquals(plan["sourceIdentity"],identity)||plan["observedPropertiesSha256"]?.GetValue<string>()!=ObservationPolicy.Digest(observed)||
           !JsonNode.DeepEquals(OperationPlanStore.SourceObservations(bindingSource),observed))throw new ReferenceRejected("owned_operation_plan_binding_observations_differ");
        for(var i=0;i<entries.Length;i++)
            if(sets[i].Plan.Any(p=>!JsonNode.DeepEquals(entries[i][p.Key],p.Value))||!JsonNode.DeepEquals(OperationPlanStore.SourceObservations(sets[i].Source),observed))throw new ReferenceRejected("owned_operation_set_receipt_differs");
        if(LeaseTime.Parse(plan["expiresUtc"]!.GetValue<string>(),"operation_time_invalid")!=sets.Min(s=>s.Expiry)||!JsonNode.DeepEquals(plan["lengthUnitsRaw"],observed["lengthUnitsRaw"])||!JsonNode.DeepEquals(plan["lengthUnitFactorRaw"],observed["lengthUnitFactorRaw"]))throw new ReferenceRejected("owned_operation_plan_units_or_lifetime_differs");
        var result=new JsonObject{["operationPlanId"]=local["operationId"]!.DeepClone(),["operationPlanSha256"]=local["planSha256"]!.DeepClone()};
        foreach(var (key,value) in bindingArgs)result[key]=value?.DeepClone();return result;
    }
    public JsonObject Receipt(JsonObject result,JsonObject? parameters=null)
    {
        var id=ArtifactProof.String(result,"ownedOperationId","owned_operation_native_identity_invalid");var sha=ArtifactProof.String(result,"ownedOperationSha256","owned_operation_native_identity_invalid");
        if(!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64))throw new ReferenceRejected("owned_operation_native_identity_invalid");
        var packet=ArtifactProof.Verified(root,"ownedoperations",id,sha,65536);
        if(!ArtifactProof.Fields(packet,Fields)||!JsonNode.DeepEquals(packet,result["ownedOperation"])||!ArtifactProof.IntegerIs(packet["schemaVersion"],1)||packet["ownedOperationId"]?.GetValue<string>()!=id||!ArtifactProof.Boolean(packet["executionEnabled"],false))throw new ReferenceRejected("owned_operation_native_packet_invalid");
        foreach(var field in new[]{"nativeObservedPropertiesSha256","windowContextSha256","workingSha256","ownershipSha256"})
            if(!FileArtifacts.Token(packet[field]?.GetValue<string>(),64))throw new ReferenceRejected("owned_operation_native_proof_invalid");
        foreach(var field in new[]{"executionEnabled","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})
            if(!ArtifactProof.Boolean(result[field],false))throw new ReferenceRejected("owned_operation_native_execution_policy_differs");
        if(!JsonNode.DeepEquals(result["expiresUtc"],packet["expiresUtc"]))throw new ReferenceRejected("owned_operation_native_execution_policy_differs");
        if(parameters is not null&&parameters.Any(p=>!JsonNode.DeepEquals(packet[p.Key],p.Value)))throw new ReferenceRejected("owned_operation_native_dependencies_differ");
        foreach(var field in new[]{"operationPlanId","bindingId","copyId"})if(!FileArtifacts.Token(packet[field]?.GetValue<string>()))throw new ReferenceRejected("owned_operation_native_dependencies_invalid");
        foreach(var field in new[]{"operationPlanSha256","bindingSha256"})if(!FileArtifacts.Token(packet[field]?.GetValue<string>(),64))throw new ReferenceRejected("owned_operation_native_dependencies_invalid");
        var binding=ArtifactProof.Verified(root,"documentbindings",packet["bindingId"]!.GetValue<string>(),packet["bindingSha256"]!.GetValue<string>(),65536);
        foreach(var (target,source) in new[]{("copyId","copyId"),("ownershipSha256","ownershipSha256"),("windowContextSha256","windowContextSha256"),("nativeObservedPropertiesSha256","observedPropertiesSha256")})
            if(!JsonNode.DeepEquals(packet[target],binding[source]))throw new ReferenceRejected("owned_operation_native_binding_differs");
        var journal=ArtifactProof.Journal(root,id);
        if(journal["state"]?.GetValue<string>()!="prepared"||!ArtifactProof.Boolean(journal["writerLockPresent"],false)||!ArtifactProof.IntegerIs(journal["eventCount"],1)||journal["planSha256"]?.GetValue<string>()!=sha)throw new ReferenceRejected("owned_operation_native_journal_not_prepared");
        return packet;
    }
    public JsonObject Issue(JsonObject result,JsonObject parameters,string planHandle,string bindingHandle)
    {
        if(result["state"]?.GetValue<string>()!="owned_operation_prepared_observed"||!JsonNode.DeepEquals(result["bridgeRequestId"],result["ownedOperationId"]))throw new ReferenceRejected("owned_operation_preparation_receipt_invalid");
        if(!JsonNode.DeepEquals(PrepareParameters(planHandle,bindingHandle),parameters))throw new ReferenceRejected("owned_operation_dependency_lost_during_preparation");
        var packet=Receipt(result,parameters);
        var seconds=Math.Min((LeaseTime.Parse(packet["expiresUtc"]!.GetValue<string>(),"operation_time_invalid")-LeaseTime.PythonPrecision(utcNow())).TotalSeconds,Math.Min(plans.Remaining(planHandle),bindings.Remaining(bindingHandle)));
        if(seconds<=0||seconds>300)throw new ReferenceRejected("owned_operation_expired");
        foreach(var handle in packets.Where(p=>p.Value.Expires<=now()).Select(p=>p.Key).ToArray())packets.Remove(handle);
        if(packets.Count>=128)Clear();var token=Guid.NewGuid().ToString("N");
        packets[token]=new(now()+seconds,planHandle,bindingHandle,parameters.DeepClone().AsObject(),new(){["ownedOperationId"]=result["ownedOperationId"]!.DeepClone(),["ownedOperationSha256"]=result["ownedOperationSha256"]!.DeepClone()});
        var issued=result.DeepClone().AsObject();issued["ownedOperationHandle"]=token;issued["ownedOperationLifetimeSeconds"]=(int)seconds;return issued;
    }
    public JsonObject ValidationParameters(string handle)
    {
        if(!packets.TryGetValue(handle,out var found)||found.Expires<=now()){packets.Remove(handle);throw new ReferenceRejected("owned_operation_handle_unknown_or_expired");}
        if(!JsonNode.DeepEquals(PrepareParameters(found.Plan,found.Binding),found.Source)){Clear();throw new ReferenceRejected("owned_operation_dependencies_changed");}
        return found.Parameters.DeepClone().AsObject();
    }
    public void CheckedValidation(JsonObject result,JsonObject parameters)
    {
        if(parameters.Any(p=>!JsonNode.DeepEquals(result[p.Key],p.Value)))throw new ReferenceRejected("owned_operation_validation_identity_differs");Receipt(result);
    }
}
