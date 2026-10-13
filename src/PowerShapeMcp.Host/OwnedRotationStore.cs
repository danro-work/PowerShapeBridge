using System.Diagnostics;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public sealed class OwnedRotationStore(string root,RotationPlanStore plans,DocumentBindingStore bindings,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(double Expires,string Plan,string Binding,JsonObject Source,JsonObject Parameters);
    private readonly Dictionary<string,Lease> packets=[];
    public void Clear()=>packets.Clear();
    public void Revoke(string handle)=>packets.Remove(handle);
    private static string Text(JsonObject data,string key)=>ArtifactProof.String(data,key,"owned_rotation_proof_invalid");
    private static void Require(bool condition,string code){if(!condition)throw new ReferenceRejected(code);}
    public JsonObject PrepareParameters(string planHandle,string bindingHandle)
    {
        var plan=plans.Inspect(planHandle);RotationDefinition.Read(plan["parameters"]!.AsObject());
        var bindingArgs=bindings.ValidationParameters(bindingHandle);
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(bindingArgs,"bindingId"),Text(bindingArgs,"bindingSha256"),65536);
        var source=OperationPlanStore.ReadSet(root,plan["entitySet"]!.AsObject(),LeaseTime.PythonPrecision(utcNow()));
        var bound=ArtifactProof.Verified(root,"responses",Text(binding,"inspectionRequestId"),Text(binding,"inspectionSha256"),16000000);
        Require(JsonNode.DeepEquals(OperationPlanStore.SourceObservations(source.Source),OperationPlanStore.SourceObservations(bound)),"owned_rotation_plan_binding_observations_differ");
        var journal=ArtifactProof.Journal(root,Text(plan,"operationId"));
        Require(journal["state"]?.GetValue<string>()=="prepared"&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&ArtifactProof.IntegerIs(journal["eventCount"],1),"owned_rotation_plan_not_prepared");
        var reference=plans.ReferenceParameters(planHandle);
        return new(){["rotationPlanId"]=reference["rotationPlanId"]!.DeepClone(),["rotationPlanSha256"]=reference["rotationPlanSha256"]!.DeepClone(),["bindingId"]=bindingArgs["bindingId"]!.DeepClone(),["bindingSha256"]=bindingArgs["bindingSha256"]!.DeepClone()};
    }
    public JsonObject Receipt(JsonObject result,JsonObject parameters)
    {
        var id=Text(result,"ownedRotationId");var sha=Text(result,"ownedRotationSha256");
        Require(FileArtifacts.Token(id)&&FileArtifacts.Token(sha,64)&&Text(result,"bridgeRequestId")==id&&result["state"]?.GetValue<string>()=="owned_rotation_prepared_observed","owned_rotation_native_identity_invalid");
        var packet=ArtifactProof.Verified(root,"ownedrotations",id,sha,65536);
        Require(ArtifactProof.Fields(packet,"rotationPlanId","rotationPlanSha256","bindingId","bindingSha256","schemaVersion","ownedRotationId","preparedUtc","expiresUtc","executionEnabled","nativeObservedPropertiesSha256","windowContextSha256","workingSha256","copyId","ownershipSha256")&&
            JsonNode.DeepEquals(packet,result["ownedRotation"])&&ArtifactProof.IntegerIs(packet["schemaVersion"],1)&&Text(packet,"ownedRotationId")==id&&parameters.All(pair=>JsonNode.DeepEquals(packet[pair.Key],pair.Value)),"owned_rotation_native_packet_differs");
        foreach(var flag in new[]{"executionEnabled","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})Require(ArtifactProof.Boolean(result[flag],false),"owned_rotation_execution_policy_invalid");
        Require(ArtifactProof.Boolean(packet["executionEnabled"],false)&&JsonNode.DeepEquals(result["expiresUtc"],packet["expiresUtc"]),"owned_rotation_execution_policy_invalid");
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(packet,"bindingId"),Text(packet,"bindingSha256"),65536);
        foreach(var pair in new[]{("copyId","copyId"),("ownershipSha256","ownershipSha256"),("windowContextSha256","windowContextSha256"),("nativeObservedPropertiesSha256","observedPropertiesSha256")})Require(JsonNode.DeepEquals(packet[pair.Item1],binding[pair.Item2]),"owned_rotation_binding_differs");
        foreach(var field in new[]{"workingSha256","ownershipSha256","windowContextSha256","nativeObservedPropertiesSha256"})Require(FileArtifacts.Token(Text(packet,field),64),"owned_rotation_digest_invalid");
        var journal=ArtifactProof.Journal(root,id);
        Require(journal["state"]?.GetValue<string>()=="prepared"&&ArtifactProof.IntegerIs(journal["eventCount"],1)&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&Text(journal,"planSha256")==sha,"owned_rotation_journal_not_prepared");
        return packet;
    }
    public JsonObject Issue(JsonObject result,JsonObject parameters,string planHandle,string bindingHandle)
    {
        Require(JsonNode.DeepEquals(PrepareParameters(planHandle,bindingHandle),parameters),"owned_rotation_dependency_changed");
        var packet=Receipt(result,parameters);
        var expiry=LeaseTime.Parse(Text(packet,"expiresUtc"),"owned_rotation_expired");var prepared=LeaseTime.Parse(Text(packet,"preparedUtc"),"owned_rotation_expired");
        var plan=plans.Inspect(planHandle);var binding=ArtifactProof.Verified(root,"documentbindings",Text(parameters,"bindingId"),Text(parameters,"bindingSha256"),65536);
        var minimum=new[]{LeaseTime.Parse(Text(plan,"expiresUtc"),"owned_rotation_expired"),LeaseTime.Parse(Text(binding,"expiresUtc"),"owned_rotation_expired")}.Min();
        Require(expiry==minimum&&prepared<=LeaseTime.PythonPrecision(utcNow()).AddSeconds(5),"owned_rotation_lifetime_differs");
        var remaining=Math.Min((expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds,Math.Min(plans.Remaining(planHandle),bindings.Remaining(bindingHandle)));
        Require(remaining>0&&remaining<=300,"owned_rotation_expired");
        foreach(var expired in packets.Where(pair=>pair.Value.Expires<=now()).Select(pair=>pair.Key).ToArray())packets.Remove(expired);
        Require(packets.Count<128,"owned_rotation_handle_limit");var handle=Guid.NewGuid().ToString("N");
        packets[handle]=new(now()+remaining,planHandle,bindingHandle,parameters.DeepClone().AsObject(),new(){["ownedRotationId"]=id(result),["ownedRotationSha256"]=result["ownedRotationSha256"]!.DeepClone()});
        var output=result.DeepClone().AsObject();output["ownedRotationHandle"]=handle;output["ownedRotationLifetimeSeconds"]=(int)remaining;return output;
    }
    private static JsonNode id(JsonObject data)=>data["ownedRotationId"]!.DeepClone();
    public JsonObject ValidationParameters(string handle)
    {
        Require(packets.TryGetValue(handle,out var lease)&&lease.Expires>now(),"owned_rotation_handle_unknown_or_expired");
        Require(JsonNode.DeepEquals(PrepareParameters(lease!.Plan,lease.Binding),lease.Source),"owned_rotation_dependency_changed");return lease.Parameters.DeepClone().AsObject();
    }
}
