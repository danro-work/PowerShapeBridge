using System.Diagnostics;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public sealed class DocumentBindingStore(string root,CandidateStore candidates,WorkingCopyStore copies,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(double Expires,string Document,string Copy,JsonObject Source,JsonObject Parameters);
    private readonly Dictionary<string,Lease> bindings=[];
    public void Clear()=>bindings.Clear();
    public JsonObject PrepareParameters(string document,string copy)
    {
        var result=candidates.DocumentParameters(document);foreach(var (key,value) in copies.InspectParameters(copy))result[key]=value?.DeepClone();return result;
    }
    public JsonObject VerifyReceipt(JsonObject result,JsonObject? parameters=null)
    {
        var id=ArtifactProof.String(result,"bindingId","document_binding_native_identity_invalid");var sha=ArtifactProof.String(result,"bindingSha256","document_binding_native_identity_invalid");
        if(!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64))throw new ReferenceRejected("document_binding_native_identity_invalid");
        JsonObject plan;
        try{plan=ArtifactProof.Verified(root,"documentbindings",id,sha,65536);}
        catch(ReferenceRejected){throw new ReferenceRejected("document_binding_artifact_sha_differs");}
        if(!ArtifactProof.Fields(plan,"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","expiresUtc","schemaVersion","bindingId","bridgeVersion","timestampUtc","observedPropertiesSha256","windowContextSha256","executionEnabled")||
           !ArtifactProof.IntegerIs(plan["schemaVersion"],1)||!ArtifactProof.Boolean(plan["executionEnabled"],false)||plan["bindingId"]?.GetValue<string>()!=id||!JsonNode.DeepEquals(plan,result["binding"]))throw new ReferenceRejected("document_binding_native_receipt_invalid");
        if(!FileArtifacts.Token(plan["observedPropertiesSha256"]?.GetValue<string>(),64)||!FileArtifacts.Token(plan["windowContextSha256"]?.GetValue<string>(),64))throw new ReferenceRejected("document_binding_observation_invalid");
        if(parameters is not null&&parameters.Any(p=>!JsonNode.DeepEquals(plan[p.Key],p.Value)))throw new ReferenceRejected("document_binding_source_identity_differs");
        return plan;
    }
    public JsonObject Issue(JsonObject result,JsonObject parameters,string document,string copy)
    {
        if(result["state"]?.GetValue<string>()!="working_document_bound_observed"||result["bridgeRequestId"]?.GetValue<string>()!=result["bindingId"]?.GetValue<string>())throw new ReferenceRejected("document_binding_native_receipt_invalid");
        if(!JsonNode.DeepEquals(PrepareParameters(document,copy),parameters))throw new ReferenceRejected("document_binding_source_identity_differs");
        var plan=VerifyReceipt(result,parameters);
        var expiry=LeaseTime.Parse(ArtifactProof.String(plan,"expiresUtc","document_binding_time_invalid_or_expired"),"document_binding_time_invalid_or_expired");
        var seconds=(expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds;
        if(seconds<=0||seconds>300)throw new ReferenceRejected("document_binding_time_invalid_or_expired");
        seconds=Math.Min(seconds,Math.Min(candidates.DocumentRemaining(document),copies.Remaining(copy)));
        if(seconds<=0)throw new ReferenceRejected("document_binding_source_expired");
        if(bindings.Count>=128)Clear();var handle=Guid.NewGuid().ToString("N");
        bindings[handle]=new(now()+seconds,document,copy,parameters.DeepClone().AsObject(),new(){["bindingId"]=result["bindingId"]!.DeepClone(),["bindingSha256"]=result["bindingSha256"]!.DeepClone()});
        var issued=result.DeepClone().AsObject();issued["workingDocumentBindingHandle"]=handle;issued["bindingLifetimeSeconds"]=(int)seconds;return issued;
    }
    public JsonObject ValidationParameters(string handle)
    {
        if(!bindings.TryGetValue(handle,out var found)||found.Expires<=now()){bindings.Remove(handle);throw new ReferenceRejected("document_binding_handle_unknown_or_expired");}
        try{if(!JsonNode.DeepEquals(PrepareParameters(found.Document,found.Copy),found.Source))throw new ReferenceRejected("document_binding_source_identity_differs");}
        catch(ReferenceRejected){Clear();throw;}
        return found.Parameters.DeepClone().AsObject();
    }
    public void CheckedValidation(JsonObject result,JsonObject parameters)
    {
        if(!JsonNode.DeepEquals(result["bindingId"],parameters["bindingId"])||!JsonNode.DeepEquals(result["bindingSha256"],parameters["bindingSha256"]))throw new ReferenceRejected("document_binding_native_identity_differs");
        VerifyReceipt(result);
    }
    public double Remaining(string handle){ValidationParameters(handle);return bindings[handle].Expires-now();}
}
