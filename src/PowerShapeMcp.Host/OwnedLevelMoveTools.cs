using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class OwnedLevelMoveTools(BridgeSession session)
{
    [McpServerTool(Name="adopt_owned_level_move_execution",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("After MCP reconnect, obtain a connection-local verification-only handle for an original recorded owned level transfer. Requires the original request ID and exact raw receipt SHA, immutable prepared/execution plans, native Boolean return, journal/once proof and owned files. Does not contact CAD, revive a prepared intent, execute, retry or clear a fence. Use fresh inspection/listing handles with verify_owned_level_move afterwards. Full server only.")]
    public Task<CallToolResult> Adopt(
        [Description("Original lowercase 32-hex level-transfer execution request ID; never a path.")]string executionRequestId,
        [Description("SHA256 of the original immutable raw native response; lowercase 64-hex.")]string executionReceiptSha256,
        CancellationToken cancellationToken)=>Run("adopt_owned_level_move_execution",new(){["executionRequestId"]=executionRequestId,["executionReceiptSha256"]=executionReceiptSha256},cancellationToken);
    [McpServerTool(Name="prepare_owned_level_move",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Experimental: prepare one owned SOLID's transfer to an explicitly named existing level. Shows target visibility and the documented selection clear. Reads fresh level data and creates a disabled plan only; no CAD change. Requires this connection's entity set and owned binding; absent from read-only mode.")]
    public Task<CallToolResult> Prepare(
        [Description("Entity set handle for exactly one SOLID in this connection.")]string entitySetHandle,
        [Description("Fresh owned working-document binding handle in this connection.")]string workingDocumentBindingHandle,
        [Description("Exact object: targetLevelNumber integer0..997, targetLevelName, targetActiveRaw integer0/1, selectionAfterPolicy=cleared_by_documented_api, mode=move_in_place. No paths/commands. An inactive target can hide the object.")]JsonElement parameters,
        CancellationToken cancellationToken)=>Run("prepare_owned_level_move",new(){["entitySetHandle"]=entitySetHandle,["workingDocumentBindingHandle"]=workingDocumentBindingHandle,["parameters"]=JsonNode.Parse(parameters.GetRawText())},cancellationToken);

    [McpServerTool(Name="execute_owned_level_move",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false)]
    [Description("Experimental: execute this connection's prepared owned single-SOLID level transfer once. Clears selection as declared; never enables the target level or saves. Consumes the intent before submission, invalidates CAD references, and retains a fence until separate independent verification. Never repeat, inverse or automatically retry an unknown outcome. Use only for the explicitly approved owned-copy operation.")]
    public Task<CallToolResult> Execute([Description("Prepared ownedLevelMoveHandle from this connection.")]string ownedLevelMoveHandle,CancellationToken cancellationToken)=>Run("execute_owned_level_move",new(){["ownedLevelMoveHandle"]=ownedLevelMoveHandle},cancellationToken);

    [McpServerTool(Name="verify_owned_level_move",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Independently verify this connection's original level-transfer execution using fresh candidate/document-listing handles and a new passive level read. May complete its journal and remove its own fence. No CAD command, save, inverse or replay. Does not prove saved/reopened state or complete topology; unavailable in read-only mode.")]
    public Task<CallToolResult> Verify(
        [Description("Level move execution handle returned by the original execute call, including unknown outcomes.")]string levelMoveExecutionHandle,
        [Description("New candidateDocumentHandle from an inspection after command return.")]string candidateDocumentHandle,
        [Description("New one-use active document handle from a listing after command return.")]string activeDocumentHandle,
        CancellationToken cancellationToken)=>Run("verify_owned_level_move",new(){["levelMoveExecutionHandle"]=levelMoveExecutionHandle,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    private async Task<CallToolResult> Run(string name,JsonObject input,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed(name);bool acquired=false,submitted=false;string? execution=null,request=null;var timer=Stopwatch.StartNew();
        TimeSpan Budget()=>session.Options.Timeout-timer.Elapsed;
        async Task<JsonObject> Submit(string recipe,JsonObject args){if(Budget()<=TimeSpan.Zero){request=null;return new(){["state"]="queue_timeout_before_submission",["retryAllowed"]=false};}var response=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=recipe,["parameters"]=args},cancellationToken,Budget());request=response["bridgeRequestId"]?.GetValue<string>();return response;}
        CallToolResult Failed(string state,bool sent,string? handle=null,string? error=null)=>Failure(state,sent,handle,error,name=="execute_owned_level_move",request);
        try
        {
            if(name=="prepare_owned_level_move")_=OwnedLevelMoveStore.Parameters(input["parameters"]!.AsObject());
            acquired=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!acquired)return Failed("queue_timeout_before_submission",false);
            cancellationToken.ThrowIfCancellationRequested();if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Failed("maintenance_before_submission",false);
            if(name=="adopt_owned_level_move_execution")
            {
                var adopted=session.LevelMoves.AdoptExecution(input["executionRequestId"]!.GetValue<string>(),input["executionReceiptSha256"]!.GetValue<string>());
                cancellationToken.ThrowIfCancellationRequested();return DiagnosticTools.Result(adopted);
            }
            if(name=="prepare_owned_level_move")
            {
                var set=input["entitySetHandle"]!.GetValue<string>();var binding=input["workingDocumentBindingHandle"]!.GetValue<string>();
                var args=session.LevelMoves.Sources(set,binding,input["parameters"]!.AsObject());var levels=await Submit("inspect_model_levels",new());
                if(levels["state"]?.GetValue<string>()!="native_levels_read_unqualified")return DiagnosticTools.Result(levels,true);
                cancellationToken.ThrowIfCancellationRequested();args=session.LevelMoves.AddLevelReceipt(args,levels);submitted=true;
                var reply=await Submit(name,args);if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero)return Failed("owned_level_move_preparation_outcome_unknown",true);
                if(reply["state"]?.GetValue<string>()!="owned_level_move_prepared_observed")return DiagnosticTools.Result(reply,true);
                var issued=session.LevelMoves.Issue(reply,args,set,binding);if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero){session.LevelMoves.RevokePrepared(issued["ownedLevelMoveHandle"]!.GetValue<string>());return Failed("owned_level_move_preparation_outcome_unknown",true);}return DiagnosticTools.Result(issued);
            }
            if(name=="execute_owned_level_move")
            {
                cancellationToken.ThrowIfCancellationRequested();var consumed=session.LevelMoves.Consume(input["ownedLevelMoveHandle"]!.GetValue<string>());execution=consumed.ExecutionHandle;
                session.RevokeCadLeases(true);submitted=true;var reply=await Submit(name,consumed.Parameters);session.LevelMoves.Record(execution,reply);
                reply["levelMoveExecutionHandle"]=execution;reply["intentConsumed"]=true;reply["retryAllowed"]=false;
                if(reply["state"]?.GetValue<string>() is "queue_timeout_before_submission" or "maintenance_before_submission" or "cancelled_before_submission" or "failed_before_submission" or "too_many_pending_calls"){reply["cadMutationMayHaveOccurred"]=false;return DiagnosticTools.Result(reply,true);}
                if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero)return Failed("owned_level_move_outcome_unknown",true,execution);
                if(reply["state"]?.GetValue<string>()=="owned_level_move_executed_unverified")session.LevelMoves.CheckExecuted(execution);
                return DiagnosticTools.Result(reply,reply["state"]?.GetValue<string>()!="owned_level_move_executed_unverified");
            }
            execution=input["levelMoveExecutionHandle"]!.GetValue<string>();var original=session.LevelMoves.ExecutionProof(execution);
            original["candidateDocumentHandle"]=input["candidateDocumentHandle"]!.DeepClone();original["activeDocumentHandle"]=input["activeDocumentHandle"]!.DeepClone();
            var parameters=OwnedExecutionProof.VerificationParameters(original,session.Candidates,session.Documents,true);
            var read=await Submit("inspect_model_levels",new());if(read["state"]?.GetValue<string>()!="native_levels_read_unqualified")return DiagnosticTools.Result(read,true);
            _=LevelReadTools.Validate(read);var id=read["bridgeRequestId"]!.GetValue<string>();var raw=FileArtifacts.ReadBytes(session.Options.BridgeRoot,Path.Combine("responses",id+".json"),16000000);var saved=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
            if(!JsonNode.DeepEquals(saved,read))throw new ReferenceRejected("owned_level_move_verification_level_bytes_differ");
            parameters["levelInspectionRequestId"]=id;parameters["levelInspectionSha256"]=FileArtifacts.Sha256(raw);cancellationToken.ThrowIfCancellationRequested();submitted=true;
            var result=await Submit(name,parameters);if(cancellationToken.IsCancellationRequested||Budget()<=TimeSpan.Zero)return Failed("owned_level_move_verification_outcome_unknown",true,execution);
            if(result["state"]?.GetValue<string>()=="owned_level_move_verified_observed")session.LevelMoves.CheckVerified(execution,result,parameters);
            result["levelMoveExecutionHandle"]=execution;return DiagnosticTools.Result(result,result["state"]?.GetValue<string>()!="owned_level_move_verified_observed");
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return Failed(submitted?"owned_level_move_outcome_unknown":"cancelled_before_submission",submitted,execution);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or NullReferenceException)
        {return Failed(submitted?"owned_level_move_outcome_unknown":"owned_level_move_rejected_before_submission",submitted,execution,error is ReferenceRejected rejected?rejected.Code:"owned_level_move_proof_unavailable");}
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private static CallToolResult Failure(string state,bool submitted,string? execution,string? error,bool mutation,string? request)
    {
        var value=new JsonObject{["state"]=state,["localArtifactsMayExist"]=submitted,["cadMutationMayHaveOccurred"]=submitted&&mutation,["retryAllowed"]=false};
        if(execution is not null){value["levelMoveExecutionHandle"]=execution;if(mutation)value["intentConsumed"]=true;}if(request is not null)value["bridgeRequestId"]=request;if(error is not null)value["error"]=error;return DiagnosticTools.Result(value,true);
    }
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_owned_level_move"))return[];var tools=new OwnedLevelMoveTools(session);
        JsonNode Token(string name)=>StrictTool.Token(name,name switch{"executionRequestId"=>"Original native level-transfer execution request ID, not a new intent.","entitySetHandle"=>"This connection's observed entity set containing exactly one SOLID.","workingDocumentBindingHandle"=>"Fresh binding of the owned working document in this connection.","levelMoveExecutionHandle"=>"Execution handle from the original one-use level transfer, including unknown outcomes.","candidateDocumentHandle"=>"New candidate document inspection after command return.","activeDocumentHandle"=>"New one-use active-document choice from a listing after command return.",_=>"Opaque handle issued by this connection."})["properties"]![name]!.DeepClone();
        JsonObject Schema(params (string Name,JsonNode Value)[] fields){var props=new JsonObject();var required=new JsonArray();foreach(var (key,value) in fields){props[key]=value;required.Add(key);}return new(){["type"]="object",["properties"]=props,["required"]=required,["additionalProperties"]=false};}
        var parameters=JsonNode.Parse("""{"type":"object","properties":{"targetLevelNumber":{"type":"integer","minimum":0,"maximum":997},"targetLevelName":{"type":"string","minLength":1,"maxLength":4096},"targetActiveRaw":{"type":"integer","enum":[0,1]},"selectionAfterPolicy":{"type":"string","enum":["cleared_by_documented_api"]},"mode":{"type":"string","enum":["move_in_place"]}},"required":["targetLevelNumber","targetLevelName","targetActiveRaw","selectionAfterPolicy","mode"],"additionalProperties":false}""")!;
        return [new StrictTool(McpServerTool.Create(tools.Adopt),session,Schema(("executionRequestId",Token("executionRequestId")),("executionReceiptSha256",JsonNode.Parse("""{"type":"string","pattern":"^[0-9a-f]{64}$","description":"Exact original raw native response SHA256."}""")!))),
            new StrictTool(McpServerTool.Create(tools.Prepare),session,Schema(("entitySetHandle",Token("entitySetHandle")),("workingDocumentBindingHandle",Token("workingDocumentBindingHandle")),("parameters",parameters)),args=>_=OwnedLevelMoveStore.Parameters(args["parameters"]!.AsObject())),
            new StrictTool(McpServerTool.Create(tools.Execute),session,StrictTool.Token("ownedLevelMoveHandle","Prepared owned single-SOLID level move in this connection.")),
            new StrictTool(McpServerTool.Create(tools.Verify),session,Schema(("levelMoveExecutionHandle",Token("levelMoveExecutionHandle")),("candidateDocumentHandle",Token("candidateDocumentHandle")),("activeDocumentHandle",Token("activeDocumentHandle"))))];
    }
}
