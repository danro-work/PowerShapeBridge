using System.Diagnostics;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public sealed class ReferenceStore(Func<double>? clock = null)
{
    public const int LifetimeSeconds = 300;
    public const int MaxEntities = 4096;
    public const string Qualification = "Read observations only; not a full geometry revision or persistent identity. Unobserved close/reopen or ID reuse cannot be ruled out. These references are not accepted by mutation tools.";
    private readonly Func<double> now = clock ?? (() => (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private sealed record Document(JsonObject Identity,string Fingerprint,double Expires);
    private sealed record Entity(string Document,EntityKey Key);
    private readonly Dictionary<string,Document> documents=[];
    private readonly Dictionary<string,Entity> entities=[];
    private (string Identity,string Digest)? context;

    public void Clear(){documents.Clear();entities.Clear();context=null;}
    private void Purge()
    {
        var time=now();
        foreach(var id in documents.Where(p=>p.Value.Expires<=time).Select(p=>p.Key).ToArray())documents.Remove(id);
        foreach(var id in entities.Where(p=>!documents.ContainsKey(p.Value.Document)).Select(p=>p.Key).ToArray())entities.Remove(id);
    }
    public Observation Observe(JsonObject snapshot)
    {
        try
        {
            var observed=ObservationPolicy.Read(snapshot);
            var fresh=(ObservationPolicy.Digest(observed.Identity),observed.Digest);
            if(context is not null&&context.Value!=fresh)Clear();
            context=fresh;return observed;
        }
        catch{Clear();throw;}
    }
    public JsonObject Issue(JsonObject snapshot)
    {
        var observed=Observe(snapshot);
        if(observed.Entities.Count>MaxEntities){Clear();throw new ReferenceRejected("reference_inventory_limit");}
        Purge();
        if(entities.Count+observed.Entities.Count>MaxEntities||documents.Count>=128)
        {Clear();context=(ObservationPolicy.Digest(observed.Identity),observed.Digest);}
        var doc=Guid.NewGuid().ToString("N");
        documents[doc]=new(observed.Identity,observed.Digest,now()+LifetimeSeconds);
        var result=snapshot.DeepClone().AsObject();
        result["state"]="active_model_inspected";result["documentReference"]=doc;
        result["observedPropertiesSha256"]=observed.Digest;result["referenceLifetimeSeconds"]=LifetimeSeconds;
        result["referenceQualification"]=Qualification;
        foreach(var value in result["entities"]!.AsArray())
        {
            var entity=value!.AsObject();var token=Guid.NewGuid().ToString("N");
            var native=System.Text.Json.JsonSerializer.SerializeToElement(entity);
            entities[token]=new(doc,new(ObservationPolicy.Integer(native,"nativeTypeCode","ambiguous_entity_identity"),ObservationPolicy.Integer(native,"nativeId","ambiguous_entity_identity")));
            entity["entityReference"]=token;
        }
        return result;
    }
    private (Document Document,Entity[] Entities) Find(string document,string[] references)
    {
        Purge();
        if(!documents.TryGetValue(document,out var found))throw new ReferenceRejected("document_reference_unknown_or_expired");
        var records=new List<Entity>();
        foreach(var reference in references)
        {
            if(!entities.TryGetValue(reference,out var entity)||entity.Document!=document)throw new ReferenceRejected("entity_reference_unknown_or_wrong_document");
            records.Add(entity);
        }
        return(found,records.ToArray());
    }
    public void Require(string document,string[] references)=>Find(document,references);
    public JsonObject Resolve(string document,string[] references,JsonObject snapshot)
    {
        var expected=Find(document,references);
        var observed=Observe(snapshot);
        if(ObservationPolicy.Digest(expected.Document.Identity)!=ObservationPolicy.Digest(observed.Identity))throw new ReferenceRejected("document_context_changed");
        if(expected.Document.Fingerprint!=observed.Digest)throw new ReferenceRejected("observed_properties_changed");
        Require(document,references);
        var values=new JsonArray();
        for(var i=0;i<references.Length;i++)
        {
            var entity=JsonNode.Parse(observed.Entities[expected.Entities[i].Key].GetRawText())!.AsObject();
            entity["entityReference"]=references[i];values.Add(entity);
        }
        return new JsonObject
        {
            ["state"]="entity_references_resolved_observed",["documentReference"]=document,["entities"]=values,
            ["model"]=snapshot["model"]!.DeepClone(),["processId"]=snapshot["processId"]!.DeepClone(),
            ["observedPropertiesSha256"]=observed.Digest,["inspectionRequestId"]=snapshot["bridgeRequestId"]?.DeepClone(),
            ["warnings"]=snapshot["warnings"]?.DeepClone()??new JsonArray(),["referenceQualification"]=Qualification,["cadMutationAttempted"]=false
        };
    }
}
