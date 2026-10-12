using System.Text.Json;
using System.Text.Json.Nodes;
using PowerShapeMcp;

namespace PowerShapeMcp.Tests;

public sealed class SafetyFixture
{
    public Fixture Files { get; }=new();
    public double Time=100;
    public DateTimeOffset Utc=new(2026,10,12,10,0,0,TimeSpan.Zero);
    public CandidateStore Candidates { get; }
    public WorkingCopyStore Copies { get; }
    public OperationPlanStore Plans { get; }
    public DocumentBindingStore Bindings { get; }
    public OwnedOperationStore Owned { get; }
    public JsonObject Source { get; }
    public string SourceId { get; }
    public string SourceSha { get; }
    public string Document { get; }
    public string Copy { get; }
    public string Binding { get; }
    public string[] Sets { get; }
    public JsonObject BindingReceipt { get; }
    public string Root=>Files.Root;
    public SafetyFixture()
    {
        Candidates=new(()=>Time,()=>Utc);Copies=new(()=>Time,()=>Utc);Plans=new(Root,Candidates,()=>Time,()=>Utc);Bindings=new(Root,Candidates,Copies,()=>Time,()=>Utc);Owned=new(Root,Plans,Bindings,()=>Time,()=>Utc);
        using var data=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","observations.json")));
        Source=new();foreach(var field in OperationPlanStore.ObservedFields)Source[field]=null;
        foreach(var field in JsonNode.Parse(data.RootElement[0].GetProperty("snapshot").GetRawText())!.AsObject())Source[field.Key]=field.Value?.DeepClone();
        Source["productVersion"]="23.2.2";Source["modelScope"]="active_document_read_only";Source["timestampUtc"]=LeaseTime.Format(Utc);
        Source["contextAfter"]=new JsonArray(0,0);Source["selectionBefore"]=new JsonArray();Source["selectionAfter"]=new JsonArray();Source["levelsContainingEntities"]=new JsonArray();
        Source["entities"]!.AsArray().Add(JsonNode.Parse("{\"nativeTypeCode\":21,\"nativeId\":8,\"type\":\"SURFACE\",\"name\":\"Plane\",\"levelNumber\":0}"));
        Source["entities"]!.AsArray().Add(JsonNode.Parse("{\"nativeTypeCode\":59,\"nativeId\":10,\"type\":\"COMPCURVE\",\"name\":\"Curve\",\"levelNumber\":0}"));
        Source["entityCount"]=3;SourceId=Guid.NewGuid().ToString("N");Source["bridgeRequestId"]=SourceId;
        SourceSha=ArtifactProof.Freeze(Root,"responses/"+SourceId+".json",Source);
        Document=Candidates.Issue(Source,SourceSha)["candidateDocumentHandle"]!.GetValue<string>();
        var copyId=Guid.NewGuid().ToString("N");
        Copy=Copies.Issue(new(){["state"]="working_copy_prepared_disk",["copyId"]=copyId,["bridgeRequestId"]=copyId,["ownershipSha256"]=new string('d',64),["timestampUtc"]=LeaseTime.Format(Utc)})["workingCopyHandle"]!.GetValue<string>();
        var parameters=Bindings.PrepareParameters(Document,Copy);var bindingId=Guid.NewGuid().ToString("N");var binding=parameters.DeepClone().AsObject();
        binding["schemaVersion"]=1;binding["bindingId"]=bindingId;binding["bridgeVersion"]="0.31.0";binding["timestampUtc"]=LeaseTime.Format(Utc);binding["observedPropertiesSha256"]=new string('e',64);binding["windowContextSha256"]=new string('f',64);binding["executionEnabled"]=false;
        var bindingSha=ArtifactProof.Freeze(Root,"documentbindings/"+bindingId+".json",binding);
        BindingReceipt=new(){["state"]="working_document_bound_observed",["bindingId"]=bindingId,["bindingSha256"]=bindingSha,["bridgeRequestId"]=bindingId,["binding"]=binding};
        Binding=Bindings.Issue(BindingReceipt,parameters,Document,Copy)["workingDocumentBindingHandle"]!.GetValue<string>();
        Sets=[MakeSet(new JsonArray(new JsonArray(40,5))),MakeSet(new JsonArray(new JsonArray(21,8))),MakeSet(new JsonArray(new JsonArray(59,10)))];
    }
    public string MakeSet(JsonArray keys)
    {
        var id=Guid.NewGuid().ToString("N");var expiry=LeaseTime.Format(Utc.AddSeconds(300));
        var plan=new JsonObject{["inspectionRequestId"]=SourceId,["inspectionSha256"]=SourceSha,["expiresUtc"]=expiry,["entityKeys"]=keys};
        var sha=ArtifactProof.Freeze(Root,"entitysets/"+id+".json",plan);
        return Candidates.IssueSet(new(){["state"]="entity_set_prepared_observed",["entitySetId"]=id,["entitySetSha256"]=sha,["expiresUtc"]=expiry})["entitySetHandle"]!.GetValue<string>();
    }
    public JsonObject Args(string kind="translate_entities",string[]? handles=null)=>new()
    {
        ["operation"]=kind,["entitySetHandles"]=new JsonArray((handles??(kind=="translate_entities"?Sets[..1]:Sets[..2])).Select(h=>JsonValue.Create(h)).ToArray()),
        ["parameters"]=kind=="translate_entities"?JsonNode.Parse("{\"vector\":[1.0,0,0],\"coordinateFrame\":\"world\",\"lengthUnits\":\"document_raw\",\"mode\":\"move\"}"):new JsonObject{["offset"]=0}
    };
    public (JsonObject Receipt,string Handle,string PlanHandle) Packet()
    {
        var prepared=Plans.Prepare(Args());var planHandle=prepared["operationPlanHandle"]!.GetValue<string>();var parameters=Owned.PrepareParameters(planHandle,Binding);
        var id=Guid.NewGuid().ToString("N");var packet=parameters.DeepClone().AsObject();
        packet["schemaVersion"]=1;packet["ownedOperationId"]=id;packet["preparedUtc"]=LeaseTime.Format(Utc);packet["expiresUtc"]=BindingReceipt["binding"]!["expiresUtc"]!.DeepClone();packet["executionEnabled"]=false;
        packet["nativeObservedPropertiesSha256"]=new string('e',64);packet["windowContextSha256"]=new string('f',64);packet["workingSha256"]=new string('a',64);packet["copyId"]=BindingReceipt["binding"]!["copyId"]!.DeepClone();packet["ownershipSha256"]=new string('d',64);
        var sha=ArtifactProof.Freeze(Root,"ownedoperations/"+id+".json",packet);PreparedJournal.Create(Root,id,sha,Utc,new(){["executionEnabled"]=false});
        var receipt=new JsonObject{["state"]="owned_operation_prepared_observed",["ownedOperationId"]=id,["ownedOperationSha256"]=sha,["ownedOperation"]=packet,["expiresUtc"]=packet["expiresUtc"]!.DeepClone(),["bridgeRequestId"]=id,
            ["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};
        var handle=Owned.Issue(receipt,parameters,planHandle,Binding)["ownedOperationHandle"]!.GetValue<string>();return(receipt,handle,planHandle);
    }
}
