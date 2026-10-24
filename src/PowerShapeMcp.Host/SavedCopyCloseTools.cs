using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class SavedCopyCloseTools(BridgeSession session)
{
    [McpServerTool(Name="execute_owned_saved_copy_close",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false),Description("Close exactly one explicitly disposable verified saved-reopen Working copy in its active last model window, with canonical saved source still open. Requires a current binding issued by saved-reopen verification and fresh candidate/active choice. Consumes all CAD leases before IPC. No Save, geometry, activation, close-all or automatic retry. Returns unverified/unknown; separate verifier required. Full mode only.")]
    public Task<CallToolResult> Execute([Description("Current binding issued by verify_owned_saved_reopen in this connection.")]string workingDocumentBindingHandle,[Description("Fresh candidate of the disposable active Working copy.")]string candidateDocumentHandle,[Description("Fresh one-use active model window choice.")]string activeDocumentHandle,[Description("Explicit decision for this owned test duplicate only: discard_owned_saved_copy.")]string disposition,CancellationToken cancellationToken)=>Run(false,new(){["workingDocumentBindingHandle"]=workingDocumentBindingHandle,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle,["disposition"]=disposition},cancellationToken);

    [McpServerTool(Name="verify_owned_saved_copy_close",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Independently verify one original owned saved-copy close from fresh remaining-document/window reads and immutable original receipt, plan, claim, files and authoritative command-return journal. May append terminal success and release only its matching fence. No close, Save, activation, geometry command, replay or new edit binding. Absence of original command return cannot be resolved by this tool. Full mode only.")]
    public Task<CallToolResult> Verify([Description("Closed owned copy ID from the original close plan/receipt.")]string closedCopyId,[Description("Original close Native request ID.")]string closeRequestId,[Description("SHA256 of original immutable close Native receipt.")]string closeReceiptSha256,[Description("Fresh candidate of the remaining active document after close.")]string candidateDocumentHandle,[Description("Fresh one-use choice of that remaining active window.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run(true,new(){["closedCopyId"]=closedCopyId,["closeRequestId"]=closeRequestId,["closeReceiptSha256"]=closeReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    private async Task<CallToolResult> Run(bool verify,JsonObject input,CancellationToken cancellationToken)
    {
        string name=verify?"verify_owned_saved_copy_close":"execute_owned_saved_copy_close";session.AssertToolAllowed(name);var timer=Stopwatch.StartNew();bool acquired=false,submitted=false;string? request=null;
        try
        {
            if(!await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken))return DiagnosticTools.Result(Failure(verify,"queue_timeout_before_submission",false),true);acquired=true;
            if(cancellationToken.IsCancellationRequested)return DiagnosticTools.Result(Failure(verify,"cancelled_before_submission",false),true);
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return DiagnosticTools.Result(Failure(verify,"maintenance_before_submission",false),true);
            var parameters=verify?SavedCopyCloseProof.VerifyParameters(session,input):SavedCopyCloseProof.ExecuteParameters(session,input);
            if(!verify)session.RevokeCadLeases(true);
            submitted=true;var result=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=parameters},cancellationToken,session.Options.Timeout-timer.Elapsed);request=result["bridgeRequestId"]?.GetValue<string>();string? state=result["state"]?.GetValue<string>();
            if((cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)&&!Before(state))return DiagnosticTools.Result(Failure(verify,cancellationToken.IsCancellationRequested?"cancelled_outcome_unknown":"timeout_outcome_unknown",true,request),true);
            if(state==(verify?"owned_saved_copy_close_verified":"owned_saved_copy_closed_unverified"))
            {
                if(verify)SavedCopyCloseProof.Verification(session,result,parameters);else SavedCopyCloseProof.Execution(session,result,parameters);
                if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return DiagnosticTools.Result(Failure(verify,cancellationToken.IsCancellationRequested?"cancelled_outcome_unknown":"timeout_outcome_unknown",true,request),true);
                return DiagnosticTools.Result(result);
            }
            result["retryAllowed"]=false;if(!Before(state)&&state!=(verify?"owned_saved_copy_close_verification_rejected":"owned_saved_copy_close_rejected"))result[verify?"verificationMayBePartial":"contextChangeMayHaveOccurred"]=true;return DiagnosticTools.Result(result,true);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return DiagnosticTools.Result(Failure(verify,submitted?"cancelled_outcome_unknown":"cancelled_before_submission",submitted,request),true);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {var result=Failure(verify,verify?"owned_saved_copy_close_verification_rejected":submitted?"owned_saved_copy_close_outcome_unknown":"owned_saved_copy_close_rejected",submitted,request);result["error"]=new JsonObject{["code"]=error is ReferenceRejected rejected?rejected.Code:"saved_copy_close_proof_unavailable",["message"]=error.Message};return DiagnosticTools.Result(result,true);}
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private static bool Before(string? state)=>state is "cancelled_before_submission"or"queue_timeout_before_submission"or"maintenance_before_submission"or"failed_before_submission"or"too_many_pending_calls";
    private static JsonObject Failure(bool verify,string state,bool submitted,string? request=null)
    {var result=new JsonObject{["state"]=state,["cadSaveAttempted"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["contextChangeMayHaveOccurred"]=submitted&&!verify,["verificationMayBePartial"]=submitted&&verify,["retryAllowed"]=false,["qualification"]="Inspect original close receipt/journal, immutable files and fresh windows. No automatic replay, save, cleanup or new edit binding."};if(request is not null)result["bridgeRequestId"]=request;return result;}
    public static McpServerTool[] Create(BridgeSession session)
    {
        var tools=new List<McpServerTool>();var instance=new SavedCopyCloseTools(session);
        foreach(var method in typeof(SavedCopyCloseTools).GetMethods().Where(m=>m.GetCustomAttribute<McpServerToolAttribute>()is not null))
        {
            var name=method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;if(!session.ExecutionEnabled(name))continue;var properties=new JsonObject();var required=new JsonArray();
            foreach(var p in method.GetParameters().Where(p=>p.ParameterType!=typeof(CancellationToken)))
            {var rule=new JsonObject{["type"]="string",["description"]=p.GetCustomAttribute<DescriptionAttribute>()?.Description};if(p.Name=="disposition")rule["enum"]=new JsonArray("discard_owned_saved_copy");else rule["pattern"]=p.Name!.EndsWith("Sha256",StringComparison.Ordinal)?"^[0-9a-f]{64}$":"^[0-9a-f]{32}$";properties[p.Name!]=rule;required.Add(p.Name);}
            tools.Add(new StrictTool(McpServerTool.Create(method,instance),session,new(){["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=false}));
        }
        return tools.ToArray();
    }
}
