using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class OwnedSaveTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_owned_save_as",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Reserve a NEW immutable owned SaveAs target from this connection's working document binding. Pins native file/directory/proof dependencies; no CAD connection, SaveAs, activation or geometry command. Preparing does not authorize a later save; unavailable in read-only mode.")]
    public async Task<CallToolResult> PrepareOwnedSaveAs([Description("Fresh working document binding handle in this connection.")]string workingDocumentBindingHandle,CancellationToken cancellationToken)
    {
        const string name="prepare_owned_save_as";session.AssertToolAllowed(name);
        var timer=Stopwatch.StartNew();var acquired=false;var submitted=false;string? request=null;
        try
        {
            if(!await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken))return DiagnosticTools.Result(Failure("queue_timeout_before_submission",false),true);
            acquired=true;
            if(cancellationToken.IsCancellationRequested)return DiagnosticTools.Result(Failure("cancelled_before_submission",false),true);
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return DiagnosticTools.Result(Failure("maintenance_before_submission",false),true);
            var parameters=session.SaveTargets.PrepareParameters(workingDocumentBindingHandle);submitted=true;
            var data=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=parameters},cancellationToken,session.Options.Timeout-timer.Elapsed);
            request=data["bridgeRequestId"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||data["state"]?.GetValue<string>()!="owned_save_target_prepared_files")
            {
                session.SaveTargets.Clear();
                if(cancellationToken.IsCancellationRequested&&data["state"]?.GetValue<string>() is not ("cancelled_before_submission" or "queue_timeout_before_submission" or "maintenance_before_submission"))
                {data=Failure("cancelled_outcome_unknown",true);data["bridgeRequestId"]=request;}
                return DiagnosticTools.Result(data,true);
            }
            return DiagnosticTools.Result(session.SaveTargets.Issue(data,parameters,workingDocumentBindingHandle));
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return DiagnosticTools.Result(Failure("cancelled_before_submission",false),true);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if(acquired)session.SaveTargets.Clear();var data=Failure("owned_save_target_rejected",submitted);
            data["error"]=new JsonObject{["code"]=error is ReferenceRejected rejected?rejected.Code:"save_target_proof_unavailable",["message"]=error.Message};
            if(request is not null)data["bridgeRequestId"]=request;
            return DiagnosticTools.Result(data,true);
        }
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    [McpServerTool(Name="execute_owned_save_as",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false),Description("Save the active owned working document to its prepared NEW fixed target exactly once through the existing native worker. Consumes the save handle and CAD leases before IPC. Returned success remains unverified/fenced until a separate fresh verifier; unknown outcomes are never saved or retried automatically. Unavailable in read-only mode.")]
    public Task<CallToolResult> ExecuteOwnedSaveAs([Description("Unconsumed prepared SaveAs handle in this connection.")]string ownedSaveAsHandle,CancellationToken cancellationToken)=>Save(false,new(){["ownedSaveAsHandle"]=ownedSaveAsHandle},cancellationToken);

    [McpServerTool(Name="verify_owned_save_as",ReadOnly=true,Destructive=false,Idempotent=false,OpenWorld=false),Description("Independently verify the original owned SaveAs receipt, authoritative return journal, fresh observations and saved bytes. Native verifier may append the journal and release only its matching fence; returns a connection-scoped savedResultHandle on checked success. No SaveAs, activation or geometry command. Unavailable in read-only server.")]
    public Task<CallToolResult> VerifyOwnedSaveAs([Description("Original SaveAs request ID.")]string saveRequestId,[Description("Original native SaveAs receipt SHA256.")]string saveReceiptSha256,[Description("Fresh candidate observation of the saved result.")]string candidateDocumentHandle,[Description("Fresh one-use active listing choice of that result.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Save(true,new(){["saveRequestId"]=saveRequestId,["saveReceiptSha256"]=saveReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    private async Task<CallToolResult> Save(bool verify,JsonObject args,CancellationToken cancellationToken)
    {
        var name=verify?"verify_owned_save_as":"execute_owned_save_as";session.AssertToolAllowed(name);
        var timer=Stopwatch.StartNew();var acquired=false;var submitted=false;string? request=null;
        try
        {
            if(!await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken))return DiagnosticTools.Result(SaveFailure(verify,"queue_timeout_before_submission",false),true);
            acquired=true;
            if(cancellationToken.IsCancellationRequested)return DiagnosticTools.Result(SaveFailure(verify,"cancelled_before_submission",false),true);
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return DiagnosticTools.Result(SaveFailure(verify,"maintenance_before_submission",false),true);
            JsonObject parameters;
            if(verify)parameters=OwnedExecutionProof.VerificationParameters(args,session.Candidates,session.Documents,false,true);
            else {parameters=session.SaveTargets.Consume(args["ownedSaveAsHandle"]!.GetValue<string>());session.RevokeCadLeases(true);}
            submitted=true;
            var data=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=parameters},cancellationToken,session.Options.Timeout-timer.Elapsed);
            request=data["bridgeRequestId"]?.GetValue<string>();var state=data["state"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested&&!BeforeSubmission(state))
            {var unknown=SaveFailure(verify,"cancelled_outcome_unknown",true);unknown["bridgeRequestId"]=request;return DiagnosticTools.Result(unknown,true);}
            if(state==(verify?"owned_save_as_verified_observed":"owned_save_as_executed_unverified"))
            {
                if(verify)data=session.SavedResults.Issue(data,parameters,args["candidateDocumentHandle"]!.GetValue<string>());
                else session.SavedResults.CheckedExecution(data,parameters);
                if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)
                {
                    if(data["savedResultHandle"] is JsonValue handle)session.SavedResults.Revoke(handle.GetValue<string>());
                    var unknown=SaveFailure(verify,cancellationToken.IsCancellationRequested?"cancelled_outcome_unknown":"timeout_outcome_unknown",true);unknown["bridgeRequestId"]=request;
                    return DiagnosticTools.Result(unknown,true);
                }
                return DiagnosticTools.Result(data);
            }
            data["retryAllowed"]=false;
            if(!BeforeSubmission(state)&&!(state=="failed"&&request is null))
            {
                if(verify)data["verificationMayBePartial"]=true;
                else if(state!="owned_save_as_rejected_before_change")data["cadSaveMayHaveOccurred"]=true;
                if(state!=(verify?"owned_save_as_verification_rejected":"owned_save_as_rejected_before_change")&&state!="owned_save_as_outcome_unknown")
                {data["nativeState"]=state;data["state"]=verify?"owned_save_as_verification_rejected":"owned_save_as_outcome_unknown";}
            }
            return DiagnosticTools.Result(data,true);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested)
        {return DiagnosticTools.Result(SaveFailure(verify,submitted?"cancelled_outcome_unknown":"cancelled_before_submission",submitted),true);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            var data=SaveFailure(verify,verify?"owned_save_as_verification_rejected":submitted?"owned_save_as_outcome_unknown":"owned_save_as_rejected_before_change",submitted);
            data["error"]=new JsonObject{["code"]=error is ReferenceRejected rejected?rejected.Code:"saved_result_proof_unavailable",["message"]=error.Message};
            if(request is not null)data["bridgeRequestId"]=request;return DiagnosticTools.Result(data,true);
        }
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private static bool BeforeSubmission(string? state)=>state is "cancelled_before_submission" or "queue_timeout_before_submission" or "maintenance_before_submission" or "too_many_pending_calls" or "failed_before_submission";
    private static JsonObject SaveFailure(bool verify,string state,bool submitted)=>new(){["state"]=state,["cadSaveAttempted"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,
        ["cadSaveMayHaveOccurred"]=submitted&&!verify,["verificationMayBePartial"]=submitted&&verify,["retryAllowed"]=false,["qualification"]="Inspect the original request, receipt and journal. No automatic SaveAs replay, inverse or fence repair."};
    private static JsonObject Failure(string state,bool artifacts)=>new(){["state"]=state,["executionEnabled"]=false,["cadSaveAttempted"]=false,
        ["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["localArtifactsMayExist"]=artifacts,["retryAllowed"]=false};
    public static McpServerTool[] Create(BridgeSession session)
    {
        var tools=new List<McpServerTool>();var instance=new OwnedSaveTools(session);
        foreach(var method in typeof(OwnedSaveTools).GetMethods().Where(m=>m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            var name=method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;
            if(!session.PreparedEnabled(name)&&!session.ExecutionEnabled(name))continue;
            var properties=new JsonObject();var required=new JsonArray();
            foreach(var parameter in method.GetParameters().Where(p=>p.ParameterType!=typeof(CancellationToken)))
            {
                properties[parameter.Name!]=new JsonObject{["type"]="string",["pattern"]=parameter.Name!.EndsWith("Sha256",StringComparison.Ordinal)?"^[0-9a-f]{64}$":"^[0-9a-f]{32}$",["description"]=parameter.GetCustomAttribute<DescriptionAttribute>()?.Description};
                required.Add(parameter.Name);
            }
            var schema=new JsonObject{["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=false};
            tools.Add(new StrictTool(McpServerTool.Create(method,instance),session,schema));
        }
        return tools.ToArray();
    }
}
