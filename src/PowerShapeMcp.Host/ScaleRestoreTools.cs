using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

internal static class ScaleRestoreProof
{
    internal const string Scope="current_owned_saved_reopen_after_verified_scale_v1";
    private static string Text(JsonObject o,string k)=>ArtifactProof.String(o,k,"scale_restore_proof_invalid");
    private static void Need(bool yes){if(!yes)throw new ReferenceRejected("scale_restore_proof_differs");}
    internal static JsonObject Historical(BridgeSession s,string id,string sha)
    {
        var original=SolidScaleProof.Raw(s.Options.BridgeRoot,id,sha).Value;
        Need(Text(original,"state")=="owned_solid_scale_verified_observed");
        var diagnostic=ArtifactProof.Verified(s.Options.BridgeRoot,"solidscaleverifications",id,Text(original,"diagnosticSha256"),16000000);
        var oldArgs=new JsonObject();foreach(var k in new[]{"executionRequestId","executionReceiptSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256"})oldArgs[k]=diagnostic[k]!.DeepClone();
        SolidScaleProof.CheckedVerification(s.Options.BridgeRoot,original,oldArgs);
        var execution=SolidScaleProof.Execution(s.Options.BridgeRoot,Text(oldArgs,"executionRequestId"),Text(oldArgs,"executionReceiptSha256"),true);
        var refs=execution["plan"]!["references"]!.AsObject();var packet=ArtifactProof.Verified(s.Options.BridgeRoot,"ownedscales",Text(refs,"ownedScaleId"),Text(refs,"ownedScaleSha256"),65536);
        var binding=ArtifactProof.Verified(s.Options.BridgeRoot,"documentbindings",Text(packet,"bindingId"),Text(packet,"bindingSha256"),65536);
        var opened=SolidScaleProof.Raw(s.Options.BridgeRoot,Text(packet,"bindingId")).Value;Need(JsonNode.DeepEquals(opened["binding"],binding));
        var evidence=opened["evidence"]!.AsArray();Need(evidence.Count==3);
        var listing=ArtifactProof.Verified(s.Options.BridgeRoot,"responses",Text(evidence[2]!.AsObject(),"requestId"),Text(evidence[2]!.AsObject(),"sha256"),16777216);
        var openArgs=new JsonObject{["reopenRequestId"]=opened["reopenOperationId"]!.DeepClone(),["reopenReceiptSha256"]=opened["reopenReceiptSha256"]!.DeepClone(),["inspectionRequestId"]=evidence[1]!["requestId"]!.DeepClone(),["inspectionSha256"]=evidence[1]!["sha256"]!.DeepClone(),["listingRequestId"]=evidence[2]!["requestId"]!.DeepClone(),["listingSha256"]=evidence[2]!["sha256"]!.DeepClone(),["modelId"]=listing["activeWindow"]!["modelId"]!.DeepClone(),["windowNumber"]=listing["activeWindow"]!["windowNumber"]!.DeepClone(),["expiresUtc"]=opened["expiresUtc"]!.DeepClone()};
        OwnedReopenProof.Verification(s,opened,openArgs);return oldArgs;
    }
    internal static JsonObject Parameters(BridgeSession s,string id,string sha,string candidate,string active)
    {
        var history=Historical(s,id,sha);
        var standard=new JsonObject{["executionRequestId"]=history["executionRequestId"]!.DeepClone(),["executionReceiptSha256"]=history["executionReceiptSha256"]!.DeepClone(),["candidateDocumentHandle"]=candidate,["activeDocumentHandle"]=active};
        var args=OwnedExecutionProof.VerificationParameters(standard,s.Candidates,s.Documents,true);args["scaleVerificationRequestId"]=id;args["scaleVerificationReceiptSha256"]=sha;return args;
    }
    internal static void Check(BridgeSession s,JsonObject result,JsonObject args)
    {
        var id=Text(result,"bridgeRequestId");Need(JsonNode.DeepEquals(SolidScaleProof.Raw(s.Options.BridgeRoot,id).Value,result)&&Text(result,"state")=="owned_solid_scale_restored_observed"&&Text(result,"restorationId")==id&&Text(result,"restorationScope")==Scope&&Text(result,"recordedJournalState")=="succeeded_verified"&&ArtifactProof.Boolean(result["noCommandReplay"],true));
        foreach(var k in new[]{"geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","retryAllowed","fullTopologyEquivalent","originalJournalModified","geometrySaved"})Need(ArtifactProof.Boolean(result[k],false));
        _=Historical(s,Text(args,"scaleVerificationRequestId"),Text(args,"scaleVerificationReceiptSha256"));
        var originalVerifier=SolidScaleProof.Raw(s.Options.BridgeRoot,Text(args,"scaleVerificationRequestId"),Text(args,"scaleVerificationReceiptSha256")).Value;foreach(var k in new[]{"copyId","ownershipSha256"})Need(JsonNode.DeepEquals(result[k],originalVerifier[k]));
        var report=ArtifactProof.Verified(s.Options.BridgeRoot,"solidscalerestorations",id,Text(result,"restorationSha256"),16777216);
        Need(ArtifactProof.Fields(report,"schemaVersion","restorationId","scope","parameters","copyId","ownershipSha256","bindingId","bindingSha256","diagnosticSha256","model","expiresUtc","noCommandReplay","originalJournalModified","geometrySaved","fullTopologyEquivalent")&&ArtifactProof.IntegerIs(report["schemaVersion"],1)&&Text(report,"scope")==Scope&&JsonNode.DeepEquals(report,result["restoration"])&&JsonNode.DeepEquals(report["parameters"],args));
        foreach(var k in new[]{"restorationId","copyId","ownershipSha256","bindingId","bindingSha256","diagnosticSha256","model","expiresUtc","noCommandReplay","originalJournalModified","geometrySaved","fullTopologyEquivalent"})Need(JsonNode.DeepEquals(report[k],result[k]));
        var diagnostic=ArtifactProof.Verified(s.Options.BridgeRoot,"solidscaleverifications",id,Text(result,"diagnosticSha256"),16000000);
        foreach(var k in new[]{"executionRequestId","executionReceiptSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256"})Need(JsonNode.DeepEquals(diagnostic[k],args[k]));
        var binding=s.Bindings.VerifyReceipt(result);Need(Text(result,"bindingId")==id);
        foreach(var k in new[]{"copyId","ownershipSha256","expiresUtc"})Need(JsonNode.DeepEquals(binding[k],result[k]));foreach(var k in new[]{"inspectionRequestId","inspectionSha256"})Need(JsonNode.DeepEquals(binding[k],args[k]));
        var current=ArtifactProof.Verified(s.Options.BridgeRoot,"responses",Text(args,"inspectionRequestId"),Text(args,"inspectionSha256"),16000000);Need(JsonNode.DeepEquals(current["model"],result["model"]));
        foreach(var name in new[]{"mutation-fence.json","geometry-export-fence.json"}){var path=FileArtifacts.FixedPath(s.Options.BridgeRoot,"workingcopies",Text(result,"copyId"),name);Need(!File.Exists(path)&&!Directory.Exists(path));}
    }
}

