using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
namespace PowerShapeMcp;

internal static class OwnedOpenAccessRestoreProof
{
    internal const string Scope="current_owned_document_after_verified_open_v1";
    private static void Need(bool ok){if(!ok)throw new ReferenceRejected("owned_open_access_restore_proof_differs");}
    private static string Text(JsonObject o,string k)=>ArtifactProof.String(o,k,"owned_open_access_restore_proof_invalid");
    internal static (JsonObject Verified,JsonArray JournalHashes) History(string root,string id,string sha)
    {
        var verified=SolidScaleProof.Raw(root,id,sha).Value;
        Need(Text(verified,"state")=="owned_open_verified_observed"&&Text(verified,"recordedJournalState")=="succeeded_verified");
        foreach(string k in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","retryAllowed"})Need(ArtifactProof.Boolean(verified[k],false));
        string opened=Text(verified,"openOperationId");var receipt=SolidScaleProof.Raw(root,opened,Text(verified,"openReceiptSha256")).Value;
        var plan=ArtifactProof.Verified(root,"copyopenplans",opened,Text(verified,"planSha256"),65536);
        var originalArgs=new JsonObject();foreach(var k in new[]{"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256","windowNumber","modelId","expiresUtc"})originalArgs[k]=plan[k]!.DeepClone();
        OwnedExecutionProof.OpenReceipt(root,receipt,originalArgs);
        foreach(var k in new[]{"copyId","ownershipSha256","planSha256"})Need(JsonNode.DeepEquals(verified[k],receipt[k]));
        var claimPath=FileArtifacts.FixedPath(root,"workingcopies",Text(verified,"copyId"),"open-once.json");var claim=JsonNode.Parse(FileArtifacts.ReadBytes(root,Path.GetRelativePath(root,claimPath),65536))!.AsObject();
        Need(ArtifactProof.Fields(claim,"operationId","planSha256","copyId","ownershipSha256","automaticReplayForbidden")&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true)&&Text(claim,"operationId")==opened);
        foreach(var k in new[]{"copyId","ownershipSha256","planSha256"})Need(JsonNode.DeepEquals(claim[k],verified[k]));
        var folder=FileArtifacts.FixedPath(root,"operations",opened);Need(!File.Exists(Path.Combine(folder,"writer.lock"))&&Directory.GetFiles(folder).Length==6);
        string[] states=["prepared","execution_claimed","command_started","command_returned","executed_unverified","succeeded_verified"];string? previous=null;var hashes=new JsonArray();JsonObject? last=null;
        for(int i=0;i<6;i++){
            string name=i.ToString("D6")+".json";var bytes=FileArtifacts.ReadBytes(root,Path.Combine("operations",opened,name),65536);var e=JsonNode.Parse(bytes)!.AsObject();
            Need(ArtifactProof.Fields(e,"schemaVersion","operationId","planSha256","sequence","previousSha256","state","timestampUtc","details")&&ArtifactProof.IntegerIs(e["schemaVersion"],1)&&ArtifactProof.IntegerIs(e["sequence"],i)&&Text(e,"operationId")==opened&&Text(e,"planSha256")==Text(verified,"planSha256")&&Text(e,"state")==states[i]&&(previous is null?e["previousSha256"]is null:Text(e,"previousSha256")==previous));
            previous=FileArtifacts.Sha256(bytes);hashes.Add(new JsonObject{["name"]=name,["sha256"]=previous});last=e;
        }
        Need(ArtifactProof.Boolean(last!["details"]!["noCommandReplay"],true)&&JsonNode.DeepEquals(last["details"]!["evidence"],verified["evidence"]));
        var evidence=verified["evidence"]!.AsArray();Need(evidence.Count==3);
        foreach(var e in evidence){var row=e!.AsObject();_=SolidScaleProof.Raw(root,Text(row,"requestId"),Text(row,"sha256"));}
        Need(JsonNode.DeepEquals(evidence[2]!["requestId"],verified["openOperationId"])&&JsonNode.DeepEquals(evidence[2]!["sha256"],verified["openReceiptSha256"]));
        return(verified,hashes);
    }
    internal static JsonObject Parameters(BridgeSession session,string id,string sha,string candidate,string active)
    {
        var original=History(session.Options.BridgeRoot,id,sha).Verified;
        var input=new JsonObject{["openRequestId"]=original["openOperationId"]!.DeepClone(),["openReceiptSha256"]=original["openReceiptSha256"]!.DeepClone(),["candidateDocumentHandle"]=candidate,["activeDocumentHandle"]=active};
        var args=OwnedExecutionProof.VerificationParameters(input,session.Candidates,session.Documents,false);args["verificationRequestId"]=id;args["verificationReceiptSha256"]=sha;return args;
    }
    internal static void Check(BridgeSession session,JsonObject result,JsonObject args)
    {
        string root=session.Options.BridgeRoot,id=Text(result,"bridgeRequestId");var original=History(root,Text(args,"verificationRequestId"),Text(args,"verificationReceiptSha256"));
        Need(Text(result,"state")=="owned_open_access_restored_observed"&&Text(result,"qualificationScope")==Scope&&Text(result,"restorationId")==id&&ArtifactProof.Boolean(result["nativeProviderQualified"],true)&&JsonNode.DeepEquals(SolidScaleProof.Raw(root,id).Value,result));
        var report=ArtifactProof.Verified(root,"openaccessrestorations",id,Text(result,"restorationSha256"),16000000);
        Need(JsonNode.DeepEquals(report,result["restoration"])&&ArtifactProof.IntegerIs(report["schemaVersion"],1)&&Text(report,"restorationId")==id&&Text(report,"qualificationScope")==Scope&&JsonNode.DeepEquals(report["parameters"],args)&&JsonNode.DeepEquals(report["originalJournalHashes"],original.JournalHashes));
        foreach(var k in new[]{"state","timestampUtc","copyId","ownershipSha256","expiresUtc","bindingId","bindingSha256","binding","observedModel","observedPropertiesSha256","windowContextSha256","workingSha256","nativeProviderQualified"})Need(JsonNode.DeepEquals(result[k],report[k]));
        foreach(var k in new[]{"cadMutationAttempted","contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","retryAllowed","originalJournalModified","fullTopologyEquivalent"})Need(ArtifactProof.Boolean(result[k],false)&&ArtifactProof.Boolean(report[k],false));
        foreach(var k in new[]{"historicalWindowPostconditionReapplied","historicalSavedContentCertified"})Need(ArtifactProof.Boolean(report[k],false));Need(ArtifactProof.Boolean(result["noCommandReplay"],true)&&ArtifactProof.Boolean(report["noCommandReplay"],true));
        foreach(var k in new[]{"copyId","ownershipSha256"})Need(JsonNode.DeepEquals(result[k],original.Verified[k]));Need(JsonNode.DeepEquals(result["expiresUtc"],args["expiresUtc"]));
        var binding=session.Bindings.VerifyReceipt(result);foreach(var k in new[]{"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","expiresUtc"})Need(JsonNode.DeepEquals(binding[k],k is "copyId" or "ownershipSha256"?result[k]:args[k]));Need(Text(binding,"bindingId")==id);
        var current=SolidScaleProof.Raw(root,Text(args,"inspectionRequestId"),Text(args,"inspectionSha256")).Value;var listing=SolidScaleProof.Raw(root,Text(args,"listingRequestId"),Text(args,"listingSha256")).Value;
        Need(JsonNode.DeepEquals(current["model"],result["observedModel"])&&JsonNode.DeepEquals(listing["activeWindow"]!["modelId"],original.Verified["openedWindow"]!["modelId"]));
        foreach(var name in new[]{"mutation-fence.json","geometry-export-fence.json"}){var p=FileArtifacts.FixedPath(root,"workingcopies",Text(result,"copyId"),name);Need(!File.Exists(p)&&!Directory.Exists(p));}
    }
}

