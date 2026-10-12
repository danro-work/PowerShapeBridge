using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Read-only candidate lease portion. Set preparation/validation arrives in K4.
public sealed class CandidateStore(Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    public const string Qualification="Experimental observed entity set only. No CAD edit is authorized. Standalone validation is not a check immediately before a later edit. Unobserved ID reuse and changes outside measured properties remain possible.";
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(double Expires,JsonObject Parameters);
    private sealed record Candidate(string Document,EntityKey Key);
    private readonly Dictionary<string,Lease> documents=[];
    private readonly Dictionary<string,Candidate> candidates=[];
    private readonly Dictionary<string,Lease> sets=[];
    private (string Identity,string Digest)? context;
    public (string Identity,string Digest)? Context=>context;
    public void Clear(){documents.Clear();candidates.Clear();sets.Clear();context=null;}
    private void Purge()
    {
        var time=now();foreach(var id in documents.Where(p=>p.Value.Expires<=time).Select(p=>p.Key).ToArray())documents.Remove(id);
        foreach(var id in candidates.Where(p=>!documents.ContainsKey(p.Value.Document)).Select(p=>p.Key).ToArray())candidates.Remove(id);
        foreach(var id in sets.Where(p=>p.Value.Expires<=time).Select(p=>p.Key).ToArray())sets.Remove(id);
    }
    public Observation Observe(JsonObject snapshot)
    {
        try
        {
            var observed=ObservationPolicy.Read(snapshot);var fresh=(ObservationPolicy.Digest(observed.Identity),observed.Digest);
            if(context is not null&&context.Value!=fresh)Clear();context=fresh;return observed;
        }
        catch{Clear();throw;}
    }
    public JsonObject Issue(JsonObject snapshot,string receiptSha256)
    {
        var observed=Observe(snapshot);var data=JsonSerializer.SerializeToElement(snapshot);
        var request=ObservationPolicy.String(data,"bridgeRequestId","entity_candidate_receipt_invalid");
        if(!FileArtifacts.Token(request)||!FileArtifacts.Token(receiptSha256,64)){Clear();throw new ReferenceRejected("entity_candidate_receipt_invalid");}
        var time=LeaseTime.Remaining(ObservationPolicy.String(data,"timestampUtc","entity_set_time_invalid_or_expired"),utcNow(),"entity_set_time_invalid_or_expired");
        var seconds=Math.Min(time.Remaining,300);
        Purge();if(observed.Entities.Count>4096){Clear();throw new ReferenceRejected("entity_candidate_inventory_limit");}
        if(candidates.Count+observed.Entities.Count>4096||documents.Count>=32){Clear();context=(ObservationPolicy.Digest(observed.Identity),observed.Digest);}
        var document=Guid.NewGuid().ToString("N");
        documents[document]=new(now()+seconds,new JsonObject{["inspectionRequestId"]=request,["inspectionSha256"]=receiptSha256,["expiresUtc"]=LeaseTime.Format(time.Expiry)});
        var result=snapshot.DeepClone().AsObject();result["state"]="entity_candidates_inspected";result["candidateDocumentHandle"]=document;
        result["candidateLifetimeSeconds"]=(int)seconds;result["entitySetQualification"]=Qualification;
        foreach(var value in result["entities"]!.AsArray())
        {
            var entity=value!.AsObject();var handle=Guid.NewGuid().ToString("N");var native=JsonSerializer.SerializeToElement(entity);
            var key=new EntityKey(ObservationPolicy.Integer(native,"nativeTypeCode","ambiguous_entity_identity"),ObservationPolicy.Integer(native,"nativeId","ambiguous_entity_identity"));
            candidates[handle]=new(document,key);entity["candidateEntityHandle"]=handle;entity["entitySetSupported"]=key.Type is 40 or 21 or 59;
        }
        return result;
    }
    public JsonObject DocumentParameters(string document)
    {
        Purge();if(!documents.TryGetValue(document,out var found))throw new ReferenceRejected("candidate_document_unknown_or_expired");
        return found.Parameters.DeepClone().AsObject();
    }
    public double DocumentRemaining(string document){DocumentParameters(document);return documents[document].Expires-now();}
    public JsonObject IssueSet(JsonObject result)
    {
        var id=ArtifactProof.String(result,"entitySetId","entity_set_receipt_invalid");
        var sha=ArtifactProof.String(result,"entitySetSha256","entity_set_receipt_invalid");
        if(result["state"]?.GetValue<string>()!="entity_set_prepared_observed"||!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64))throw new ReferenceRejected("entity_set_receipt_invalid");
        var expiry=LeaseTime.Parse(ArtifactProof.String(result,"expiresUtc","entity_set_time_invalid_or_expired"),"entity_set_time_invalid_or_expired");
        var seconds=(expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds;
        if(seconds<=0||seconds>300)throw new ReferenceRejected("entity_set_time_invalid_or_expired");
        Purge();if(sets.Count>=128)sets.Clear();
        var handle=Guid.NewGuid().ToString("N");
        sets[handle]=new(now()+seconds,new JsonObject{["entitySetId"]=id,["entitySetSha256"]=sha});
        var issued=result.DeepClone().AsObject();issued["entitySetHandle"]=handle;issued["entitySetQualification"]=Qualification;return issued;
    }
    public JsonObject SetParameters(string handle)
    {
        Purge();if(!sets.TryGetValue(handle,out var found))throw new ReferenceRejected("entity_set_handle_unknown_or_expired");
        return found.Parameters.DeepClone().AsObject();
    }
    public double SetRemaining(string handle){SetParameters(handle);return sets[handle].Expires-now();}
    // Internal K4 seam only; no public preparation tool is registered by this class.
    public JsonObject PrepareParameters(string document,string[] handles)
    {
        var parameters=DocumentParameters(document);
        if(handles.Length is <1 or >128||handles.Any(h=>!FileArtifacts.Token(h))||handles.Distinct().Count()!=handles.Length)throw new ReferenceRejected("candidate_entities_invalid");
        var keys=new JsonArray();
        foreach(var handle in handles)
        {
            if(!candidates.TryGetValue(handle,out var found)||found.Document!=document)throw new ReferenceRejected("candidate_entity_unknown_or_wrong_document");
            if(found.Key.Type is not (40 or 21 or 59))throw new ReferenceRejected("entity_set_type_unsupported");
            keys.Add(new JsonArray(found.Key.Type,found.Key.Id));
        }
        parameters["entityKeys"]=keys;return parameters;
    }
}