[McpServerToolType]
public sealed class ScaleRestoreTools(BridgeSession session)
{
    [McpServerTool(Name="restore_owned_solid_scale",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Restore current connection copy/binding leases for an existing owned saved-reopen single SOLID after its successful qualified scale verifier. Rechecks original successful OPEN/scale history, retained files, fresh process/document/windows and sampled post-scale geometry. Writes a NEW observation/binding only. No OPEN, scale, save, selection, activation, fence release or journal repair. Geometry remains unsaved; separate SaveAs required. Full mode only; unknown/late/cancel issues no authority and is not retried.")]
    public async Task<CallToolResult> Restore([Description("Original successful qualified SOLID scale verifier Native request ID.")]string scaleVerificationRequestId,[Description("Exact SHA256 of its immutable original Native receipt.")]string scaleVerificationReceiptSha256,[Description("Fresh candidate of the same currently active owned scaled copy.")]string candidateDocumentHandle,[Description("Fresh one-use active window choice.")]string activeDocumentHandle,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed("restore_owned_solid_scale");bool gate=false,submitted=false;string? request=null,copy=null;var timer=Stopwatch.StartNew();
        CallToolResult Fail(string state){var o=new JsonObject{["state"]=state,["retryAllowed"]=false,["localArtifactsMayExist"]=submitted,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};if(request is not null)o["bridgeRequestId"]=request;return DiagnosticTools.Result(o,true);}
        try{
            gate=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!gate)return Fail("queue_timeout_before_submission");cancellationToken.ThrowIfCancellationRequested();if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Fail("maintenance_before_submission");
            var args=ScaleRestoreProof.Parameters(session,scaleVerificationRequestId,scaleVerificationReceiptSha256,candidateDocumentHandle,activeDocumentHandle);cancellationToken.ThrowIfCancellationRequested();var remaining=session.Options.Timeout-timer.Elapsed;if(remaining<=TimeSpan.Zero)return Fail("queue_timeout_before_submission");
            submitted=true;var result=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]="restore_owned_solid_scale",["parameters"]=args},cancellationToken,remaining);request=result["bridgeRequestId"]?.GetValue<string>();if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Fail("owned_solid_scale_restore_outcome_unknown");
            if(result["state"]?.GetValue<string>()!="owned_solid_scale_restored_observed")return DiagnosticTools.Result(result,true);ScaleRestoreProof.Check(session,result,args);
            var issued=session.Copies.IssueRestoredScale(result);copy=issued["workingCopyHandle"]!.GetValue<string>();var bindingReceipt=result.DeepClone().AsObject();bindingReceipt["state"]="working_document_bound_observed";
            var bindingArgs=session.Bindings.PrepareParameters(candidateDocumentHandle,copy);bindingArgs["expiresUtc"]=args["expiresUtc"]!.DeepClone();var bound=session.Bindings.Issue(bindingReceipt,bindingArgs,candidateDocumentHandle,copy,LeaseTime.Parse(args["expiresUtc"]!.GetValue<string>(),"scale_restore_expired"));
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout){session.RevokeCadLeases(true);return Fail("owned_solid_scale_restore_outcome_unknown");}
            foreach(var k in new[]{"workingCopyHandle","workingCopyLifetimeSeconds"})result[k]=issued[k]!.DeepClone();foreach(var k in new[]{"workingDocumentBindingHandle","bindingLifetimeSeconds"})result[k]=bound[k]!.DeepClone();return DiagnosticTools.Result(result);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){if(copy is not null)session.RevokeCadLeases(true);return Fail(submitted?"owned_solid_scale_restore_outcome_unknown":"cancelled_before_submission");}
        catch(Exception e)when(e is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException or NullReferenceException){if(copy is not null)session.RevokeCadLeases(true);var response=Fail(submitted?"owned_solid_scale_restore_outcome_unknown":"owned_solid_scale_restore_rejected");return response;}
        finally{if(gate)session.ReferenceGate.Release();}
    }
    internal static JsonObject Schema(){var schema=SolidQuarterTurnTools.Schema("scaleVerificationRequestId","scaleVerificationReceiptSha256","candidateDocumentHandle","activeDocumentHandle");schema["properties"]!["scaleVerificationReceiptSha256"]=new JsonObject{["type"]="string",["pattern"]="^[0-9a-f]{64}$",["description"]="Exact SHA256 of the original successful scale verifier receipt."};return schema;}
    public static McpServerTool[] Create(BridgeSession s)=>!s.PreparedEnabled("restore_owned_solid_scale")?[]:[new StrictTool(McpServerTool.Create(new ScaleRestoreTools(s).Restore),s,Schema())];
}
