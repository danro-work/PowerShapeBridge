using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class SavedResultRestoreTools(BridgeSession session)
{
    [McpServerTool(Name="restore_owned_saved_result",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Restore a connection-local savedResultHandle for the current owned saved document using its original successful SaveAs verifier and fresh candidate/one-use active choice. Native rechecks current same-process document, saved file identity/bytes and immutable history; SDK checks the raw response and separate report. No OPEN, SaveAs, activation, selection, geometry command or journal repair. New handle permits separate disk-copy preparation only, never a CAD edit. Full server only; unknown restoration is not retried automatically.")]
    public async Task<CallToolResult> Restore([Description("Original successful SaveAs verification request ID; never another restoration.")]string verificationRequestId,[Description("SHA256 of that original raw verification response.")]string verificationReceiptSha256,[Description("Fresh candidate observation of the active saved result in this connection.")]string candidateDocumentHandle,[Description("Fresh one-use active listing choice in this connection.")]string activeDocumentHandle,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed("restore_owned_saved_result");var timer=Stopwatch.StartNew();bool gate=false,submitted=false;string? request=null,issued=null;
        CallToolResult Fail(string state,string? code=null){var d=new JsonObject{["state"]=state,["retryAllowed"]=false,["cadSaveAttempted"]=false,["contextChangeAttempted"]=false,["geometryMutationAttempted"]=false,["localArtifactsMayExist"]=submitted,["sdkLeaseIssued"]=false};if(code is not null)d["errorCode"]=code;if(request is not null)d["bridgeRequestId"]=request;return DiagnosticTools.Result(d,true);}
        try{
            gate=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!gate)return Fail("queue_timeout_before_submission");cancellationToken.ThrowIfCancellationRequested();
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Fail("maintenance_before_submission");
            _=session.SavedResults.VerifyHistorical(verificationRequestId,verificationReceiptSha256);
            var args=SavedResultObservationProof.Parameters(new(){["verificationRequestId"]=verificationRequestId,["verificationReceiptSha256"]=verificationReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},session.Candidates,session.Documents);
            cancellationToken.ThrowIfCancellationRequested();var remaining=session.Options.Timeout-timer.Elapsed;if(remaining<=TimeSpan.Zero)return Fail("queue_timeout_before_submission");submitted=true;
            var reply=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]="restore_owned_saved_result",["parameters"]=args},cancellationToken,remaining);request=reply["bridgeRequestId"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Fail("saved_result_restore_outcome_unknown");
            if(reply["state"]?.GetValue<string>()!="current_saved_result_observed"){reply["retryAllowed"]=false;return DiagnosticTools.Result(reply,true);}
            var result=session.SavedResults.IssueObserved(reply,args,candidateDocumentHandle);issued=result["savedResultHandle"]!.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout){session.SavedResults.Revoke(issued);return Fail("saved_result_restore_outcome_unknown");}
            return DiagnosticTools.Result(result);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){if(issued is not null)session.SavedResults.Revoke(issued);return Fail(submitted?"saved_result_restore_outcome_unknown":"cancelled_before_submission");}
        catch(Exception e)when(e is ReferenceRejected or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException or NullReferenceException){if(issued is not null)session.SavedResults.Revoke(issued);return Fail(submitted?"saved_result_restore_outcome_unknown":"saved_result_restore_rejected_before_submission",e is ReferenceRejected r?r.Code:"saved_result_proof_unavailable");}
        finally{if(gate)session.ReferenceGate.Release();}
    }
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("restore_owned_saved_result"))return [];
        var fields=new[]{"verificationRequestId","verificationReceiptSha256","candidateDocumentHandle","activeDocumentHandle"};var properties=new JsonObject();var required=new JsonArray();
        foreach(string field in fields){properties[field]=new JsonObject{["type"]="string",["pattern"]=field.EndsWith("Sha256",StringComparison.Ordinal)?"^[0-9a-f]{64}$":"^[0-9a-f]{32}$"};required.Add(field);}
        return [new StrictTool(McpServerTool.Create(new SavedResultRestoreTools(session).Restore),session,new JsonObject{["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=false})];
    }
}
