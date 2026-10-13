using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class CheckpointRecoveryTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_checkpoint_recovery",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Pin an owned operation's protected checkpoint by original plan/ownership/file proofs and prepare a one-use filesystem recovery intent. No CAD, OPEN, save, close, inverse or original outcome resolution. Creates intent files only; absent from read-only server.")]
    public Task<CallToolResult> Prepare([Description("Original owned native operation ID; legacy activation has no checkpoint.")]string operationId,[Description("SHA256 of the original immutable owned native plan.")]string operationPlanSha256,CancellationToken cancellationToken)=>Run(false,new(){["operationId"]=operationId,["operationPlanSha256"]=operationPlanSha256},cancellationToken);

    [McpServerTool(Name="create_checkpoint_working_copy",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Consume this connection's recovery intent before IPC and create a NEW owned Working copy from its readonly checkpoint saved bytes. Original files/journal/claims/fences remain; unknown is not resolved. No CAD or OPEN; independent guarded OPEN/verification is a separate step. Never automatically replay an unknown copy; absent from read-only server.")]
    public Task<CallToolResult> CreateCopy([Description("Unconsumed checkpointRecoveryHandle from this connection.")]string checkpointRecoveryHandle,CancellationToken cancellationToken)=>Run(true,new(){["checkpointRecoveryHandle"]=checkpointRecoveryHandle},cancellationToken);

    private async Task<CallToolResult> Run(bool materialize,JsonObject args,CancellationToken cancellationToken)
    {
        var name=materialize?"create_checkpoint_working_copy":"prepare_checkpoint_recovery";session.AssertToolAllowed(name);
        var timer=Stopwatch.StartNew();bool acquired=false,submitted=false;string? request=null;
        try
        {
            acquired=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);
            if(!acquired)return DiagnosticTools.Result(Failure("queue_timeout_before_submission",false),true);
            if(cancellationToken.IsCancellationRequested)return DiagnosticTools.Result(Failure("cancelled_before_submission",false),true);
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return DiagnosticTools.Result(Failure("maintenance_before_submission",false),true);
            var parameters=materialize?session.CheckpointRecoveries.Consume(args["checkpointRecoveryHandle"]!.GetValue<string>()):args;
            submitted=true;var data=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=parameters},cancellationToken,session.Options.Timeout-timer.Elapsed);
            request=data["bridgeRequestId"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return DiagnosticTools.Result(Failure(cancellationToken.IsCancellationRequested?"cancelled_outcome_unknown":"timeout_outcome_unknown",true,request),true);
            if(data["state"]?.GetValue<string>()!=(materialize?"checkpoint_working_copy_prepared_disk":"checkpoint_recovery_prepared_files"))return DiagnosticTools.Result(Failure(data["state"]?.GetValue<string>()??"checkpoint_recovery_outcome_unknown",true,request),true);
            var result=materialize?session.Copies.Issue(session.CheckpointRecoveries.CheckCreated(data,parameters)):session.CheckpointRecoveries.Issue(data,parameters);
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)
            {if(materialize)session.Copies.Revoke(result["workingCopyHandle"]!.GetValue<string>());else session.CheckpointRecoveries.Revoke(result["checkpointRecoveryHandle"]!.GetValue<string>());return DiagnosticTools.Result(Failure("timeout_or_cancel_after_proof_unknown",true,request),true);}
            return DiagnosticTools.Result(result);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return DiagnosticTools.Result(Failure(submitted?"cancelled_outcome_unknown":"cancelled_before_submission",submitted,request),true);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {var result=Failure(submitted?"checkpoint_recovery_outcome_unknown":"checkpoint_recovery_rejected_before_submission",submitted,request);result["errorCode"]=error is ReferenceRejected rejected?rejected.Code:"checkpoint_recovery_proof_unavailable";result["error"]=error.Message;return DiagnosticTools.Result(result,true);}
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private static JsonObject Failure(string state,bool submitted,string? request=null)=>new(){["state"]=state,["bridgeRequestId"]=request,["localArtifactsMayExist"]=submitted,["retryAllowed"]=false,["originalOutcomeResolved"]=false,["cadSaveAttempted"]=false,["contextChangeAttempted"]=false,["geometryMutationAttempted"]=false,["powerShapeConnectionAttempted"]=false};
    public static McpServerTool[] Create(BridgeSession session)
    {
        var tools=new List<McpServerTool>();var instance=new CheckpointRecoveryTools(session);
        foreach(var method in typeof(CheckpointRecoveryTools).GetMethods().Where(method=>method.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            var name=method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;if(!session.PreparedEnabled(name))continue;
            var properties=new JsonObject();var required=new JsonArray();foreach(var parameter in method.GetParameters().Where(parameter=>parameter.ParameterType!=typeof(CancellationToken)))
            {properties[parameter.Name!]=new JsonObject{["type"]="string",["pattern"]=parameter.Name!.EndsWith("Sha256",StringComparison.Ordinal)?"^[0-9a-f]{64}$":"^[0-9a-f]{32}$",["description"]=parameter.GetCustomAttribute<DescriptionAttribute>()!.Description};required.Add(parameter.Name);}
            tools.Add(new StrictTool(McpServerTool.Create(method,instance),session,new(){["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=false}));
        }
        return tools.ToArray();
    }
}
