using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

internal static class SavedResultCloseProof
{
    static string Text(JsonObject o,string k)=>ArtifactProof.String(o,k,"saved_result_close_proof_invalid");
    static void Need(bool yes){if(!yes)throw new ReferenceRejected("saved_result_close_proof_differs");}
    static JsonObject Raw(BridgeSession s,string id,string? sha=null)=>SolidScaleProof.Raw(s.Options.BridgeRoot,id,sha).Value;
    static string Marker(BridgeSession s,string target,string name)=>FileArtifacts.FixedPath(s.Options.BridgeRoot,"savedresults",target,name);
    static DateTimeOffset Expiry(params JsonObject[] values)=>values.Select(o=>LeaseTime.Parse(Text(o,"expiresUtc"),"saved_result_close_expired")).Min();
    static JsonObject Source(BridgeSession s,JsonObject refs,bool fresh)
    {
        var raw=Raw(s,Text(refs,"observationRequestId"),Text(refs,"observationReceiptSha256"));s.SavedResults.VerifyObservedSource(raw,raw["observation"]!["parameters"]!.AsObject(),fresh);return raw["observation"]!.AsObject();
    }
    internal static JsonObject Parameters(BridgeSession s,string handle,string candidate,string active,string disposition)
    {
        Need(disposition=="discard_owned_saved_result");var refs=s.SavedResults.Parameters(handle);Need(ArtifactProof.Fields(refs,"observationRequestId","observationReceiptSha256"));var source=Source(s,refs,true);
        var fresh=SavedCopyCloseProof.Fresh(s,new(){["candidateDocumentHandle"]=candidate,["activeDocumentHandle"]=active},out var choice);
        var observed=ArtifactProof.Verified(s.Options.BridgeRoot,"responses",Text(fresh,"inspectionRequestId"),Text(fresh,"inspectionSha256"),16777216);
        var snapshot=ArtifactProof.Verified(s.Options.BridgeRoot,"responses",Text(source["parameters"]!.AsObject(),"inspectionRequestId"),Text(source["parameters"]!.AsObject(),"inspectionSha256"),16777216);
        foreach(var k in new[]{"processId","processStartedUtc","windowsSessionId","model","entities","entityCount","levelsContainingEntities","workplanes","contextBefore","contextAfter","selectionBefore","selectionAfter","lengthUnitsRaw","lengthUnitFactorRaw","generalToleranceRaw"})Need(JsonNode.DeepEquals(observed[k],snapshot[k]));
        refs["expiresUtc"]=LeaseTime.Format(Expiry(fresh,choice,source));foreach(var k in new[]{"inspectionRequestId","inspectionSha256","listingRequestId","listingSha256"})refs[k]=fresh[k]!.DeepClone();refs["discardOwnedSavedResult"]=true;return refs;
    }
    internal static JsonObject VerifyParameters(BridgeSession s,string id,string sha,string candidate,string active)
    {
        Need(FileArtifacts.Token(id)&&FileArtifacts.Token(sha,64));var fresh=SavedCopyCloseProof.Fresh(s,new(){["candidateDocumentHandle"]=candidate,["activeDocumentHandle"]=active},out var choice);fresh["expiresUtc"]=LeaseTime.Format(Expiry(fresh,choice));fresh["closeRequestId"]=id;fresh["closeReceiptSha256"]=sha;return fresh;
    }
    static JsonObject Plan(BridgeSession s,JsonObject original)
    {
        var id=Text(original,"operationId");var target=Text(original,"targetId");var claimBytes=FileArtifacts.ReadBytes(s.Options.BridgeRoot,Path.Combine("savedresults",target,"result-close-once.json"),65536);var claim=JsonNode.Parse(claimBytes)!.AsObject();Need(FileArtifacts.Sha256(claimBytes)==Text(original,"executionClaimSha256")&&ArtifactProof.Fields(claim,"operationId","planSha256","automaticReplayForbidden")&&Text(claim,"operationId")==id&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true)&&JsonNode.DeepEquals(claim["planSha256"],original["planSha256"]));
        var plan=ArtifactProof.Verified(s.Options.BridgeRoot,"savedresultcloseplans",id,Text(claim,"planSha256"),65536);Need(ArtifactProof.Fields(plan,"observationRequestId","observationReceiptSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256","expiresUtc","discardOwnedSavedResult","schemaVersion","operationId","operation","targetId","copyId","savedResult","targetModelId","before","savedFileHashesBefore","command","fullTopologyEquivalent")&&ArtifactProof.IntegerIs(plan["schemaVersion"],1)&&Text(plan,"operationId")==id&&Text(plan,"operation")=="close_owned_saved_result"&&Text(plan,"targetId")==target&&Text(plan,"command")=="FILE CLOSE SELECTED YES"&&ArtifactProof.Boolean(plan["discardOwnedSavedResult"],true)&&ArtifactProof.Boolean(plan["fullTopologyEquivalent"],false));
        var source=Source(s,plan,false);foreach(var k in new[]{"targetId","copyId","savedResult"})Need(JsonNode.DeepEquals(source[k],plan[k])&&JsonNode.DeepEquals(original[k],plan[k]));Need(JsonNode.DeepEquals(source["model"]!["id"],plan["targetModelId"]));return plan;
    }
    static void Common(BridgeSession s,JsonObject result)
    {
        Need(JsonNode.DeepEquals(Raw(s,Text(result,"bridgeRequestId")),result)&&ArtifactProof.Boolean(result["nativeProviderQualified"],true)&&ArtifactProof.Boolean(result["queueQualifiedRoute"],true));foreach(var k in new[]{"cadSaveAttempted","geometryMutationAttempted","fullTopologyEquivalent"})Need(ArtifactProof.Boolean(result[k],false));
    }
    static JsonObject Journal(BridgeSession s,string id,string planSha,bool verified)
    {
        var journal=ArtifactProof.Journal(s.Options.BridgeRoot,id);Need(Text(journal,"recordedState")== (verified?"succeeded_verified":"executed_unverified")&&Text(journal,"planSha256")==planSha&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&journal["events"]!.AsArray().Count==(verified?6:5));var returned=journal["events"]!.AsArray().Where(e=>e!["state"]!.GetValue<string>()=="command_returned").ToArray();Need(returned.Length==1&&Text(returned[0]!["details"]!.AsObject(),"nativeRequestId")==id);return journal;
    }
    internal static void Execution(BridgeSession s,JsonObject result,JsonObject args)
    {
        Common(s,result);Need(Text(result,"state")=="owned_saved_result_closed_unverified"&&Text(result,"operationId")==Text(result,"bridgeRequestId")&&ArtifactProof.Boolean(result["contextChangeAttempted"],true)&&ArtifactProof.Boolean(result["savedFilesUnchanged"],true));var plan=Plan(s,result);Need(args.All(p=>JsonNode.DeepEquals(p.Value,plan[p.Key]))&&JsonNode.DeepEquals(result["before"],plan["before"]));SavedCopyCloseProof.WindowResult(plan["before"]!.AsObject(),result["after"]!.AsObject(),plan["targetModelId"]!.GetValue<int>());_=Journal(s,Text(result,"operationId"),Text(result,"planSha256"),false);
        var bytes=FileArtifacts.ReadBytes(s.Options.BridgeRoot,Path.Combine("savedresults",Text(result,"targetId"),"result-close-fence.json"),65536);var fence=JsonNode.Parse(bytes)!.AsObject();Need(FileArtifacts.Sha256(bytes)==Text(result,"executionFenceSha256")&&Text(fence,"operationId")==Text(result,"operationId")&&JsonNode.DeepEquals(fence["planSha256"],result["planSha256"]));
    }
    internal static void Verification(BridgeSession s,JsonObject result,JsonObject args)
    {
        Common(s,result);Need(Text(result,"state")=="owned_saved_result_close_verified"&&Text(result,"verificationId")==Text(result,"bridgeRequestId")&&Text(result,"operationId")==Text(args,"closeRequestId")&&ArtifactProof.Boolean(result["contextChangeAttempted"],false)&&ArtifactProof.Boolean(result["savedFilesUnchanged"],true)&&ArtifactProof.Boolean(result["noCommandReplay"],true));var original=Raw(s,Text(args,"closeRequestId"),Text(args,"closeReceiptSha256"));Need(new[]{"owned_saved_result_closed_unverified","owned_saved_result_close_outcome_unknown"}.Contains(Text(original,"state"))&&ArtifactProof.Boolean(original["nativeProviderQualified"],true)&&ArtifactProof.Boolean(original["contextChangeAttempted"],true));var plan=Plan(s,original);foreach(var k in new[]{"targetId","copyId","savedResult","targetModelId","planSha256"})Need(JsonNode.DeepEquals(original[k],result[k]));
        var after=ArtifactProof.Verified(s.Options.BridgeRoot,"responses",Text(args,"listingRequestId"),Text(args,"listingSha256"),16777216);SavedCopyCloseProof.WindowResult(plan["before"]!.AsObject(),after,plan["targetModelId"]!.GetValue<int>());var journal=Journal(s,Text(args,"closeRequestId"),Text(result,"planSha256"),true);var evidence=new JsonArray();foreach(var pair in new[]{("closeRequestId","closeReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})evidence.Add(new JsonObject{["requestId"]=args[pair.Item1]!.DeepClone(),["sha256"]=args[pair.Item2]!.DeepClone()});var terminal=journal["events"]!.AsArray().Last()!["details"]!.AsObject();Need(JsonNode.DeepEquals(terminal["evidence"],evidence)&&ArtifactProof.Boolean(terminal["noCommandReplay"],true));var fence=Marker(s,Text(plan,"targetId"),"result-close-fence.json");Need(!File.Exists(fence)&&!Directory.Exists(fence));
    }
}

[McpServerToolType]
public sealed class SavedResultCloseTools(BridgeSession session)
{
    [McpServerTool(Name="execute_owned_saved_result_close",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false),Description("Close once an explicitly disposable owned saved Result in its original named active-last single-view window, after fresh saved-result restoration and file/provenance/context guards. Preserves the verified saved file, other windows and original history. Consumes CAD leases before IPC; requires separate close verification. No Save, geometry, activation, close-all or retry. Full only; cannot close arbitrary/unsaved documents.")]
    public Task<CallToolResult> Execute([Description("Current observed saved-result handle from restore_owned_saved_result in this connection.")]string savedResultHandle,[Description("Fresh candidate of the disposable current saved Result.")]string candidateDocumentHandle,[Description("Fresh one-use active window choice.")]string activeDocumentHandle,[Description("Explicit decision discard_owned_saved_result for this owned saved window only.")]string disposition,CancellationToken cancellationToken)=>Run(false,savedResultHandle,null,null,candidateDocumentHandle,activeDocumentHandle,disposition,cancellationToken);
    [McpServerTool(Name="verify_owned_saved_result_close",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Independently verify one original returned owned saved Result close using fresh remaining-context observations, immutable original plan/claim/receipt, saved files and journal. May append terminal success and release only its matching result-close fence. No close, save, activation, geometry, replay or new edit binding. Missing authoritative command return cannot be inferred. Full only.")]
    public Task<CallToolResult> Verify([Description("Original Native saved-result close request ID.")]string closeRequestId,[Description("Exact SHA256 of its immutable raw receipt.")]string closeReceiptSha256,[Description("Fresh candidate of the remaining active document after close.")]string candidateDocumentHandle,[Description("Fresh one-use choice of that remaining active window.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run(true,null,closeRequestId,closeReceiptSha256,candidateDocumentHandle,activeDocumentHandle,null,cancellationToken);
    async Task<CallToolResult> Run(bool verify,string? handle,string? id,string? sha,string candidate,string active,string? disposition,CancellationToken ct)
    {
        string name=verify?"verify_owned_saved_result_close":"execute_owned_saved_result_close";session.AssertToolAllowed(name);bool gate=false,submitted=false;string? request=null;var timer=Stopwatch.StartNew();
        CallToolResult Fail(string state){var value=new JsonObject{["state"]=state,["retryAllowed"]=false,["contextChangeMayHaveOccurred"]=submitted&&!verify,["verificationMayBePartial"]=submitted&&verify,["cadSaveAttempted"]=false,["geometryMutationAttempted"]=false};if(request is not null)value["bridgeRequestId"]=request;return DiagnosticTools.Result(value,true);}
        try{gate=await session.ReferenceGate.WaitAsync(session.Options.Timeout,ct);if(!gate)return Fail("queue_timeout_before_submission");ct.ThrowIfCancellationRequested();if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Fail("maintenance_before_submission");var args=verify?SavedResultCloseProof.VerifyParameters(session,id!,sha!,candidate,active):SavedResultCloseProof.Parameters(session,handle!,candidate,active,disposition!);var remaining=session.Options.Timeout-timer.Elapsed;if(remaining<=TimeSpan.Zero)return Fail("queue_timeout_before_submission");ct.ThrowIfCancellationRequested();if(!verify)session.RevokeCadLeases(true);submitted=true;
            var result=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=args},ct,remaining);request=result["bridgeRequestId"]?.GetValue<string>();if(ct.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Fail("owned_saved_result_close_outcome_unknown");
            if(result["state"]?.GetValue<string>()==(verify?"owned_saved_result_close_verified":"owned_saved_result_closed_unverified")){if(verify)SavedResultCloseProof.Verification(session,result,args);else SavedResultCloseProof.Execution(session,result,args);if(ct.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Fail("owned_saved_result_close_outcome_unknown");return DiagnosticTools.Result(result);}result["retryAllowed"]=false;return DiagnosticTools.Result(result,true);
        }catch(OperationCanceledException)when(ct.IsCancellationRequested){return Fail(submitted?"owned_saved_result_close_outcome_unknown":"cancelled_before_submission");}
        catch(Exception e)when(e is ReferenceRejected or InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException or NullReferenceException){return Fail(submitted?"owned_saved_result_close_outcome_unknown":"owned_saved_result_close_rejected");}
        finally{if(gate)session.ReferenceGate.Release();}
    }
    internal static JsonObject Schema(bool verify){var fields=verify?new[]{"closeRequestId","closeReceiptSha256","candidateDocumentHandle","activeDocumentHandle"}:new[]{"savedResultHandle","candidateDocumentHandle","activeDocumentHandle","disposition"};var p=new JsonObject();var r=new JsonArray();foreach(var field in fields){p[field]=field=="disposition"?new JsonObject{["type"]="string",["enum"]=new JsonArray("discard_owned_saved_result")}:new JsonObject{["type"]="string",["pattern"]=field.EndsWith("Sha256",StringComparison.Ordinal)?"^[0-9a-f]{64}$":"^[0-9a-f]{32}$"};r.Add(field);}return new(){["type"]="object",["properties"]=p,["required"]=r,["additionalProperties"]=false};}
    public static McpServerTool[] Create(BridgeSession s)=>!s.ExecutionEnabled("execute_owned_saved_result_close")?[]:[new StrictTool(McpServerTool.Create(new SavedResultCloseTools(s).Execute),s,Schema(false)),new StrictTool(McpServerTool.Create(new SavedResultCloseTools(s).Verify),s,Schema(true))];
}