[McpServerToolType]
public sealed class OwnedOpenAccessRestoreTools(BridgeSession session)
{
    [McpServerTool(Name="restore_verified_owned_open",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Restore current connection access to an already-open clean owned working copy from its original successful generic OPEN verifier. Rechecks immutable six-event OPEN history, source/checkpoint/saved working identities, fresh active same-process document and current stable windows; later unrelated windows are allowed. Writes a separate observation and passive binding only. Does not repeat OPEN, activate, select, save, transform, close, rewrite history or certify historical topology. Full server only; never retry unknown restoration.")]
    public async Task<CallToolResult> Restore([Description("Original successful generic owned OPEN verification request ID.")]string verificationRequestId,[Description("Exact SHA256 of its original raw Native response.")]string verificationReceiptSha256,[Description("Fresh same-connection active candidate document handle.")]string candidateDocumentHandle,[Description("Fresh one-use active listing choice.")]string activeDocumentHandle,CancellationToken cancellationToken)
    {
        const string recipe="restore_verified_owned_open";session.AssertToolAllowed(recipe);var timer=Stopwatch.StartNew();bool gate=false,submitted=false;string? request=null,copy=null;
        CallToolResult Fail(string state,string? code=null){var d=new JsonObject{["state"]=state,["retryAllowed"]=false,["cadMutationAttempted"]=false,["contextChangeAttempted"]=false,["localArtifactsMayExist"]=submitted};if(request is not null)d["bridgeRequestId"]=request;if(code is not null)d["errorCode"]=code;return DiagnosticTools.Result(d,true);}
        try{
            gate=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!gate)return Fail("queue_timeout_before_submission");cancellationToken.ThrowIfCancellationRequested();if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Fail("maintenance_before_submission");
            var args=OwnedOpenAccessRestoreProof.Parameters(session,verificationRequestId,verificationReceiptSha256,candidateDocumentHandle,activeDocumentHandle);cancellationToken.ThrowIfCancellationRequested();var remaining=session.Options.Timeout-timer.Elapsed;if(remaining<=TimeSpan.Zero)return Fail("queue_timeout_before_submission");submitted=true;
            var result=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=recipe,["parameters"]=args},cancellationToken,remaining);request=result["bridgeRequestId"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Fail("owned_open_access_restore_outcome_unknown");if(result["state"]?.GetValue<string>()!="owned_open_access_restored_observed")return DiagnosticTools.Result(result,true);OwnedOpenAccessRestoreProof.Check(session,result,args);
            var issued=session.Copies.IssueRestoredOpenAccess(result);copy=issued["workingCopyHandle"]!.GetValue<string>();var bindingReceipt=result.DeepClone().AsObject();bindingReceipt["state"]="working_document_bound_observed";var bindingArgs=session.Bindings.PrepareParameters(candidateDocumentHandle,copy);bindingArgs["expiresUtc"]=args["expiresUtc"]!.DeepClone();
            var bound=session.Bindings.Issue(bindingReceipt,bindingArgs,candidateDocumentHandle,copy,LeaseTime.Parse(args["expiresUtc"]!.GetValue<string>(),"open_access_restore_expired"));
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout){session.RevokeCadLeases(true);return Fail("owned_open_access_restore_outcome_unknown");}
            foreach(var k in new[]{"workingCopyHandle","workingCopyLifetimeSeconds"})result[k]=issued[k]!.DeepClone();foreach(var k in new[]{"workingDocumentBindingHandle","bindingLifetimeSeconds"})result[k]=bound[k]!.DeepClone();return DiagnosticTools.Result(result);
        }catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){if(copy is not null)session.RevokeCadLeases(true);return Fail(submitted?"owned_open_access_restore_outcome_unknown":"cancelled_before_submission");}
        catch(Exception e)when(e is ReferenceRejected or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException or NullReferenceException){if(copy is not null)session.RevokeCadLeases(true);return Fail(submitted?"owned_open_access_restore_outcome_unknown":"owned_open_access_restore_rejected",e is ReferenceRejected r?r.Code:"owned_open_access_restore_proof_unavailable");}
        finally{if(gate)session.ReferenceGate.Release();}
    }
    internal static JsonObject Schema(){var s=SolidQuarterTurnTools.Schema("verificationRequestId","verificationReceiptSha256","candidateDocumentHandle","activeDocumentHandle");s["properties"]!["verificationReceiptSha256"]=new JsonObject{["type"]="string",["pattern"]="^[0-9a-f]{64}$",["description"]="SHA256 of the original successful generic OPEN verifier."};return s;}
    public static McpServerTool[] Create(BridgeSession s)=>!s.PreparedEnabled("restore_verified_owned_open")?[]:[new StrictTool(McpServerTool.Create(new OwnedOpenAccessRestoreTools(s).Restore),s,Schema())];
}
