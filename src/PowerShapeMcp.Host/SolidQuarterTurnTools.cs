using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class SolidQuarterTurnTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_owned_solid_quarter_turn",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Prepare an experimental signed quarter turn: exactly one SOLID, the only entity in an owned working copy, mm/factor1, +90 or -90 degrees around an explicit normalized WORLD axis through the explicitly planned pivot. Binds this connection's passive rotation plan and working document, captures pinned face UV samples/normals, and freezes proofs. No CAD change, execution or save. Other angles, multiple entities and other types are rejected; unavailable in read-only mode.")]
    public Task<CallToolResult> Prepare([Description("Passive rotationPlanHandle from this connection, +90 or -90 WORLD axis (including oblique directions) with an explicit pivot in document_raw units.")]string rotationPlanHandle,[Description("Fresh owned workingDocumentBindingHandle in this connection.")]string workingDocumentBindingHandle,CancellationToken cancellationToken)=>Run("prepare_owned_solid_quarter_turn",new(){["rotationPlanHandle"]=rotationPlanHandle,["workingDocumentBindingHandle"]=workingDocumentBindingHandle},cancellationToken);

    [McpServerTool(Name="execute_owned_solid_quarter_turn",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false)]
    [Description("Rotate the prepared owned single SOLID exactly once by the planned +90 or -90 WORLD axis around the explicit pivot, within the mm/factor1 scope. Consumes the preparation and CAD leases before submission. One native RotateItem; no selection, save or activation. Success is unverified/fenced until a separate fresh verifier. Never repeat, inverse or automatically retry unknown outcomes. Use only for an approved owned-copy operation; unavailable in read-only mode.")]
    public Task<CallToolResult> Execute([Description("Unconsumed ownedRotationHandle issued by prepare_owned_solid_quarter_turn in this connection.")]string ownedRotationHandle,CancellationToken cancellationToken)=>Run("execute_owned_solid_quarter_turn",new(){["ownedRotationHandle"]=ownedRotationHandle},cancellationToken);

    [McpServerTool(Name="verify_owned_solid_quarter_turn",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Independently verify the original single-SOLID signed WORLD quarter turn using its exact immutable execution receipt and fresh candidate/listing handles. Reads the original pinned face UV positions/normals, COG/invariants/bounds, ownership/files and native journal. May complete the journal and release only its matching fence. No RotateItem, replay, inverse, selection, save or activation. Sampled observations do not prove full topology or saved/reopened persistence. Full server only.")]
    public Task<CallToolResult> Verify([Description("Original native rotation execution request ID, never a new intent.")]string executionRequestId,[Description("Exact lowercase SHA256 of the original raw execution receipt.")]string executionReceiptSha256,[Description("Fresh candidateDocumentHandle from inspection after command return.")]string candidateDocumentHandle,[Description("Fresh one-use active document choice from a listing after command return.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run("verify_owned_solid_quarter_turn",new(){["executionRequestId"]=executionRequestId,["executionReceiptSha256"]=executionReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    Task<CallToolResult> Run(string name,JsonObject input,CancellationToken cancellationToken)=>RunCore(name,input,cancellationToken,false);
    internal Task<CallToolResult> RunGeneral(string name,JsonObject input,CancellationToken cancellationToken)=>RunCore(name,input,cancellationToken,true);
    async Task<CallToolResult> RunCore(string name,JsonObject input,CancellationToken cancellationToken,bool general)
    {
        var store=general?session.SolidRotations:session.SolidQuarterTurns;var prefix=general?"solid_rotation":"solid_quarter_turn";
        session.AssertToolAllowed(name);bool acquired=false,submitted=false,consumed=false;string? request=null,issuedHandle=null;var timer=Stopwatch.StartNew();bool mutation=name.StartsWith("execute_",StringComparison.Ordinal);
        CallToolResult Failed(string state,string? code=null){var o=new JsonObject{["state"]=state,["localArtifactsMayExist"]=submitted,["cadMutationMayHaveOccurred"]=submitted&&mutation,["intentConsumed"]=consumed,["retryAllowed"]=false};if(request is not null)o["bridgeRequestId"]=request;if(code is not null)o["errorCode"]=code;return DiagnosticTools.Result(o,true);}
        try
        {
            acquired=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!acquired)return Failed("queue_timeout_before_submission");
            cancellationToken.ThrowIfCancellationRequested();if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Failed("maintenance_before_submission");
            JsonObject args;
            if(name.StartsWith("prepare_",StringComparison.Ordinal))args=session.OwnedRotations.PrepareParameters(input["rotationPlanHandle"]!.GetValue<string>(),input["workingDocumentBindingHandle"]!.GetValue<string>());
            else if(mutation)
            {
                var handle=input["ownedRotationHandle"]!.GetValue<string>();args=store.Consume(handle,cancellationToken);consumed=true;
                cancellationToken.ThrowIfCancellationRequested();if(timer.Elapsed>=session.Options.Timeout)return Failed("queue_timeout_before_submission");
                session.RevokeCadLeases(true);
            }
            else
            {
                _=SolidQuarterTurnProof.Execution(session.Options.BridgeRoot,input["executionRequestId"]!.GetValue<string>(),input["executionReceiptSha256"]!.GetValue<string>(),general:general);
                args=OwnedExecutionProof.VerificationParameters(input,session.Candidates,session.Documents,true);
            }
            cancellationToken.ThrowIfCancellationRequested();var remaining=session.Options.Timeout-timer.Elapsed;if(remaining<=TimeSpan.Zero)return Failed("queue_timeout_before_submission");
            submitted=true;var reply=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=args},cancellationToken,remaining);request=reply["bridgeRequestId"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Failed(prefix+"_outcome_unknown");
            if(name.StartsWith("prepare_",StringComparison.Ordinal))
            {
                if(reply["state"]?.GetValue<string>()!="owned_rotation_prepared_observed")return DiagnosticTools.Result(reply,true);
                var prepared=SolidQuarterTurnProof.Preparation(session.Options.BridgeRoot,new(){["ownedRotationId"]=reply["ownedRotationId"]!.DeepClone(),["ownedRotationSha256"]=reply["ownedRotationSha256"]!.DeepClone()},general);
                var raw=SolidQuarterTurnProof.Raw(session.Options.BridgeRoot,request!);if(!JsonNode.DeepEquals(raw.Value,reply))throw new ReferenceRejected("solid_quarter_turn_preparation_bytes_differ");
                var issued=session.OwnedRotations.Issue(reply,args,input["rotationPlanHandle"]!.GetValue<string>(),input["workingDocumentBindingHandle"]!.GetValue<string>());issuedHandle=issued["ownedRotationHandle"]!.GetValue<string>();store.Pin(issuedHandle,prepared);
                if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout){store.Revoke(issuedHandle);return Failed(prefix+"_preparation_outcome_unknown");}return DiagnosticTools.Result(issued);
            }
            if(mutation)
            {
                if(reply["state"]?.GetValue<string>()!="owned_rotation_executed_unverified"){reply["intentConsumed"]=true;reply["retryAllowed"]=false;return DiagnosticTools.Result(reply,true);}
                var sha=SolidQuarterTurnProof.CheckedExecution(session.Options.BridgeRoot,reply,args,general);reply["executionReceiptSha256"]=sha;reply["intentConsumed"]=true;reply["retryAllowed"]=false;return DiagnosticTools.Result(reply);
            }
            if(reply["state"]?.GetValue<string>()!=SolidQuarterTurnProof.VerificationStateFor(reply["qualificationScope"]?.GetValue<string>()??""))return DiagnosticTools.Result(reply,true);
            SolidQuarterTurnProof.CheckedVerification(session.Options.BridgeRoot,reply,args,general);return DiagnosticTools.Result(reply);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){if(issuedHandle is not null)store.Revoke(issuedHandle);return Failed(submitted?prefix+"_outcome_unknown":"cancelled_before_submission");}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException or NullReferenceException)
        {if(issuedHandle is not null)store.Revoke(issuedHandle);return Failed(submitted?prefix+"_outcome_unknown":prefix+"_rejected_before_submission",error is ReferenceRejected rejected?rejected.Code:"solid_quarter_turn_proof_unavailable");}
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_owned_solid_quarter_turn"))return[];var tools=new SolidQuarterTurnTools(session);
        return[new StrictTool(McpServerTool.Create(tools.Prepare),session,Schema("rotationPlanHandle","workingDocumentBindingHandle")),new StrictTool(McpServerTool.Create(tools.Execute),session,Schema("ownedRotationHandle")),new StrictTool(McpServerTool.Create(tools.Verify),session,Schema("executionRequestId","executionReceiptSha256","candidateDocumentHandle","activeDocumentHandle"))];
    }
    internal static JsonObject Schema(params string[] fields)
    {
        JsonNode Token(string n)=>StrictTool.Token(n,"Connection-local proof handle or original fixed request identity.")["properties"]![n]!.DeepClone();
        var p=new JsonObject();var r=new JsonArray();foreach(var name in fields){p[name]=name=="executionReceiptSha256"?JsonNode.Parse("""{"type":"string","pattern":"^[0-9a-f]{64}$","description":"Exact SHA256 of the original immutable raw execution receipt."}"""):Token(name);r.Add(name);}return new(){["type"]="object",["properties"]=p,["required"]=r,["additionalProperties"]=false};
    }
}
