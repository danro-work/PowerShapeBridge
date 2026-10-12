using System.ComponentModel;
using System.Diagnostics;
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
    private static JsonObject Failure(string state,bool artifacts)=>new(){["state"]=state,["executionEnabled"]=false,["cadSaveAttempted"]=false,
        ["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["localArtifactsMayExist"]=artifacts,["retryAllowed"]=false};
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_owned_save_as"))return [];
        var method=typeof(OwnedSaveTools).GetMethod(nameof(PrepareOwnedSaveAs))!;
        var schema=new JsonObject{["type"]="object",["properties"]=new JsonObject{["workingDocumentBindingHandle"]=new JsonObject{["type"]="string",["pattern"]="^[0-9a-f]{32}$",["description"]="Fresh working document binding handle in this connection."}},
            ["required"]=new JsonArray("workingDocumentBindingHandle"),["additionalProperties"]=false};
        return [new StrictTool(McpServerTool.Create(method,new OwnedSaveTools(session)),session,schema)];
    }
}
