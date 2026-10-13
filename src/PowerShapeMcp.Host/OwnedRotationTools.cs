using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class OwnedRotationTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_owned_rotation",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Experimental K8: bind a passive rotation plan to this connection's owned working-document binding through the native held-context/checkpoint guard. Writes a disabled owned packet/prepared journal only. No RotateItem, selection, save or execution authority. Requires feature flag; absent from read-only server.")]
    public async Task<CallToolResult> Prepare([Description("Passive rotationPlanHandle from this connection.")]string rotationPlanHandle,[Description("Fresh owned workingDocumentBindingHandle from this connection.")]string workingDocumentBindingHandle,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed("prepare_owned_rotation");bool gate=false,submitted=false;string? handle=null,request=null;var timer=Stopwatch.StartNew();
        try
        {
            gate=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!gate)return Failure("queue_timeout_before_submission",false);
            cancellationToken.ThrowIfCancellationRequested();
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Failure("maintenance_before_submission",false);
            var parameters=session.OwnedRotations.PrepareParameters(rotationPlanHandle,workingDocumentBindingHandle);submitted=true;
            var result=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]="prepare_owned_rotation",["parameters"]=parameters},cancellationToken,session.Options.Timeout-timer.Elapsed);
            request=result["bridgeRequestId"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Failure("owned_rotation_preparation_outcome_unknown",true,request);
            if(result["state"]?.GetValue<string>()!="owned_rotation_prepared_observed")return DiagnosticTools.Result(result,true);
            var issued=session.OwnedRotations.Issue(result,parameters,rotationPlanHandle,workingDocumentBindingHandle);handle=issued["ownedRotationHandle"]!.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout){session.OwnedRotations.Revoke(handle);return Failure("owned_rotation_preparation_outcome_unknown",true,request);}
            return DiagnosticTools.Result(issued);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return Failure(submitted?"owned_rotation_preparation_outcome_unknown":"cancelled_before_submission",submitted,request);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if(handle is not null)session.OwnedRotations.Revoke(handle);
            var failure=new JsonObject{["state"]=submitted?"owned_rotation_preparation_outcome_unknown":"owned_rotation_preparation_rejected",["localArtifactsMayExist"]=submitted,["errorCode"]=error is ReferenceRejected rejected?rejected.Code:"owned_rotation_proof_unavailable",["error"]=error.Message,["executionEnabled"]=false,["retryAllowed"]=false};
            if(request is not null)failure["bridgeRequestId"]=request;
            return DiagnosticTools.Result(failure,true);
        }
        finally{if(gate)session.ReferenceGate.Release();}
    }
    private static CallToolResult Failure(string state,bool submitted,string? request=null)
    {
        var data=new JsonObject{["state"]=state,["localArtifactsMayExist"]=submitted,["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,["retryAllowed"]=false};
        if(request is not null)data["bridgeRequestId"]=request;
        return DiagnosticTools.Result(data,true);
    }
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_owned_rotation"))return [];
        var schema=JsonNode.Parse("""{"type":"object","properties":{"rotationPlanHandle":{"type":"string","pattern":"^[0-9a-f]{32}$","description":"Passive plan lease from this connection."},"workingDocumentBindingHandle":{"type":"string","pattern":"^[0-9a-f]{32}$","description":"Fresh owned binding lease from this connection."}},"required":["rotationPlanHandle","workingDocumentBindingHandle"],"additionalProperties":false}""")!.AsObject();
        return [new StrictTool(McpServerTool.Create(typeof(OwnedRotationTools).GetMethod(nameof(Prepare))!,new OwnedRotationTools(session)),session,schema)];
    }
}
