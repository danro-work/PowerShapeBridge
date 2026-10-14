using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal sealed class UniformScaleDefinition
{
    public double Factor {get;}
    public IReadOnlyList<double> Centre {get;}
    private UniformScaleDefinition(double factor,double[] centre){Factor=factor;Centre=Array.AsReadOnly(centre);}
    public static UniformScaleDefinition Read(JsonObject value)
    {
        const string error="uniform_scale_parameters_invalid";
        if(!ArtifactProof.Fields(value,"factor","centre","coordinateFrame","lengthUnits","mode")||
            ArtifactProof.String(value,"coordinateFrame",error)!="world"||
            ArtifactProof.String(value,"lengthUnits",error)!="document_raw"||
            ArtifactProof.String(value,"mode",error)!="scale_in_place")throw new ReferenceRejected(error);
        var factor=Number(value["factor"]);
        if(factor<=0||factor==1)throw new ReferenceRejected("uniform_scale_factor_invalid_or_noop");
        if(value["centre"] is not JsonArray centre||centre.Count!=3)throw new ReferenceRejected(error);
        return new(factor,centre.Select(Number).ToArray());
    }
    private static double Number(JsonNode? value)
    {
        if(value is null)throw new ReferenceRejected("uniform_scale_number_invalid");
        var json=JsonSerializer.SerializeToElement(value);
        if(json.ValueKind!=JsonValueKind.Number||!json.TryGetDouble(out var number)||!double.IsFinite(number))throw new ReferenceRejected("uniform_scale_number_invalid");
        return number;
    }
    public JsonObject Parameters()=>new(){["factor"]=Factor,["centre"]=new JsonArray(Centre.Select(c=>(JsonNode?)JsonValue.Create(c)).ToArray()),
        ["coordinateFrame"]="world",["lengthUnits"]="document_raw",["mode"]="scale_in_place"};
}

// Connection-owned passive local plans. This store does not submit native requests.
internal sealed class UniformScalePlanStore(string root,CandidateStore candidates,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(string Id,string Sha,string Set,(string Identity,string Digest) Context,double Expires);
    private readonly Dictionary<string,Lease> plans=[];
    public void Clear()=>plans.Clear();
    public void Revoke(string handle)=>plans.Remove(handle);
    public JsonObject Prepare(string setHandle,JsonObject parameters)
    {
        foreach(var expired in plans.Where(p=>p.Value.Expires<=now()).Select(p=>p.Key).ToArray())plans.Remove(expired);
        if(plans.Count>=128)throw new ReferenceRejected("uniform_scale_plan_limit");
        var definition=UniformScaleDefinition.Read(parameters);var timestamp=LeaseTime.PythonPrecision(utcNow());
        var setParameters=candidates.SetParameters(setHandle);var set=OperationPlanStore.ReadSet(root,setParameters,timestamp);
        if(candidates.Context!=set.Context)throw new ReferenceRejected("uniform_scale_source_context_changed");
        var id=Guid.NewGuid().ToString("N");var plan=new JsonObject
        {
            ["schemaVersion"]=1,["operationId"]=id,["operation"]="uniform_scale_entities",
            ["preparedUtc"]=LeaseTime.Format(timestamp),["expiresUtc"]=LeaseTime.Format(set.Expiry),
            ["executionEnabled"]=false,["nativeProviderQualified"]=false,["parameters"]=definition.Parameters(),
            ["entitySet"]=setParameters.DeepClone(),["sourceIdentityDigest"]=set.Context.Identity,
            ["sourceObservedDigest"]=set.Context.Digest,["entityKeys"]=set.Plan["entityKeys"]!.DeepClone(),
            ["lengthUnitsRaw"]=set.Source["lengthUnitsRaw"]?.DeepClone(),["lengthUnitFactorRaw"]=set.Source["lengthUnitFactorRaw"]?.DeepClone(),
            ["requiredBeforeExecution"]=new JsonArray("qualified_uniform_scale_recipe","qualified_native_measurements","fresh_owned_context_guard","exact_target_set","primitive_remapping_proof","once_claim_and_mutation_fence","independent_postconditions","saved_result_reopen","recovery_fault_matrix"),
            ["qualification"]="Passive positive uniform scale plan only; no native queue/edit authority. Native recipe, conversion/remapping, units, measurements, saved result and faults require qualification."
        };
        var sha=ArtifactProof.Freeze(root,Path.Combine("scaleplans",id+".json"),plan);
        PreparedJournal.Create(root,id,sha,timestamp,new(){["executionEnabled"]=false,["cadMutationAttempted"]=false});
        var remaining=Math.Min((set.Expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds,candidates.SetRemaining(setHandle));
        if(remaining<=0)throw new ReferenceRejected("uniform_scale_plan_expired_during_preparation"){OperationId=id,PlanSha256=sha};
        var handle=Guid.NewGuid().ToString("N");plans[handle]=new(id,sha,setHandle,set.Context,now()+remaining);
        return new(){["state"]="uniform_scale_plan_prepared_local",["scalePlanHandle"]=handle,["operationId"]=id,["planSha256"]=sha,["plan"]=plan,
            ["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};
    }
    public JsonObject Inspect(string handle)
    {
        if(!plans.TryGetValue(handle,out var lease)||lease.Expires<=now())throw new ReferenceRejected("uniform_scale_plan_handle_unknown_or_expired");
        var set=OperationPlanStore.ReadSet(root,candidates.SetParameters(lease.Set),LeaseTime.PythonPrecision(utcNow()));
        if(candidates.Context!=lease.Context||set.Context!=lease.Context)throw new ReferenceRejected("uniform_scale_source_context_changed");
        var plan=ArtifactProof.Verified(root,"scaleplans",lease.Id,lease.Sha,65536);
        var journal=ArtifactProof.Journal(root,lease.Id);
        if(journal["state"]?.GetValue<string>()!="prepared"||!ArtifactProof.IntegerIs(journal["eventCount"],1)||
            !ArtifactProof.Boolean(journal["writerLockPresent"],false)||ArtifactProof.String(journal,"planSha256","uniform_scale_journal_invalid")!=lease.Sha)
            throw new ReferenceRejected("uniform_scale_journal_not_prepared");
        return plan;
    }
    public JsonObject ReferenceParameters(string handle){Inspect(handle);var lease=plans[handle];return new(){["scalePlanId"]=lease.Id,["scalePlanSha256"]=lease.Sha};}
    public double Remaining(string handle){Inspect(handle);return plans[handle].Expires-now();}
}
