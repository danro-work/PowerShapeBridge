using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

internal static class OwnedOpenAdoptionProof
{
    internal const string Scope="current_owned_document_after_interrupted_open_v1";
    static void Need(bool ok){if(!ok)throw new ReferenceRejected("owned_open_adoption_proof_differs");}
    static string Text(JsonObject d,string key)=>ArtifactProof.String(d,key,"owned_open_adoption_proof_invalid");
    internal static JsonObject Parameters(JsonObject input,CandidateStore candidates,DocumentHandleStore documents)
    {
        const string code="owned_open_adoption_arguments_invalid";
        if(!ArtifactProof.Fields(input,"openRequestId","openReceiptSha256","openPlanSha256","candidateDocumentHandle","activeDocumentHandle")||!FileArtifacts.Token(input["openPlanSha256"]?.GetValue<string>(),64))throw new ReferenceRejected(code);
        var standard=input.DeepClone().AsObject();standard.Remove("openPlanSha256");
        var args=OwnedExecutionProof.VerificationParameters(standard,candidates,documents,false);args["openPlanSha256"]=input["openPlanSha256"]!.DeepClone();return args;
    }
    internal static JsonObject RestoreParameters(string root,JsonObject input,CandidateStore candidates,DocumentHandleStore documents)
    {
        Need(ArtifactProof.Fields(input,"adoptionRequestId","adoptionReceiptSha256","candidateDocumentHandle","activeDocumentHandle"));
        var initial=SolidScaleProof.Raw(root,Text(input,"adoptionRequestId"),Text(input,"adoptionReceiptSha256")).Value;Need(Text(initial,"state")=="owned_open_document_adopted_observed");
        var originalArgs=initial["adoption"]!["parameters"]!.AsObject();Check(root,initial,originalArgs);
        var standard=new JsonObject();foreach(string k in new[]{"openRequestId","openReceiptSha256","openPlanSha256"})standard[k]=originalArgs[k]!.DeepClone();foreach(string k in new[]{"candidateDocumentHandle","activeDocumentHandle"})standard[k]=input[k]!.DeepClone();
        var args=Parameters(standard,candidates,documents);args["initialAdoptionId"]=initial["adoptionId"]!.DeepClone();args["initialAdoptionSha256"]=initial["adoptionSha256"]!.DeepClone();return args;
    }
    internal static void Check(string root,JsonObject result,JsonObject args)
    {
        bool refresh=args.ContainsKey("initialAdoptionId");string id=Text(result,"bridgeRequestId");Need(FileArtifacts.Token(id)&&Text(result,"state")== (refresh?"owned_open_adoption_refreshed_observed":"owned_open_document_adopted_observed")&&Text(result,"adoptionId")==id&&Text(result,"qualificationScope")==Scope);
        var raw=SolidScaleProof.Raw(root,id);Need(JsonNode.DeepEquals(raw.Value,result));
        var proof=ArtifactProof.Verified(root,"openadoptions",id,Text(result,"adoptionSha256"),16000000);Need(JsonNode.DeepEquals(proof,result["adoption"])&&JsonNode.DeepEquals(proof["parameters"],args)&&Text(proof,"adoptionId")==id&&Text(proof,"state")==Text(result,"state")&&Text(proof,"qualificationScope")==Scope&&ArtifactProof.IntegerIs(proof["schemaVersion"],1));
        foreach(string flag in new[]{"cadMutationAttempted","contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","retryAllowed","originalCommandReturned","originalJournalModified","historicalSavedBytesCertified","fullTopologyEquivalent"})Need(ArtifactProof.Boolean(proof[flag],false)&&ArtifactProof.Boolean(result[flag],false));
        Need(ArtifactProof.Boolean(proof["nativeProviderQualified"],true)&&ArtifactProof.Boolean(result["nativeProviderQualified"],true)&&ArtifactProof.Boolean(proof["checkpointVerified"],true)&&ArtifactProof.IntegerIs(proof["sameContextObservationReads"],2));
        foreach(string key in new[]{"copyId","ownershipSha256","expiresUtc","timestampUtc"})Need(JsonNode.DeepEquals(proof[key],result[key]));
        Need(JsonNode.DeepEquals(result["expiresUtc"],args["expiresUtc"]));
        string opened=Text(args,"openRequestId");_=SolidScaleProof.Raw(root,opened,Text(args,"openReceiptSha256"));var plan=ArtifactProof.Verified(root,"copyopenplans",opened,Text(args,"openPlanSha256"),65536);
        Need(Text(plan,"operation")=="open_working_copy"&&Text(plan,"operationId")==opened&&JsonNode.DeepEquals(plan["copyId"],proof["copyId"])&&JsonNode.DeepEquals(plan["ownershipSha256"],proof["ownershipSha256"])&&JsonNode.DeepEquals(plan["workingPath"],proof["workingPath"]));
        var onceBytes=FileArtifacts.ReadBytes(root,Path.Combine("openadoptions",opened+".once.json"),65536);var once=JsonNode.Parse(FileArtifacts.Parse(onceBytes).GetRawText())!.AsObject();
        Need(ArtifactProof.Fields(once,"schemaVersion","qualificationScope","openRequestId","openReceiptSha256","openPlanSha256","adoptionId","adoptionSha256","copyId","ownershipSha256","noCommandReplay")&&ArtifactProof.IntegerIs(once["schemaVersion"],1)&&Text(once,"qualificationScope")==Scope&&Text(once,"adoptionId")== (refresh?Text(args,"initialAdoptionId"):id)&&JsonNode.DeepEquals(once["adoptionSha256"],refresh?args["initialAdoptionSha256"]:result["adoptionSha256"])&&ArtifactProof.Boolean(once["noCommandReplay"],true));
        if(refresh){var initial=SolidScaleProof.Raw(root,Text(args,"initialAdoptionId")).Value;Need(Text(initial,"state")=="owned_open_document_adopted_observed"&&JsonNode.DeepEquals(initial["adoptionSha256"],args["initialAdoptionSha256"]));Check(root,initial,initial["adoption"]!["parameters"]!.AsObject());}
        foreach(string key in new[]{"openRequestId","openReceiptSha256","openPlanSha256"})Need(JsonNode.DeepEquals(once[key],args[key]));foreach(string key in new[]{"copyId","ownershipSha256"})Need(JsonNode.DeepEquals(once[key],proof[key]));
        var hashes=proof["originalJournalHashes"]!.AsArray();Need(hashes.Count==3);string? previous=null;string[] states=["prepared","execution_claimed","command_started"];
        var journal=FileArtifacts.FixedPath(root,"operations",opened);Need(!File.Exists(Path.Combine(journal,"writer.lock"))&&Directory.GetFiles(journal).Length==3);
        for(int i=0;i<3;i++){var bytes=FileArtifacts.ReadBytes(root,Path.Combine("operations",opened,i.ToString("D6")+".json"),65536);string sha=FileArtifacts.Sha256(bytes);Need(hashes[i]!.GetValue<string>()==sha);var e=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();Need(Text(e,"operationId")==opened&&Text(e,"planSha256")==Text(args,"openPlanSha256")&&ArtifactProof.IntegerIs(e["sequence"],i)&&Text(e,"state")==states[i]&&(i==0?e["previousSha256"] is null:Text(e,"previousSha256")==previous));previous=sha;}
        Need(FileArtifacts.Token(Text(proof,"copyId"))&&FileArtifacts.Token(Text(proof,"ownershipSha256"),64));
        var claimBytes=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",Text(proof,"copyId"),"open-once.json"),65536);Need(FileArtifacts.Sha256(claimBytes)==Text(proof,"originalClaimSha256"));
        foreach(string name in new[]{"mutation-fence.json","geometry-export-fence.json"}){var path=FileArtifacts.FixedPath(root,"workingcopies",Text(proof,"copyId"),name);Need(!File.Exists(path)&&!Directory.Exists(path));}
    }
}

