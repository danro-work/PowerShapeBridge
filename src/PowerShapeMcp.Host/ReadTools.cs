using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class ReadTools(BridgeSession session)
{
    [McpServerTool(Name="list_documents",ReadOnly=true,Destructive=false,OpenWorld=false)]
    [Description("Read and recheck already-open PowerShape documents/windows without activation, selection, saving, closing or images. Issues one-use document choice handles within 300 seconds in this connection. Multi-window documents are reported as unsupported choices. Read-only cannot activate them.")]
    public Task<CallToolResult> ListDocuments(CancellationToken cancellationToken)=>Read("list_documents",null,null,cancellationToken);

    [McpServerTool(Name="inspect_active_model",ReadOnly=true,Destructive=false,OpenWorld=false)]
    [Description("Read the active supported PowerShape document without selecting, editing, saving or capturing an image. Returns inventory, available properties and read references valid for 300 seconds in this MCP connection. References do not certify full topology or persistent identity and never authorize a mutation.")]
    public Task<CallToolResult> InspectActiveModel(CancellationToken cancellationToken)=>Read("inspect_active_model",null,null,cancellationToken);

    [McpServerTool(Name="resolve_entity_references",ReadOnly=true,Destructive=false,OpenWorld=false)]
    [Description("Freshly read the active document and resolve this connection's read references. Rejects expired, foreign or cross-document references and changed observed context/properties/units. Does not extend reference TTL, select entities, create images or authorize editing.")]
    public Task<CallToolResult> ResolveEntityReferences(
        [Description("Read document reference from inspect_active_model in this connection.")]string documentReference,
        [Description("One to 128 distinct read entity references issued for that document in this connection.")]string[] entityReferences,
        CancellationToken cancellationToken)=>Read("resolve_entity_references",documentReference,entityReferences,cancellationToken);

    [McpServerTool(Name="inspect_entity_candidates",ReadOnly=true,Destructive=false,OpenWorld=false)]
    [Description("Experimental: freshly read active document and issue candidate handles separate from read references. No selection, activation, save or capture. Handles expire within 300 seconds in this connection; no edit authorization or persistent/topological identity.")]
    public Task<CallToolResult> InspectEntityCandidates(CancellationToken cancellationToken)=>Read("inspect_entity_candidates",null,null,cancellationToken);

    private async Task<CallToolResult> Read(string name,string? document,string[]? references,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed(name);
        var timer=Stopwatch.StartNew();
        var acquired=false;JsonObject? inspection=null;
        try
        {
            if(!await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken))
                return DiagnosticTools.Result(new JsonObject{["state"]="queue_timeout_before_submission"},true);
            acquired=true;
            if(name=="resolve_entity_references")session.References.Require(document!,references!);
            var recipe=name=="list_documents"?"list_documents":"inspect_active_model";
            inspection=await session.Ipc.SubmitAsync("recipe.prepare",new JsonObject{["recipeId"]=recipe,["parameters"]=new JsonObject()},cancellationToken,session.Options.Timeout-timer.Elapsed);
            if(cancellationToken.IsCancellationRequested)
            {
                Clear(name);
                return DiagnosticTools.Result(Cancelled(name),true);
            }
            var expected=name=="list_documents"?"documents_listed_verified":"succeeded_verified";
            if(inspection["state"]?.GetValue<string>()!=expected){Clear(name);return DiagnosticTools.Result(inspection,true);}
            // Native read recipes always run without capture. Refuse an unexpected
            // capture receipt and never open/return image bytes from read-only.
            if(inspection.ContainsKey("viewportCapture"))throw new InvalidDataException("Unexpected capture in read-only receipt.");
            JsonObject data;
            if(name=="list_documents")data=session.Documents.Issue(inspection,ReceiptHash(inspection));
            else if(name=="inspect_entity_candidates")data=session.Candidates.Issue(inspection,ReceiptHash(inspection));
            else
            {
                session.Candidates.Observe(inspection);
                data=name=="inspect_active_model"?session.References.Issue(inspection):session.References.Resolve(document!,references!,inspection);
            }
            return DiagnosticTools.Result(data);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested)
        {return DiagnosticTools.Result(Cancelled(name),true);}
        catch(Exception ex)when(ex is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // A forged/expired reference refused before a CAD read must not revoke
            // unrelated valid leases. Once a read was attempted, revoke its scope.
            if(acquired&&(ex is not ReferenceRejected||inspection is not null))Clear(name);
            var code=ex is ReferenceRejected rejected?rejected.Code:name=="inspect_entity_candidates"?"entity_set_receipt_or_data_unavailable":"read_receipt_or_data_unavailable";
            var data=new JsonObject{["state"]=name=="list_documents"?"document_choice_rejected":name=="inspect_entity_candidates"?"entity_set_rejected":"read_reference_rejected",
                ["error"]=new JsonObject{["code"]=code},["cadMutationAttempted"]=false};
            if(name=="list_documents"){data.Remove("cadMutationAttempted");data["contextChangeAttempted"]=false;data["geometryMutationAttempted"]=false;}
            if(inspection is not null&&name is "inspect_active_model" or "resolve_entity_references")
            {data["inspectionRequestId"]=inspection["bridgeRequestId"]?.DeepClone();data["observedDocument"]=inspection["model"]?.DeepClone();}
            return DiagnosticTools.Result(data,true);
        }
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private string ReceiptHash(JsonObject delivered)
    {
        var id=delivered["bridgeRequestId"]!.GetValue<string>();
        if(!FileArtifacts.Token(id))throw new InvalidDataException("Invalid receipt identity.");
        var raw=FileArtifacts.ReadBytes(session.Options.BridgeRoot,Path.Combine("responses",id+".json"),16000000);
        var recorded=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        if(recorded.ContainsKey("bridgeRequestId")&&recorded["bridgeRequestId"]?.GetValue<string>()!=id)throw new InvalidDataException("Recorded receipt identity differs.");
        recorded.Remove("bridgeRequestId");var observed=delivered.DeepClone().AsObject();observed.Remove("bridgeRequestId");
        if(!JsonNode.DeepEquals(recorded,observed))throw new InvalidDataException("Receipt differs from delivered observations.");
        return FileArtifacts.Sha256(raw);
    }
    private void Clear(string name)
    {
        if(name=="list_documents")session.Documents.Clear();
        else if(name=="inspect_entity_candidates")session.Candidates.Clear();
        else{session.References.Clear();session.Candidates.Clear();}
    }
    private static JsonObject Cancelled(string name)=>name=="list_documents"?
        new(){["state"]="cancelled_read",["contextChangeAttempted"]=false,["geometryMutationAttempted"]=false}:
        new(){["state"]="cancelled_read",["cadMutationAttempted"]=false};

    public static McpServerTool[] Create(BridgeSession session)
    {
        var methods=new ReadTools(session);
        var tools=new List<McpServerTool>
        {
            new StrictTool(McpServerTool.Create(methods.ListDocuments),session,StrictTool.Empty()),
            new StrictTool(McpServerTool.Create(methods.InspectActiveModel),session,StrictTool.Empty()),
            new StrictTool(McpServerTool.Create(methods.ResolveEntityReferences),session,StrictTool.ReadReferences())
        };
        if(session.CandidatesEnabled)tools.Add(new StrictTool(McpServerTool.Create(methods.InspectEntityCandidates),session,StrictTool.Empty()));
        return tools.ToArray();
    }
}
