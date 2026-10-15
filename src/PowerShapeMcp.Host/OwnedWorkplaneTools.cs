using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class OwnedWorkplaneTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_owned_workplane",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Experimental: prepare one new owned workplane from explicit WORLD origin/Z/Y in document-raw units. Requires world frame and empty selection. The later operation creates/autoactivates it once, then explicitly returns to world once; preparing changes no CAD. No name, level, command or path input. Full server only.")]
    public Task<CallToolResult> Prepare(
        [Description("Fresh owned working-document binding handle in this connection.")]string workingDocumentBindingHandle,
        [Description("Exact object: originWorldRaw[3], zAxis[3], yAxis[3], coordinateFrame=world, lengthUnits=document_raw, activeAfter=world, mode=create_new. Z/Y must already be orthogonal unit vectors; origin bounded +/-1e9 raw units. No normalization.")]JsonElement parameters,
        CancellationToken cancellationToken)=>Run("prepare_owned_workplane",new(){["workingDocumentBindingHandle"]=workingDocumentBindingHandle,["parameters"]=JsonNode.Parse(parameters.GetRawText())},cancellationToken);

    [McpServerTool(Name="execute_owned_workplane",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false)]
    [Description("Execute this connection's explicitly approved owned workplane intent once: one CreateWorkplane with automatic activation, followed only after positive return by one declared ActiveWorkplane=0. Adds one workplane on PowerShape's current creation level, leaves world active; does not rename, select, save or reopen. Consumes authority before submission and retains an owned fence until separate verification. Never retry or automatically restore after an unknown result.")]
    public Task<CallToolResult> Execute([Description("Unconsumed prepared ownedWorkplaneHandle in this connection; a verification handle is rejected.")]string ownedWorkplaneHandle,CancellationToken cancellationToken)=>Run("execute_owned_workplane",new(){["ownedWorkplaneHandle"]=ownedWorkplaneHandle},cancellationToken);

    [McpServerTool(Name="verify_owned_workplane",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Independently verify the original creation and declared world return using fresh candidate/listing receipts, native getters, old object properties/world bounds and saved files. Appends successful evidence/releases only the matching fence. On checked success, issues a fresh connection-scoped workingCopyHandle for binding the current owned document; this is not an OPEN or save/execution authority. Does not create or activate a workplane, replay, save or certify topology. Full server only.")]
    public Task<CallToolResult> Verify([Description("This connection's workplaneExecutionHandle, including an adopted verification-only handle.")]string workplaneExecutionHandle,
        [Description("Fresh candidateDocumentHandle read after command return.")]string candidateDocumentHandle,
        [Description("Fresh one-use active document handle from list_documents after command return.")]string activeDocumentHandle,
        CancellationToken cancellationToken)=>Run("verify_owned_workplane",new(){["workplaneExecutionHandle"]=workplaneExecutionHandle,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    [McpServerTool(Name="adopt_owned_workplane_execution",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("After reconnect, check an original owned workplane execution receipt/plans/once/journal/creation-return proof and owned files; issue a verification-only handle. No native queue, CAD, artifact writes, prepared-intent revival or execution authority. Both authoritative creation and declared world-return proof are required; unknown before return remains unknown. Full server only.")]
    public Task<CallToolResult> Adopt([Description("Original lowercase 32-hex execution request ID, not a path.")]string executionRequestId,
        [Description("Original raw native execution receipt SHA256, lowercase64hex.")]string executionReceiptSha256,
        CancellationToken cancellationToken)=>Run("adopt_owned_workplane_execution",new(){["executionRequestId"]=executionRequestId,["executionReceiptSha256"]=executionReceiptSha256},cancellationToken);

    private async Task<CallToolResult> Run(string name,JsonObject input,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed(name);bool acquired=false,submitted=false;string? execution=null,request=null;var timer=Stopwatch.StartNew();
        TimeSpan Budget()=>session.Options.Timeout-timer.Elapsed;
        async Task<JsonObject> Submit(string recipe,JsonObject args)
        {
            if(Budget()<=TimeSpan.Zero){request=null;return new(){["state"]="queue_timeout_before_submission",["retryAllowed"]=false};}
            var result=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=recipe,["parameters"]=args},cancellationToken,Budget());request=result["bridgeRequestId"]?.GetValue<string>();return result;
        }
        CallToolResult Failed(string state,bool sent,string? error=null)
        {
            var value=new JsonObject{["state"]=state,["localArtifactsMayExist"]=sent,["cadMutationMayHaveOccurred"]=sent&&name=="execute_owned_workplane",["retryAllowed"]=false};
            if(execution is not null){value["workplaneExecutionHandle"]=execution;if(name=="execute_owned_workplane")value["intentConsumed"]=true;}
            if(request is not null)value["bridgeRequestId"]=request;if(error is not null)value["error"]=error;return DiagnosticTools.Result(value,true);
        }
        try
        {
            if(name=="prepare_owned_workplane")_=OwnedWorkplaneStore.Parameters(input["parameters"]!.AsObject());
            acquired=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!acquired)return Failed("queue_timeout_before_submission",false);
            cancellationToken.ThrowIfCancellationRequested();if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Failed("maintenance_before_submission",false);
            if(name=="adopt_owned_workplane_execution")
            {
                var adopted=session.Workplanes.AdoptExecution(input["executionRequestId"]!.GetValue<string>(),input["executionReceiptSha256"]!.GetValue<string>());cancellationToken.ThrowIfCancellationRequested();return DiagnosticTools.Result(adopted);
            }
            if(name=="prepare_owned_workplane")
            {
                string binding=input["workingDocumentBindingHandle"]!.GetValue<string>();var args=session.Workplanes.Sources(binding,input["parameters"]!.AsObject());cancellationToken.ThrowIfCancellationRequested();submitted=true;
                var response=await Submit(name,args);if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero)return Failed("owned_workplane_preparation_outcome_unknown",true);
                if(response["state"]?.GetValue<string>()!="owned_workplane_prepared_observed")return DiagnosticTools.Result(response,true);
                var issued=session.Workplanes.Issue(response,args,binding);if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero){session.Workplanes.RevokePrepared(issued["ownedWorkplaneHandle"]!.GetValue<string>());return Failed("owned_workplane_preparation_outcome_unknown",true);}return DiagnosticTools.Result(issued);
            }
            if(name=="execute_owned_workplane")
            {
                var consumed=session.Workplanes.Consume(input["ownedWorkplaneHandle"]!.GetValue<string>());execution=consumed.ExecutionHandle;session.RevokeCadLeases(true);cancellationToken.ThrowIfCancellationRequested();submitted=true;
                var response=await Submit(name,consumed.Parameters);session.Workplanes.Record(execution,response);response["workplaneExecutionHandle"]=execution;response["intentConsumed"]=true;response["retryAllowed"]=false;
                if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero)return Failed("owned_workplane_outcome_unknown",true);
                if(response["state"]?.GetValue<string>()=="owned_workplane_executed_unverified")session.Workplanes.CheckExecuted(execution);
                return DiagnosticTools.Result(response,response["state"]?.GetValue<string>()!="owned_workplane_executed_unverified");
            }
            execution=input["workplaneExecutionHandle"]!.GetValue<string>();var proof=session.Workplanes.ExecutionProof(execution);
            proof["candidateDocumentHandle"]=input["candidateDocumentHandle"]!.DeepClone();proof["activeDocumentHandle"]=input["activeDocumentHandle"]!.DeepClone();var verification=OwnedExecutionProof.VerificationParameters(proof,session.Candidates,session.Documents,true);
            cancellationToken.ThrowIfCancellationRequested();submitted=true;var reply=await Submit(name,verification);if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero)return Failed("owned_workplane_verification_outcome_unknown",true);
            if(reply["state"]?.GetValue<string>()=="owned_workplane_verified_observed")
            {
                var ownedCopy=session.Workplanes.CheckVerified(execution,reply,verification);
                var issued=session.Copies.IssueVerifiedWorkplane(reply,ownedCopy,LeaseTime.Parse(verification["expiresUtc"]!.GetValue<string>(),"verified_workplane_copy_expired"));
                foreach(var field in new[]{"workingCopyHandle","workingCopyLifetimeSeconds"})reply[field]=issued[field]!.DeepClone();
                reply["workingCopyLeaseScope"]="Existing owned saved files and independently observed current document. Unsaved workplane is not claimed to be persisted; bind with a fresh candidate before a separately approved next action.";
            }
            reply["workplaneExecutionHandle"]=execution;return DiagnosticTools.Result(reply,reply["state"]?.GetValue<string>()!="owned_workplane_verified_observed");
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return Failed(submitted?"owned_workplane_outcome_unknown":"cancelled_before_submission",submitted);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or NullReferenceException)
        {return Failed(submitted?"owned_workplane_outcome_unknown":"owned_workplane_rejected_before_submission",submitted,error is ReferenceRejected rejected?rejected.Code:"owned_workplane_proof_unavailable");}
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_owned_workplane"))return[];var tools=new OwnedWorkplaneTools(session);
        JsonNode Token(string name)=>StrictTool.Token(name,name=="workingDocumentBindingHandle"?"Fresh owned working-document binding in this connection.":name=="ownedWorkplaneHandle"?"Unconsumed preparation handle; not a verification handle.":name=="workplaneExecutionHandle"?"Verification handle for the original one-use execution.":name=="executionRequestId"?"Original lowercase32hex execution request ID.":"Fresh opaque handle from this connection.")["properties"]![name]!.DeepClone();
        JsonObject Schema(params (string Name,JsonNode Value)[] fields){var props=new JsonObject();var required=new JsonArray();foreach(var (key,value) in fields){props[key]=value;required.Add(key);}return new(){["type"]="object",["properties"]=props,["required"]=required,["additionalProperties"]=false};}
        JsonObject Vector(double bound)=>new(){["type"]="array",["minItems"]=3,["maxItems"]=3,["items"]=new JsonObject{["type"]="number",["minimum"]=-bound,["maximum"]=bound}};
        JsonObject Constant(string value)=>new(){["type"]="string",["const"]=value};
        var parameters=Schema(("originWorldRaw",Vector(1000000000)),("zAxis",Vector(1)),("yAxis",Vector(1)),("coordinateFrame",Constant("world")),("lengthUnits",Constant("document_raw")),("activeAfter",Constant("world")),("mode",Constant("create_new")));
        return [new StrictTool(McpServerTool.Create(tools.Prepare),session,Schema(("workingDocumentBindingHandle",Token("workingDocumentBindingHandle")),("parameters",parameters))),
            new StrictTool(McpServerTool.Create(tools.Execute),session,Schema(("ownedWorkplaneHandle",Token("ownedWorkplaneHandle")))),
            new StrictTool(McpServerTool.Create(tools.Verify),session,Schema(("workplaneExecutionHandle",Token("workplaneExecutionHandle")),("candidateDocumentHandle",Token("candidateDocumentHandle")),("activeDocumentHandle",Token("activeDocumentHandle")))),
            new StrictTool(McpServerTool.Create(tools.Adopt),session,Schema(("executionRequestId",Token("executionRequestId")),("executionReceiptSha256",new JsonObject{["type"]="string",["pattern"]="^[0-9a-f]{64}$",["description"]="SHA256 of the original raw native execution receipt."})))];
    }
}