[McpServerToolType]
public sealed class OwnedOpenAdoptionTools(BridgeSession session)
{
    [McpServerTool(Name="adopt_owned_open_document",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Adopt the current owned document after an interrupted three-event OPEN and a terminal worker timeout. Consumes a fresh active listing choice; observes ownership/checkpoint/current saved bytes and the one added window. Issues a new connection-local owned-copy lease. Does not open, activate, select, save, edit CAD, rewrite the original journal or claim its command returned. Full server only; never retry an unknown adoption.")]
    public Task<CallToolResult> Adopt([Description("Original owned OPEN request ID.")]string openRequestId,[Description("SHA256 of its unchanged terminal timeout response.")]string openReceiptSha256,[Description("SHA256 of the original frozen owned OPEN plan.")]string openPlanSha256,[Description("Fresh candidate observation handle from this connection.")]string candidateDocumentHandle,[Description("Fresh one-use active listing choice from this connection.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run(false,new(){["openRequestId"]=openRequestId,["openReceiptSha256"]=openReceiptSha256,["openPlanSha256"]=openPlanSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);
    [McpServerTool(Name="restore_owned_open_adoption",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Restore connection-local access after a previously confirmed initial owned OPEN adoption. Requires that exact successful adoption receipt plus fresh candidate/active choice and new Native retained-document/ownership/current-byte checks. Writes a separate current observation report; preserves original adoption once record and OPEN journal. No CAD commands or repeated adoption. Unavailable in read-only mode; unknown restoration must not be retried automatically.")]
    public Task<CallToolResult> Restore([Description("Confirmed initial adoption request ID, never a refresh or unknown result.")]string adoptionRequestId,[Description("SHA256 of the original successful raw adoption response.")]string adoptionReceiptSha256,[Description("Fresh candidate observation in this connection.")]string candidateDocumentHandle,[Description("Fresh one-use active listing choice.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run(true,new(){["adoptionRequestId"]=adoptionRequestId,["adoptionReceiptSha256"]=adoptionReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);
    private async Task<CallToolResult> Run(bool refresh,JsonObject input,CancellationToken cancellationToken)
    {
        string name=refresh?"restore_owned_open_adoption":"adopt_owned_open_document";session.AssertToolAllowed(name);var timer=Stopwatch.StartNew();bool gate=false,submitted=false;string? request=null,issued=null;
        CallToolResult Fail(string state,string? code=null){var d=new JsonObject{["state"]=state,["retryAllowed"]=false,["cadMutationAttempted"]=false,["contextChangeAttempted"]=false,["localArtifactsMayExist"]=submitted};if(code is not null)d["errorCode"]=code;if(request is not null)d["bridgeRequestId"]=request;return DiagnosticTools.Result(d,true);}
        try{
            gate=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!gate)return Fail("queue_timeout_before_submission");cancellationToken.ThrowIfCancellationRequested();if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Fail("maintenance_before_submission");
            var args=refresh?OwnedOpenAdoptionProof.RestoreParameters(session.Options.BridgeRoot,input,session.Candidates,session.Documents):OwnedOpenAdoptionProof.Parameters(input,session.Candidates,session.Documents);
            cancellationToken.ThrowIfCancellationRequested();var remaining=session.Options.Timeout-timer.Elapsed;if(remaining<=TimeSpan.Zero)return Fail("queue_timeout_before_submission");submitted=true;
            var reply=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=args},cancellationToken,remaining);request=reply["bridgeRequestId"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Fail("owned_open_adoption_outcome_unknown");
            if(reply["state"]?.GetValue<string>()!=(refresh?"owned_open_adoption_refreshed_observed":"owned_open_document_adopted_observed"))return DiagnosticTools.Result(reply,true);
            var result=session.Copies.IssueAdoptedOpen(session.Options.BridgeRoot,reply,args);issued=result["workingCopyHandle"]!.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout){session.Copies.Revoke(issued);return Fail("owned_open_adoption_outcome_unknown");}return DiagnosticTools.Result(result);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){if(issued is not null)session.Copies.Revoke(issued);return Fail(submitted?"owned_open_adoption_outcome_unknown":"cancelled_before_submission");}
        catch(Exception e)when(e is ReferenceRejected or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException or NullReferenceException){if(issued is not null)session.Copies.Revoke(issued);return Fail(submitted?"owned_open_adoption_outcome_unknown":"owned_open_adoption_rejected_before_submission",e is ReferenceRejected r?r.Code:"owned_open_adoption_proof_unavailable");}
        finally{if(gate)session.ReferenceGate.Release();}
    }
    internal static JsonObject Schema(bool refresh=false){string[] fields=refresh?["adoptionRequestId","adoptionReceiptSha256","candidateDocumentHandle","activeDocumentHandle"]:["openRequestId","openReceiptSha256","openPlanSha256","candidateDocumentHandle","activeDocumentHandle"];var schema=SolidQuarterTurnTools.Schema(fields);foreach(string key in fields.Where(k=>k.EndsWith("Sha256",StringComparison.Ordinal)))schema["properties"]![key]=new JsonObject{["type"]="string",["pattern"]="^[0-9a-f]{64}$",["description"]="Exact SHA256 of the original immutable proof."};return schema;}
    public static McpServerTool[] Create(BridgeSession s)=>!s.PreparedEnabled("adopt_owned_open_document")?[]:[new StrictTool(McpServerTool.Create(new OwnedOpenAdoptionTools(s).Adopt),s,Schema()),new StrictTool(McpServerTool.Create(new OwnedOpenAdoptionTools(s).Restore),s,Schema(true))];
}
