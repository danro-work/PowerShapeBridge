using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class OwnedWorkplaneActivationTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_owned_workplane_activation",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Prepare one owned existing-workplane activation or WORLD return without changing CAD. Provider accepts one SOLID and one or more WORKPLANEs, empty selection, WORLD or an observed workplane as source. Preserves all observed frames; source and target may differ. Target is a fresh typed candidate or explicit world mode; names/paths/native IDs are not accepted. Full server only.")]
    public Task<CallToolResult> Prepare(
        [Description("Fresh owned working-document binding handle in this connection.")]string workingDocumentBindingHandle,
        [Description("Exact target: {mode:world} or {mode:existing,workplaneCandidateHandle:...} from the same fresh inspection as the binding. Target must differ from current context.")]JsonElement parameters,
        CancellationToken cancellationToken)=>Run("prepare_owned_workplane_activation",new(){["workingDocumentBindingHandle"]=workingDocumentBindingHandle,["parameters"]=JsonNode.Parse(parameters.GetRawText())},cancellationToken);

    [McpServerTool(Name="execute_owned_workplane_activation",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false)]
    [Description("Execute the explicitly approved context change once with one direct ActiveWorkplane setter. Leaves that context active; no automatic WORLD return, creation, geometry edit, selection, save or open. Consumes preparation before submission and retains the owned fence until independent verification. Never replay after timeout or unknown result.")]
    public Task<CallToolResult> Execute([Description("Unconsumed prepared workplaneActivationHandle; execution and verification handles cannot execute again.")]string workplaneActivationHandle,CancellationToken cancellationToken)=>Run("execute_owned_workplane_activation",new(){["workplaneActivationHandle"]=workplaneActivationHandle},cancellationToken);

    [McpServerTool(Name="verify_owned_workplane_activation",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Verify the original single setter using fresh independent candidate/listing reads, direct getters, unchanged WORLD bounds/frame/files and mathematically expected COG coordinates. Releases only its matching fence and issues current owned-copy access after checked success. Does not activate, recover, replay, save or certify full topology. Full server only.")]
    public Task<CallToolResult> Verify([Description("This connection's activationExecutionHandle, including an adopted verification-only handle.")]string activationExecutionHandle,
        [Description("Fresh candidateDocumentHandle read after command return.")]string candidateDocumentHandle,
        [Description("Fresh one-use active document handle from list_documents after command return.")]string activeDocumentHandle,
        CancellationToken cancellationToken)=>Run("verify_owned_workplane_activation",new(){["activationExecutionHandle"]=activationExecutionHandle,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    [McpServerTool(Name="adopt_owned_workplane_activation_execution",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("After reconnect, check original activation intent, execution, once claim, journal, setter-return receipt and owned files; issue only a verification handle. No queue/CAD call, replay, revived preparation or execution authority. An unknown setter without authoritative return remains unknown. Full server only.")]
    public Task<CallToolResult> Adopt([Description("Original lowercase 32-hex execution request ID, not a path.")]string executionRequestId,
        [Description("Original raw native execution receipt SHA256, lowercase64hex.")]string executionReceiptSha256,
        CancellationToken cancellationToken)=>Run("adopt_owned_workplane_activation_execution",new(){["executionRequestId"]=executionRequestId,["executionReceiptSha256"]=executionReceiptSha256},cancellationToken);

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
            var value=new JsonObject{["state"]=state,["localArtifactsMayExist"]=sent,["cadMutationMayHaveOccurred"]=sent&&name=="execute_owned_workplane_activation",["retryAllowed"]=false};
            if(execution is not null){value["activationExecutionHandle"]=execution;if(name=="execute_owned_workplane_activation")value["intentConsumed"]=true;}
            if(request is not null)value["bridgeRequestId"]=request;if(error is not null)value["error"]=error;return DiagnosticTools.Result(value,true);
        }
        try
        {
            if(name=="prepare_owned_workplane_activation")_=OwnedWorkplaneActivationStore.Parameters(input["parameters"]!.AsObject());
            acquired=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!acquired)return Failed("queue_timeout_before_submission",false);
            cancellationToken.ThrowIfCancellationRequested();if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Failed("maintenance_before_submission",false);
            if(name=="adopt_owned_workplane_activation_execution")
            {
                var adopted=session.WorkplaneActivations.AdoptExecution(input["executionRequestId"]!.GetValue<string>(),input["executionReceiptSha256"]!.GetValue<string>());cancellationToken.ThrowIfCancellationRequested();return DiagnosticTools.Result(adopted);
            }
            if(name=="prepare_owned_workplane_activation")
            {
                string binding=input["workingDocumentBindingHandle"]!.GetValue<string>();var args=session.WorkplaneActivations.Sources(binding,input["parameters"]!.AsObject());cancellationToken.ThrowIfCancellationRequested();submitted=true;
                var response=await Submit(name,args);if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero)return Failed("owned_workplane_activation_preparation_outcome_unknown",true);
                if(response["state"]?.GetValue<string>()!="owned_workplane_activation_prepared_observed")return DiagnosticTools.Result(response,true);
                var issued=session.WorkplaneActivations.Issue(response,args,binding,input["parameters"]!.AsObject());if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero){session.WorkplaneActivations.RevokePrepared(issued["workplaneActivationHandle"]!.GetValue<string>());return Failed("owned_workplane_activation_preparation_outcome_unknown",true);}return DiagnosticTools.Result(issued);
            }
            if(name=="execute_owned_workplane_activation")
            {
                var consumed=session.WorkplaneActivations.Consume(input["workplaneActivationHandle"]!.GetValue<string>());execution=consumed.ExecutionHandle;session.RevokeCadLeases(true);cancellationToken.ThrowIfCancellationRequested();submitted=true;
                var response=await Submit(name,consumed.Parameters);session.WorkplaneActivations.Record(execution,response);response["activationExecutionHandle"]=execution;response["intentConsumed"]=true;response["retryAllowed"]=false;
                if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero)return Failed("owned_workplane_activation_outcome_unknown",true);
                if(response["state"]?.GetValue<string>()=="owned_workplane_activation_executed_unverified")session.WorkplaneActivations.CheckExecuted(execution);
                return DiagnosticTools.Result(response,response["state"]?.GetValue<string>()!="owned_workplane_activation_executed_unverified");
            }
            execution=input["activationExecutionHandle"]!.GetValue<string>();var proof=session.WorkplaneActivations.ExecutionProof(execution);
            proof["candidateDocumentHandle"]=input["candidateDocumentHandle"]!.DeepClone();proof["activeDocumentHandle"]=input["activeDocumentHandle"]!.DeepClone();var verification=OwnedExecutionProof.VerificationParameters(proof,session.Candidates,session.Documents,true);
            cancellationToken.ThrowIfCancellationRequested();submitted=true;var reply=await Submit(name,verification);if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero)return Failed("owned_workplane_activation_verification_outcome_unknown",true);
            if(reply["state"]?.GetValue<string>()=="owned_workplane_activation_verified_observed")
            {
                var ownedCopy=session.WorkplaneActivations.CheckVerified(execution,reply,verification);
                var issued=session.Copies.IssueVerifiedWorkplaneActivation(reply,ownedCopy,LeaseTime.Parse(verification["expiresUtc"]!.GetValue<string>(),"verified_workplane_copy_expired"));
                foreach(var field in new[]{"workingCopyHandle","workingCopyLifetimeSeconds"})reply[field]=issued[field]!.DeepClone();
                reply["workingCopyLeaseScope"]="Existing owned saved files and independently verified current context. No persisted active-context claim; bind using fresh candidates before another separately approved action.";
            }
            reply["activationExecutionHandle"]=execution;return DiagnosticTools.Result(reply,reply["state"]?.GetValue<string>()!="owned_workplane_activation_verified_observed");
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return Failed(submitted?"owned_workplane_activation_outcome_unknown":"cancelled_before_submission",submitted);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or NullReferenceException)
        {return Failed(submitted?"owned_workplane_activation_outcome_unknown":"owned_workplane_activation_rejected_before_submission",submitted,error is ReferenceRejected rejected?rejected.Code:"owned_workplane_activation_proof_unavailable");}
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_owned_workplane_activation"))return[];var tools=new OwnedWorkplaneActivationTools(session);
        JsonNode Token(string name)=>StrictTool.Token(name,name=="workingDocumentBindingHandle"?"Fresh owned working-document binding in this connection.":name=="workplaneActivationHandle"?"Unconsumed preparation handle; not a verification handle.":name=="activationExecutionHandle"?"Verification handle for the original one-use execution.":name=="executionRequestId"?"Original lowercase32hex execution request ID.":"Fresh opaque handle from this connection.")["properties"]![name]!.DeepClone();
        JsonObject Schema(params (string Name,JsonNode Value)[] fields){var props=new JsonObject();var required=new JsonArray();foreach(var (key,value) in fields){props[key]=value;required.Add(key);}return new(){["type"]="object",["properties"]=props,["required"]=required,["additionalProperties"]=false};}
        JsonObject Constant(string value)=>new(){["type"]="string",["const"]=value};
        var parameters=new JsonObject{["oneOf"]=new JsonArray(Schema(("mode",Constant("world"))),Schema(("mode",Constant("existing")),("workplaneCandidateHandle",Token("workplaneCandidateHandle"))))};
        return [new StrictTool(McpServerTool.Create(tools.Prepare),session,Schema(("workingDocumentBindingHandle",Token("workingDocumentBindingHandle")),("parameters",parameters))),
            new StrictTool(McpServerTool.Create(tools.Execute),session,Schema(("workplaneActivationHandle",Token("workplaneActivationHandle")))),
            new StrictTool(McpServerTool.Create(tools.Verify),session,Schema(("activationExecutionHandle",Token("activationExecutionHandle")),("candidateDocumentHandle",Token("candidateDocumentHandle")),("activeDocumentHandle",Token("activeDocumentHandle")))),
            new StrictTool(McpServerTool.Create(tools.Adopt),session,Schema(("executionRequestId",Token("executionRequestId")),("executionReceiptSha256",new JsonObject{["type"]="string",["pattern"]="^[0-9a-f]{64}$",["description"]="SHA256 of the original raw native execution receipt."})))];
    }
}
