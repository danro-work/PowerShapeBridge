using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class OwnedSavedReopenTools(BridgeSession session)
{
    [McpServerTool(Name="execute_owned_saved_reopen",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Open one prepared saved-result Working copy in a new model window after fresh active source choice, context and immutable file/provenance checks. Consumes the reopen handle and CAD leases before IPC. Returns only executed_unverified or unknown; separate verifier required. No automatic OPEN replay, SaveAs, close or geometry command. Unavailable in read-only server.")]
    public Task<CallToolResult> ExecuteOwnedSavedReopen([Description("Unconsumed saved-result reopen preparation handle in this connection.")]string ownedReopenHandle,[Description("Fresh candidate observation of the active saved source document.")]string candidateDocumentHandle,[Description("Fresh one-use listing choice of the active saved source document.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run(false,new(){["ownedReopenHandle"]=ownedReopenHandle,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    [McpServerTool(Name="verify_owned_saved_reopen",ReadOnly=true,Destructive=false,Idempotent=false,OpenWorld=false),Description("Verify an original saved-result OPEN with authoritative return journal, separate fresh reads, protected bytes and available content/raw units. Issues a NEW working copy and document binding using current entity IDs only on checked success. May append a terminal journal/create a passive binding; no OPEN, selection, Save, close or geometry command. Unknown outcomes are never replayed. Unavailable in read-only server.")]
    public Task<CallToolResult> VerifyOwnedSavedReopen([Description("Original saved-result OPEN request ID.")]string reopenRequestId,[Description("Original native saved-result OPEN receipt SHA256.")]string reopenReceiptSha256,[Description("Fresh candidate observation of the newly opened working document.")]string candidateDocumentHandle,[Description("Fresh one-use active listing choice of the newly opened working document.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run(true,new(){["reopenRequestId"]=reopenRequestId,["reopenReceiptSha256"]=reopenReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    private async Task<CallToolResult> Run(bool verify,JsonObject args,CancellationToken cancellationToken)
    {
        var name=verify?"verify_owned_saved_reopen":"execute_owned_saved_reopen";session.AssertToolAllowed(name);
        var timer=Stopwatch.StartNew();var acquired=false;var submitted=false;string? request=null;
        try
        {
            if(!await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken))return DiagnosticTools.Result(Failure(verify,"queue_timeout_before_submission",false),true);
            acquired=true;
            if(cancellationToken.IsCancellationRequested)return DiagnosticTools.Result(Failure(verify,"cancelled_before_submission",false),true);
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return DiagnosticTools.Result(Failure(verify,"maintenance_before_submission",false),true);
            var parameters=verify?OwnedExecutionProof.VerificationParameters(args,session.Candidates,session.Documents,false,false,true):OwnedExecutionProof.SavedReopenParameters(args,session.Candidates,session.Reopens,session.Documents);
            if(!verify)session.RevokeCadLeases(true);
            submitted=true;var data=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=parameters},cancellationToken,session.Options.Timeout-timer.Elapsed);
            request=data["bridgeRequestId"]?.GetValue<string>();var state=data["state"]?.GetValue<string>();
            if((cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)&&!BeforeSubmission(state))
            {var unknown=Failure(verify,cancellationToken.IsCancellationRequested?"cancelled_outcome_unknown":"timeout_outcome_unknown",true);unknown["bridgeRequestId"]=request;return DiagnosticTools.Result(unknown,true);}
            if(state==(verify?"owned_saved_reopen_verified_observed":"owned_saved_reopen_executed_unverified"))
            {
                if(verify)data=OwnedReopenProof.IssueBinding(session,data,parameters,args["candidateDocumentHandle"]!.GetValue<string>());
                else OwnedReopenProof.Execution(session,data,parameters);
                if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)
                {
                    if(verify){session.Bindings.Clear();session.Copies.Clear();}
                    var unknown=Failure(verify,cancellationToken.IsCancellationRequested?"cancelled_outcome_unknown":"timeout_outcome_unknown",true);unknown["bridgeRequestId"]=request;return DiagnosticTools.Result(unknown,true);
                }
                return DiagnosticTools.Result(data);
            }
            data["retryAllowed"]=false;
            if(!BeforeSubmission(state)&&!(state=="failed"&&request is null))
            {
                if(verify)data["verificationMayBePartial"]=true;else if(state!="owned_saved_reopen_rejected_before_change")data["contextChangeMayHaveOccurred"]=true;
                if(state!=(verify?"owned_saved_reopen_verification_rejected":"owned_saved_reopen_rejected_before_change")&&state!="owned_saved_reopen_outcome_unknown")
                {data["nativeState"]=state;data["state"]=verify?"owned_saved_reopen_verification_rejected":"owned_saved_reopen_outcome_unknown";}
            }
            return DiagnosticTools.Result(data,true);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return DiagnosticTools.Result(Failure(verify,submitted?"cancelled_outcome_unknown":"cancelled_before_submission",submitted),true);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if(verify&&submitted){session.Bindings.Clear();session.Copies.Clear();}
            var data=Failure(verify,verify?"owned_saved_reopen_verification_rejected":submitted?"owned_saved_reopen_outcome_unknown":"owned_saved_reopen_rejected_before_change",submitted);
            data["error"]=new JsonObject{["code"]=error is ReferenceRejected rejected?rejected.Code:"saved_reopen_proof_unavailable",["message"]=error.Message};if(request is not null)data["bridgeRequestId"]=request;return DiagnosticTools.Result(data,true);
        }
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private static bool BeforeSubmission(string? state)=>state is "cancelled_before_submission" or "queue_timeout_before_submission" or "maintenance_before_submission" or "failed_before_submission" or "too_many_pending_calls";
    private static JsonObject Failure(bool verify,string state,bool submitted)=>new(){["state"]=state,["cadSaveAttempted"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,
        ["contextChangeMayHaveOccurred"]=submitted&&!verify,["verificationMayBePartial"]=submitted&&verify,["retryAllowed"]=false,["qualification"]="Inspect original OPEN receipt/journal and fresh reads; no replay, automatic Save, close or inverse. Rejected/partial verification grants no new binding."};
    public static McpServerTool[] Create(BridgeSession session)
    {
        var tools=new List<McpServerTool>();var instance=new OwnedSavedReopenTools(session);
        foreach(var method in typeof(OwnedSavedReopenTools).GetMethods().Where(m=>m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            var name=method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;if(!session.ExecutionEnabled(name))continue;
            var properties=new JsonObject();var required=new JsonArray();
            foreach(var parameter in method.GetParameters().Where(p=>p.ParameterType!=typeof(CancellationToken)))
            {properties[parameter.Name!]=new JsonObject{["type"]="string",["pattern"]=parameter.Name!.EndsWith("Sha256",StringComparison.Ordinal)?"^[0-9a-f]{64}$":"^[0-9a-f]{32}$",["description"]=parameter.GetCustomAttribute<DescriptionAttribute>()?.Description};required.Add(parameter.Name);}
            var schema=new JsonObject{["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=false};
            tools.Add(new StrictTool(McpServerTool.Create(method,instance),session,schema));
        }
        return tools.ToArray();
    }
}
