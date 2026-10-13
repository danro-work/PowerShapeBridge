using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public sealed record RotationDefinition(int AngleDegrees,double[] Origin,double[] UnitDirection)
{
    public static RotationDefinition Read(JsonObject value)
    {
        const string error="rotation_parameters_invalid";
        if(!ArtifactProof.Fields(value,"angleDegrees","origin","direction","coordinateFrame","lengthUnits")||
           ArtifactProof.String(value,"coordinateFrame",error)!="world"||ArtifactProof.String(value,"lengthUnits",error)!="document_raw")throw new ReferenceRejected(error);
        var angle=Number(value["angleDegrees"]);
        if(angle!=Math.Truncate(angle))throw new ReferenceRejected("rotation_fractional_angle_not_qualified");
        if(Math.Abs(angle)>1000000||angle%360==0)throw new ReferenceRejected("rotation_angle_invalid_or_noop");
        var origin=Vector(value["origin"]);var direction=Vector(value["direction"]);
        var maximum=direction.Max(Math.Abs);if(maximum==0)throw new ReferenceRejected("rotation_axis_zero");
        // Scale first: avoid overflow/underflow in the Euclidean norm.
        var scaled=direction.Select(component=>component/maximum).ToArray();var norm=Math.Sqrt(scaled.Sum(component=>component*component));
        return new((int)angle,origin,scaled.Select(component=>component/norm).ToArray());
    }
    private static double Number(JsonNode? value)
    {
        if(value is null)throw new ReferenceRejected("rotation_number_invalid");
        var raw=JsonSerializer.SerializeToElement(value);
        if(raw.ValueKind!=JsonValueKind.Number||!raw.TryGetDouble(out var number)||!double.IsFinite(number))throw new ReferenceRejected("rotation_number_invalid");
        return number;
    }
    private static double[] Vector(JsonNode? value)
    {
        if(value is not JsonArray array||array.Count!=3)throw new ReferenceRejected("rotation_vector_invalid");
        var result=array.Select(Number).ToArray();if(result.Any(component=>Math.Abs(component)>1000000))throw new ReferenceRejected("rotation_vector_out_of_range");return result;
    }
    public JsonArray NativeAxis()=>new(Origin.Concat(UnitDirection).Select(component=>(JsonNode?)JsonValue.Create(component)).ToArray());
}

// A passive K8 plan. It grants no native execution/binding authority.
public sealed class RotationPlanStore(string root,CandidateStore candidates,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(string Id,string Sha,string Set,(string Identity,string Digest) Context,double Expires);
    private readonly Dictionary<string,Lease> plans=[];
    public void Clear()=>plans.Clear();
    public void Revoke(string handle)=>plans.Remove(handle);
    public JsonObject Prepare(string setHandle,JsonObject parameters)
    {
        foreach(var expired in plans.Where(pair=>pair.Value.Expires<=now()).Select(pair=>pair.Key).ToArray())plans.Remove(expired);
        if(plans.Count>=128)throw new ReferenceRejected("rotation_plan_limit");
        var definition=RotationDefinition.Read(parameters);var timestamp=LeaseTime.PythonPrecision(utcNow());
        var setParameters=candidates.SetParameters(setHandle);var set=OperationPlanStore.ReadSet(root,setParameters,timestamp);
        if(candidates.Context!=set.Context)throw new ReferenceRejected("rotation_source_context_changed");
        var id=Guid.NewGuid().ToString("N");var plan=new JsonObject
        {
            ["schemaVersion"]=1,["operationId"]=id,["operation"]="rotate_entities",["preparedUtc"]=LeaseTime.Format(timestamp),
            ["expiresUtc"]=LeaseTime.Format(set.Expiry),["executionEnabled"]=false,["parameters"]=parameters.DeepClone(),
            ["entitySet"]=setParameters.DeepClone(),["sourceIdentityDigest"]=set.Context.Identity,
            ["sourceObservedDigest"]=set.Context.Digest,["entityKeys"]=set.Plan["entityKeys"]!.DeepClone(),
            ["nativeMethod"]="IPshapeModelDoc.RotateItem",["nativeAngleType"]="long (integer degrees)",
            ["nativeAxis"]=definition.NativeAxis(),["lengthUnitsRaw"]=set.Source["lengthUnitsRaw"]?.DeepClone(),
            ["lengthUnitFactorRaw"]=set.Source["lengthUnitFactorRaw"]?.DeepClone(),
            ["requiredBeforeExecution"]=new JsonArray("qualified_native_rotation_executor","owned_working_copy","independent_checkpoint","world_frame_guard","fresh_same_context_guard","independent_postconditions","recovery_fault_matrix"),
            ["qualification"]="Passive integer-degree rotation plan only; no CAD, native invocation or edit authority. Fractional angles, executor and native direction/frame semantics require separate qualification. Rotated AABB corners are not an exact arbitrary-angle shape bounding box."
        };
        var sha=ArtifactProof.Freeze(root,Path.Combine("rotationplans",id+".json"),plan);
        PreparedJournal.Create(root,id,sha,timestamp,new(){["executionEnabled"]=false,["cadMutationAttempted"]=false});
        var remaining=Math.Min((set.Expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds,candidates.SetRemaining(setHandle));
        if(remaining<=0)throw new ReferenceRejected("rotation_plan_expired_during_preparation"){OperationId=id,PlanSha256=sha};
        var token=Guid.NewGuid().ToString("N");plans[token]=new(id,sha,setHandle,set.Context,now()+remaining);
        return new(){["state"]="rotation_plan_prepared_local",["rotationPlanHandle"]=token,["operationId"]=id,["planSha256"]=sha,["plan"]=plan,
            ["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};
    }
    public JsonObject Inspect(string handle)
    {
        if(!plans.TryGetValue(handle,out var lease)||lease.Expires<=now())throw new ReferenceRejected("rotation_plan_handle_unknown_or_expired");
        var set=OperationPlanStore.ReadSet(root,candidates.SetParameters(lease.Set),LeaseTime.PythonPrecision(utcNow()));
        if(candidates.Context!=lease.Context||set.Context!=lease.Context)throw new ReferenceRejected("rotation_source_context_changed");
        return ArtifactProof.Verified(root,"rotationplans",lease.Id,lease.Sha,65536);
    }
    public JsonObject ReferenceParameters(string handle){Inspect(handle);var lease=plans[handle];return new(){["rotationPlanId"]=lease.Id,["rotationPlanSha256"]=lease.Sha};}
    public double Remaining(string handle){Inspect(handle);return plans[handle].Expires-now();}
}
