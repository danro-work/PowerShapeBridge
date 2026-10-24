using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
namespace PowerShapeMcp;

[McpServerToolType]
public sealed class OwnedSelectionTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_owned_selection",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Prepare an experimental owned selection change without changing CAD. Only a sole SOLID in mm/WORLD, safe alphanumeric/underscore name: select_single from empty using a fresh same-binding entity candidate, or clear exactly that selected SOLID. Other types/sets/subentities and no-op requests are rejected. No geometry, save, OPEN or UI. Full only.")]
    public Task<CallToolResult> Prepare([Description("Fresh owned working-document binding in this connection.")]string workingDocumentBindingHandle,[Description("Exact {mode:select_single,candidateEntityHandle:...} from the same inspection as binding, or {mode:clear}. Clear requires the sole body selected.")]JsonElement parameters,CancellationToken cancellationToken)=>Run("prepare_owned_selection",new(){["workingDocumentBindingHandle"]=workingDocumentBindingHandle,["parameters"]=JsonNode.Parse(parameters.GetRawText())},cancellationToken);
    [McpServerTool(Name="execute_owned_selection",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false)]
    [Description("Execute one approved prepared owned selection change with one fixed native callback. Consumes the connection-local intent and CAD leases before IPC; keeps the matching fence until independent verification. No geometry, save, OPEN, activation or mouse. Never replay or auto-clear after unknown. Full only.")]
    public Task<CallToolResult> Execute([Description("Unconsumed ownedSelectionHandle from this connection's preparation.")]string ownedSelectionHandle,CancellationToken cancellationToken)=>Run("execute_owned_selection",new(){["ownedSelectionHandle"]=ownedSelectionHandle},cancellationToken);
    [McpServerTool(Name="verify_owned_selection",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Independently verify the original selection callback from its immutable native receipt and fresh current candidate/listing. Checks exact selected-item metadata for one body or explicit double count-zero proof, unchanged geometry/direct bounds/files/windows and original claim/return/journal. May complete journal and release only matching fence. Null GetSelectedItems is never inferred empty. No selection, replay, save, OPEN or edit authority. After checked success regain copy access with existing original OPEN verifier/fresh binding. Full only.")]
    public Task<CallToolResult> Verify([Description("Original native execution request ID; may be used after reconnect solely for verification.")]string executionRequestId,[Description("Exact original raw execution SHA256, lowercase64hex.")]string executionReceiptSha256,[Description("Fresh candidateDocumentHandle after callback return.")]string candidateDocumentHandle,[Description("Fresh one-use active listing choice.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run("verify_owned_selection",new(){["executionRequestId"]=executionRequestId,["executionReceiptSha256"]=executionReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);
    async Task<CallToolResult> Run(string name,JsonObject input,CancellationToken ct)
    {
        session.AssertToolAllowed(name);bool gate=false,submitted=false;string? request=null;var timer=Stopwatch.StartNew();
        CallToolResult Fail(string state,string? code=null){var o=new JsonObject{["state"]=state,["localArtifactsMayExist"]=submitted,["contextChangeMayHaveOccurred"]=submitted&&name=="execute_owned_selection",["retryAllowed"]=false};if(request is not null)o["bridgeRequestId"]=request;if(code is not null)o["errorCode"]=code;return DiagnosticTools.Result(o,true);}
        try{
            gate=await session.ReferenceGate.WaitAsync(session.Options.Timeout,ct);if(!gate)return Fail("queue_timeout_before_submission");ct.ThrowIfCancellationRequested();if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Fail("maintenance_before_submission");JsonObject args;
            if(name=="prepare_owned_selection")args=session.Selections.Sources(input["workingDocumentBindingHandle"]!.GetValue<string>(),input["parameters"]!.AsObject());
            else if(name=="execute_owned_selection"){args=session.Selections.Consume(input["ownedSelectionHandle"]!.GetValue<string>());session.RevokeCadLeases(true);}
            else{_=OwnedSelectionProof.Execution(session.Options.BridgeRoot,input["executionRequestId"]!.GetValue<string>(),input["executionReceiptSha256"]!.GetValue<string>());args=OwnedExecutionProof.VerificationParameters(input,session.Candidates,session.Documents,true);}
            ct.ThrowIfCancellationRequested();var remaining=session.Options.Timeout-timer.Elapsed;if(remaining<=TimeSpan.Zero)return Fail("queue_timeout_before_submission");submitted=true;var reply=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=args},ct,remaining);request=reply["bridgeRequestId"]?.GetValue<string>();if(ct.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Fail("owned_selection_outcome_unknown");
            if(name=="prepare_owned_selection"){if(reply["state"]?.GetValue<string>()!="owned_selection_prepared_observed")return DiagnosticTools.Result(reply,true);reply=session.Selections.Issue(reply,args,input["workingDocumentBindingHandle"]!.GetValue<string>(),input["parameters"]!.AsObject());if(ct.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout){session.Selections.Clear();return Fail("owned_selection_preparation_outcome_unknown");}return DiagnosticTools.Result(reply);}
            if(name=="execute_owned_selection"){reply["intentConsumed"]=true;reply["retryAllowed"]=false;if(reply["state"]?.GetValue<string>()!="owned_selection_executed_unverified")return DiagnosticTools.Result(reply,true);var raw=OwnedSelectionProof.Raw(session.Options.BridgeRoot,request!);_=OwnedSelectionProof.Execution(session.Options.BridgeRoot,request!,raw.Sha);foreach(var k in new[]{"selectionId","selectionSha256"})OwnedSelectionProof.Need(JsonNode.DeepEquals(reply["plan"]![k],args[k]));reply["executionReceiptSha256"]=raw.Sha;return DiagnosticTools.Result(reply);}
            if(reply["state"]?.GetValue<string>()!="owned_selection_verified_observed")return DiagnosticTools.Result(reply,true);OwnedSelectionProof.Verified(session.Options.BridgeRoot,reply,args);return DiagnosticTools.Result(reply);
        }catch(OperationCanceledException)when(ct.IsCancellationRequested){return Fail(submitted?"owned_selection_outcome_unknown":"cancelled_before_submission");}
        catch(Exception e)when(e is ReferenceRejected or IOException or InvalidOperationException or JsonException or KeyNotFoundException or FormatException or NullReferenceException or UnauthorizedAccessException){return Fail(submitted?"owned_selection_outcome_unknown":"owned_selection_rejected_before_submission",e is ReferenceRejected r?r.Code:"owned_selection_proof_unavailable");}
        finally{if(gate)session.ReferenceGate.Release();}
    }
    public static McpServerTool[] Create(BridgeSession s)
    {
        if(!s.PreparedEnabled("prepare_owned_selection"))return[];var t=new OwnedSelectionTools(s);
        JsonNode Token(string key)=>StrictTool.Token(key,"Fresh connection-local opaque handle.")["properties"]![key]!.DeepClone();
        JsonObject Schema(params(string Key,JsonNode Value)[] fields){var p=new JsonObject();var r=new JsonArray();foreach(var(k,v)in fields){p[k]=v;r.Add(k);}return new(){["type"]="object",["properties"]=p,["required"]=r,["additionalProperties"]=false};}
        JsonNode Mode(string value)=>new JsonObject{["type"]="string",["const"]=value};var parameters=new JsonObject{["oneOf"]=new JsonArray(Schema(("mode",Mode("clear"))),Schema(("mode",Mode("select_single")),("candidateEntityHandle",Token("candidateEntityHandle"))))};
        return[new StrictTool(McpServerTool.Create(t.Prepare),s,Schema(("workingDocumentBindingHandle",Token("workingDocumentBindingHandle")),("parameters",parameters)),x=>OwnedSelectionStore.Parameters(x["parameters"]!.AsObject())),new StrictTool(McpServerTool.Create(t.Execute),s,Schema(("ownedSelectionHandle",Token("ownedSelectionHandle")))),new StrictTool(McpServerTool.Create(t.Verify),s,Schema(("executionRequestId",Token("executionRequestId")),("executionReceiptSha256",new JsonObject{["type"]="string",["pattern"]="^[0-9a-f]{64}$"}),("candidateDocumentHandle",Token("candidateDocumentHandle")),("activeDocumentHandle",Token("activeDocumentHandle"))))];
    }
}
