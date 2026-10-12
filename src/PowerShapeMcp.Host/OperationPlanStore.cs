using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public sealed record ObservedSet(JsonObject Plan,JsonObject Source,(string Identity,string Digest) Context,DateTimeOffset Expiry,EntityKey[] Keys);

public sealed class OperationPlanStore(string root,CandidateStore candidates,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    public const string Qualification="Local observed plan only; no CAD command or execution authorization. Execution requires a qualified worker guard in its own COM context, owned working copy, independent checkpoint and postcondition checks. Observed properties do not certify full topology or persistent identity.";
    public static readonly string[] ObservedFields=["processId","processStartedUtc","windowsSessionId","productVersion","model","entityCount","entities","levelsContainingEntities","contextBefore","contextAfter","selectionBefore","selectionAfter","workplanes","lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"];
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(string Id,string Sha,(string Identity,string Digest) Context,double Expires,string[] Sets);
    private readonly Dictionary<string,Lease> plans=[];
    public void Clear()=>plans.Clear();

    public static void ValidateArguments(JsonObject args)
    {
        if(!ArtifactProof.Fields(args,"operation","entitySetHandles","parameters"))throw new ReferenceRejected("operation_plan_arguments_invalid");
        var kind=ArtifactProof.String(args,"operation","operation_kind_unsupported");
        if(kind is not ("translate_entities" or "intersect_entity_sets"))throw new ReferenceRejected("operation_kind_unsupported");
        if(args["entitySetHandles"] is not JsonArray handles||handles.Count!=(kind=="translate_entities"?1:2))throw new ReferenceRejected("operation_sets_invalid");
        var tokens=handles.Select(h=>h is JsonValue value&&value.TryGetValue<string>(out var text)?text:null).ToArray();
        if(tokens.Any(t=>!FileArtifacts.Token(t))||tokens.Distinct().Count()!=tokens.Length)throw new ReferenceRejected("operation_sets_invalid");
        if(args["parameters"] is not JsonObject parameters)throw new ReferenceRejected("operation_parameters_invalid");
        if(kind=="translate_entities")
        {
            if(!ArtifactProof.Fields(parameters,"vector","coordinateFrame","lengthUnits","mode")||
               ArtifactProof.String(parameters,"coordinateFrame","translation_parameters_invalid")!="world"||
               ArtifactProof.String(parameters,"lengthUnits","translation_parameters_invalid")!="document_raw"||
               ArtifactProof.String(parameters,"mode","translation_parameters_invalid")!="move")throw new ReferenceRejected("translation_parameters_invalid");
            if(parameters["vector"] is not JsonArray vector||vector.Count!=3)throw new ReferenceRejected("translation_vector_invalid");
            var values=vector.Select(v=>Number(v,"translation_vector_invalid")).ToArray();
            if(values.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1000000)||values.All(v=>v==0))throw new ReferenceRejected("translation_vector_invalid");
        }
        else if(!ArtifactProof.Fields(parameters,"offset")||Number(parameters["offset"],"intersection_offset_not_qualified")!=0)throw new ReferenceRejected("intersection_offset_not_qualified");
    }
    private static double Number(JsonNode? value,string code)
    {
        if(value is null)throw new ReferenceRejected(code);
        var data=JsonSerializer.SerializeToElement(value);
        if(data.ValueKind!=JsonValueKind.Number||!data.TryGetDouble(out var number))throw new ReferenceRejected(code);
        return number;
    }
    public static ObservedSet ReadSet(string root,JsonObject parameters,DateTimeOffset time)
    {
        if(!ArtifactProof.Fields(parameters,"entitySetId","entitySetSha256"))throw new ReferenceRejected("operation_set_identity_invalid");
        var id=ArtifactProof.String(parameters,"entitySetId","operation_set_identity_invalid");var sha=ArtifactProof.String(parameters,"entitySetSha256","operation_set_identity_invalid");
        if(!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64))throw new ReferenceRejected("operation_set_identity_invalid");
        var plan=ArtifactProof.Verified(root,"entitysets",id,sha,65536);
        if(!ArtifactProof.Fields(plan,"inspectionRequestId","inspectionSha256","expiresUtc","entityKeys"))throw new ReferenceRejected("operation_set_artifact_invalid");
        var inspection=ArtifactProof.String(plan,"inspectionRequestId","operation_set_artifact_invalid");var inspectionSha=ArtifactProof.String(plan,"inspectionSha256","operation_set_artifact_invalid");
        if(!FileArtifacts.Token(inspection)||!FileArtifacts.Token(inspectionSha,64))throw new ReferenceRejected("operation_set_artifact_invalid");
        var source=ArtifactProof.Verified(root,"responses",inspection,inspectionSha,16000000);
        if(source.ContainsKey("bridgeRequestId")&&source["bridgeRequestId"]?.GetValue<string>()!=inspection)throw new ReferenceRejected("operation_source_identity_invalid");
        if(source["state"]?.GetValue<string>()!="succeeded_verified"||source["modelScope"]?.GetValue<string>()!="active_document_read_only")throw new ReferenceRejected("operation_source_not_verified");
        var observed=ObservationPolicy.Read(source);
        if(plan["entityKeys"] is not JsonArray keys||keys.Count is <1 or >128)throw new ReferenceRejected("operation_set_keys_invalid");
        var parsed=new List<EntityKey>();
        foreach(var value in keys)
        {
            if(value is not JsonArray pair||pair.Count!=2)throw new ReferenceRejected("operation_set_keys_invalid");
            var raw=JsonSerializer.SerializeToElement(pair);
            if(raw[0].ValueKind!=JsonValueKind.Number||!raw[0].TryGetInt64(out var type)||raw[1].ValueKind!=JsonValueKind.Number||!raw[1].TryGetInt64(out var nativeId)||type is not (40 or 21 or 59)||nativeId<0)throw new ReferenceRejected("operation_set_keys_invalid");
            parsed.Add(new(type,nativeId));
        }
        if(parsed.Distinct().Count()!=parsed.Count||parsed.Any(k=>!observed.Entities.ContainsKey(k)))throw new ReferenceRejected("operation_set_keys_ambiguous_or_absent");
        var issued=LeaseTime.Parse(ArtifactProof.String(source,"timestampUtc","operation_time_invalid"),"operation_time_invalid");
        var expiry=LeaseTime.Parse(ArtifactProof.String(plan,"expiresUtc","operation_time_invalid"),"operation_time_invalid");
        time=LeaseTime.PythonPrecision(time);
        if(issued>time.AddSeconds(5)||expiry<=time||expiry>issued.AddSeconds(300))throw new ReferenceRejected("operation_set_expired");
        return new(plan,source,(ObservationPolicy.Digest(observed.Identity),observed.Digest),expiry,parsed.ToArray());
    }
    public static JsonObject SourceObservations(JsonObject source)
    {
        if(ObservedFields.Any(field=>!source.ContainsKey(field)))throw new ReferenceRejected("operation_source_observations_incomplete");
        var result=new JsonObject();foreach(var field in ObservedFields)result[field]=source[field]?.DeepClone();
        if(result["entities"] is not JsonArray entities)throw new ReferenceRejected("operation_source_observations_incomplete");
        var ordered=entities.Select(e=>e!.DeepClone()).OrderBy(e=>e!["nativeTypeCode"]!.GetValue<long>()).ThenBy(e=>e!["nativeId"]!.GetValue<long>()).ToArray();
        result["entities"]=new JsonArray(ordered);return result;
    }
    public JsonObject Prepare(JsonObject args)
    {
        ValidateArguments(args);var timestamp=LeaseTime.PythonPrecision(utcNow());
        var handles=args["entitySetHandles"]!.AsArray().Select(h=>h!.GetValue<string>()).ToArray();
        var sets=handles.Select(h=>ReadSet(root,candidates.SetParameters(h),timestamp)).ToArray();
        var context=sets[0].Context;var observed=SourceObservations(sets[0].Source);
        if(candidates.Context!=context||sets.Any(s=>s.Context!=context||!JsonNode.DeepEquals(SourceObservations(s.Source),observed)))throw new ReferenceRejected("operation_sets_document_or_observations_differ");
        var kind=args["operation"]!.GetValue<string>();
        if(kind=="intersect_entity_sets")
        {
            if(sets[0].Keys.Any(k=>k.Type is not (40 or 21))||sets[1].Keys.Any(k=>k.Type!=21))throw new ReferenceRejected("intersection_set_type_unsupported");
            if(sets[0].Keys.Intersect(sets[1].Keys).Any())throw new ReferenceRejected("intersection_sets_overlap");
        }
        var id=Guid.NewGuid().ToString("N");var expiry=sets.Min(s=>s.Expiry);var source=sets[0].Source;
        var identity=new JsonObject();foreach(var key in new[]{"processId","processStartedUtc","windowsSessionId","productVersion","model"})identity[key]=source[key]?.DeepClone();
        var entitySets=new JsonArray();
        for(var i=0;i<sets.Length;i++)
        {var entry=candidates.SetParameters(handles[i]);foreach(var (key,value) in sets[i].Plan)entry[key]=value?.DeepClone();entitySets.Add(entry);}
        var plan=new JsonObject
        {
            ["schemaVersion"]=1,["operationId"]=id,["operation"]=kind,["parameters"]=args["parameters"]!.DeepClone(),
            ["preparedUtc"]=LeaseTime.Format(timestamp),["expiresUtc"]=LeaseTime.Format(expiry),["executionEnabled"]=false,
            ["sourceIdentity"]=identity,["observedPropertiesSha256"]=ObservationPolicy.Digest(observed),
            ["observationDigestEncoding"]="Python JSON sorted keys; distinct from native C# digest",
            ["lengthUnitsRaw"]=source["lengthUnitsRaw"]?.DeepClone(),["lengthUnitFactorRaw"]=source["lengthUnitFactorRaw"]?.DeepClone(),
            ["entitySets"]=entitySets,["requiredBeforeExecution"]=new JsonArray("qualified_operation","owned_working_copy","independent_checkpoint","guard_in_executor_com_context","postconditions"),["qualification"]=Qualification
        };
        string? sha=null;
        try
        {
            sha=ArtifactProof.Freeze(root,Path.Combine("operationplans",id+".json"),plan);
            PreparedJournal.Create(root,id,sha,timestamp,new JsonObject{["cadMutationAttempted"]=false,["executionEnabled"]=false});
            if(expiry<=LeaseTime.PythonPrecision(utcNow()))throw new ReferenceRejected("operation_plan_expired_during_preparation");
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ReferenceRejected or InvalidDataException or JsonException)
        {throw new ReferenceRejected("operation_plan_artifacts_incomplete_or_expired"){OperationId=id,PlanSha256=sha};}
        foreach(var handle in plans.Where(p=>p.Value.Expires<=now()).Select(p=>p.Key).ToArray())plans.Remove(handle);
        if(plans.Count>=128)Clear();var token=Guid.NewGuid().ToString("N");
        var remaining=Math.Min((expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds,handles.Min(candidates.SetRemaining));
        if(remaining<=0)throw new ReferenceRejected("operation_plan_artifacts_incomplete_or_expired"){OperationId=id,PlanSha256=sha};
        plans[token]=new(id,sha,context,now()+remaining,handles);
        return new(){["state"]="operation_plan_prepared_local",["operationId"]=id,["planSha256"]=sha,["operationPlanHandle"]=token,["plan"]=plan,["cadMutationAttempted"]=false,["qualification"]=Qualification};
    }
    public JsonObject Inspect(string handle)
    {
        if(!plans.TryGetValue(handle,out var found)||found.Expires<=now()||found.Context!=candidates.Context){Clear();throw new ReferenceRejected("operation_plan_handle_unknown_or_expired");}
        var timestamp=LeaseTime.PythonPrecision(utcNow());foreach(var set in found.Sets)ReadSet(root,candidates.SetParameters(set),timestamp);
        var plan=ArtifactProof.Verified(root,"operationplans",found.Id,found.Sha,65536);
        if(LeaseTime.Parse(plan["expiresUtc"]!.GetValue<string>(),"operation_time_invalid")<=timestamp)throw new ReferenceRejected("operation_plan_expired");
        return new(){["state"]="operation_plan_inspected_local",["operationId"]=found.Id,["planSha256"]=found.Sha,["plan"]=plan,["journal"]=ArtifactProof.Journal(root,found.Id),["cadMutationAttempted"]=false,["qualification"]=Qualification};
    }
    public double Remaining(string handle){Inspect(handle);return plans[handle].Expires-now();}
}
