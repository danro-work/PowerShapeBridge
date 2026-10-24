using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal static class SavedCopyCloseProof
{
    private const string Code="saved_copy_close_proof_invalid";
    private static string Text(JsonObject o,string k)=>ArtifactProof.String(o,k,Code);
    private static void Need(bool ok){if(!ok)throw new ReferenceRejected(Code);}
    private static JsonObject Read(string root,string id)=>JsonNode.Parse(FileArtifacts.ReadBytes(root,"responses/"+id+".json",16777216))!.AsObject();
    private static JsonObject Pinned(string root,JsonObject refs,string id,string sha)=>ArtifactProof.Verified(root,"responses",Text(refs,id),Text(refs,sha),16777216);
    private static DateTimeOffset Expiry(params JsonObject[] sources)=>sources.Select(o=>LeaseTime.Parse(Text(o,"expiresUtc"),Code)).Min();
    internal static JsonObject Fresh(BridgeSession s,JsonObject input,out JsonObject choice)
    {
        var observation=s.Candidates.DocumentParameters(Text(input,"candidateDocumentHandle"));choice=s.Documents.Consume(Text(input,"activeDocumentHandle"));
        var raw=Pinned(s.Options.BridgeRoot,observation,"inspectionRequestId","inspectionSha256");var listing=Pinned(s.Options.BridgeRoot,choice,"listingRequestId","listingSha256");
        Need(JsonNode.DeepEquals(raw["model"]?["id"],choice["modelId"])&&JsonNode.DeepEquals(listing["activeWindow"]?["modelId"],choice["modelId"])&&JsonNode.DeepEquals(listing["activeWindow"]?["windowNumber"],choice["windowNumber"])&&ArtifactProof.IntegerIs(listing["activeWindow"]?["selectedRaw"],1));
        foreach(var key in new[]{"processId","processStartedUtc","windowsSessionId"})Need(JsonNode.DeepEquals(raw[key],listing[key]));
        observation["listingRequestId"]=choice["listingRequestId"]!.DeepClone();observation["listingSha256"]=choice["listingSha256"]!.DeepClone();return observation;
    }
    internal static JsonObject ExecuteParameters(BridgeSession s,JsonObject input)
    {
        Need(ArtifactProof.Fields(input,"workingDocumentBindingHandle","candidateDocumentHandle","activeDocumentHandle","disposition")&&Text(input,"disposition")=="discard_owned_saved_copy");
        foreach(var key in new[]{"workingDocumentBindingHandle","candidateDocumentHandle","activeDocumentHandle"})Need(FileArtifacts.Token(Text(input,key)));
        var bindingRefs=s.Bindings.ValidationParameters(Text(input,"workingDocumentBindingHandle"));var root=s.Options.BridgeRoot;var bindingId=Text(bindingRefs,"bindingId");
        var binding=ArtifactProof.Verified(root,"documentbindings",bindingId,Text(bindingRefs,"bindingSha256"),65536);var verified=Read(root,bindingId);
        Need(Text(verified,"state")=="owned_saved_reopen_verified_observed"&&Text(verified,"recordedJournalState")=="succeeded_verified"&&JsonNode.DeepEquals(verified["bindingId"],bindingRefs["bindingId"])&&JsonNode.DeepEquals(verified["bindingSha256"],bindingRefs["bindingSha256"])&&JsonNode.DeepEquals(verified["binding"],binding));
        s.Bindings.VerifyReceipt(verified);foreach(var key in new[]{"copyId","ownershipSha256"})Need(JsonNode.DeepEquals(binding[key],verified[key]));
        var original=Pinned(root,verified,"reopenOperationId","reopenReceiptSha256");original["bridgeRequestId"]=verified["reopenOperationId"]!.DeepClone();OwnedReopenProof.Execution(s,original,null,true);
        var fresh=Fresh(s,input,out var choice);var raw=Pinned(root,fresh,"inspectionRequestId","inspectionSha256");Need(JsonNode.DeepEquals(raw["model"],verified["model"]));
        var bound=Pinned(root,binding,"inspectionRequestId","inspectionSha256");foreach(var key in new[]{"processId","processStartedUtc","windowsSessionId","model","entities","entityCount","contextBefore","contextAfter","selectionBefore","selectionAfter","workplanes","levelsContainingEntities","lengthUnitsRaw","lengthUnitFactorRaw","generalToleranceRaw"})Need(JsonNode.DeepEquals(raw[key],bound[key]));
        var expiry=Expiry(fresh,choice,binding);var result=new JsonObject{["copyId"]=binding["copyId"]!.DeepClone(),["ownershipSha256"]=binding["ownershipSha256"]!.DeepClone(),["reopenVerificationRequestId"]=bindingId,["reopenVerificationReceiptSha256"]=FileArtifacts.Sha256(FileArtifacts.ReadBytes(root,"responses/"+bindingId+".json",16777216)),["discardOwnedTestCopy"]=true,["expiresUtc"]=LeaseTime.Format(expiry)};
        foreach(var key in new[]{"inspectionRequestId","inspectionSha256","listingRequestId","listingSha256"})result[key]=fresh[key]!.DeepClone();return result;
    }
    internal static JsonObject VerifyParameters(BridgeSession s,JsonObject input)
    {
        Need(ArtifactProof.Fields(input,"closedCopyId","closeRequestId","closeReceiptSha256","candidateDocumentHandle","activeDocumentHandle"));foreach(var key in input.Select(p=>p.Key))Need(FileArtifacts.Token(Text(input,key),key.EndsWith("Sha256",StringComparison.Ordinal)?64:32));
        var fresh=Fresh(s,input,out var choice);fresh["expiresUtc"]=LeaseTime.Format(Expiry(fresh,choice));fresh["copyId"]=input["closedCopyId"]!.DeepClone();fresh["closeRequestId"]=input["closeRequestId"]!.DeepClone();fresh["closeReceiptSha256"]=input["closeReceiptSha256"]!.DeepClone();return fresh;
    }
    private static JsonObject Raw(BridgeSession s,JsonObject result)
    {
        var id=Text(result,"bridgeRequestId");Need(FileArtifacts.Token(id));var original=Read(s.Options.BridgeRoot,id);original["bridgeRequestId"]=id;
        Need(JsonNode.DeepEquals(result,original)&&ArtifactProof.Boolean(result["nativeProviderQualified"],true)&&ArtifactProof.Boolean(result["queueQualifiedRoute"],true)&&ArtifactProof.Boolean(result["coreOnlyQualification"],false)&&ArtifactProof.Boolean(result["cadSaveAttempted"],false)&&ArtifactProof.Boolean(result["geometryMutationAttempted"],false));return original;
    }
    private static JsonObject Plan(BridgeSession s,string operation,JsonObject receipt,string copy)
    {
        var root=s.Options.BridgeRoot;var claim=JsonNode.Parse(FileArtifacts.ReadBytes(root,"workingcopies/"+copy+"/close-once.json",65536))!.AsObject();Need(FileArtifacts.Sha256(FileArtifacts.ReadBytes(root,"workingcopies/"+copy+"/close-once.json",65536))==Text(receipt,"executionClaimSha256"));
        Need(ArtifactProof.Fields(claim,"operationId","planSha256","automaticReplayForbidden")&&Text(claim,"operationId")==operation&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true));
        var plan=ArtifactProof.Verified(root,"ownedcloseplans",operation,Text(claim,"planSha256"),16777216);
        Need(ArtifactProof.Fields(plan,"copyId","ownershipSha256","reopenVerificationRequestId","reopenVerificationReceiptSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256","expiresUtc","discardOwnedTestCopy","schemaVersion","operationId","operation","before","savedFileHashesBefore","command","fullTopologyEquivalent")&&ArtifactProof.IntegerIs(plan["schemaVersion"],1)&&Text(plan,"copyId")==copy&&Text(plan,"operationId")==operation&&Text(plan,"operation")=="close_disposable_saved_reopen_copy"&&Text(plan,"command")=="FILE CLOSE SELECTED YES"&&ArtifactProof.Boolean(plan["discardOwnedTestCopy"],true)&&ArtifactProof.Boolean(plan["fullTopologyEquivalent"],false));
        if(receipt["planSha256"]is not null)Need(JsonNode.DeepEquals(receipt["planSha256"],claim["planSha256"]));return plan;
    }
    private static JsonObject WindowKey(JsonObject row){var copy=row.DeepClone().AsObject();copy.Remove("nativeWindowId");copy.Remove("selectedRaw");return copy;}
    internal static void WindowResult(JsonObject before,JsonObject after,int target)
    {
        foreach(var key in new[]{"processId","processStartedUtc","windowsSessionId"})Need(JsonNode.DeepEquals(before[key],after[key]));var old=before["windows"]!.AsArray().Select(v=>v!.AsObject()).ToArray();var rows=after["windows"]!.AsArray().Select(v=>v!.AsObject()).ToArray();
        Need(old.Count(w=>ArtifactProof.IntegerIs(w["modelId"],target))==1&&rows.Length==old.Length-1&&rows.Length>=1&&ArtifactProof.IntegerIs(before["windowCount"],old.Length)&&ArtifactProof.IntegerIs(after["windowCount"],rows.Length)&&!rows.Any(w=>ArtifactProof.IntegerIs(w["modelId"],target))&&rows.Select(w=>w["nativeWindowId"]!.ToJsonString()).Distinct().Count()==rows.Length&&rows.Count(w=>ArtifactProof.IntegerIs(w["selectedRaw"],1))==1);
        foreach(var row in old.Where(w=>!ArtifactProof.IntegerIs(w["modelId"],target)))Need(rows.Count(w=>JsonNode.DeepEquals(WindowKey(w),WindowKey(row)))==1);Need(rows.Any(w=>JsonNode.DeepEquals(WindowKey(w),WindowKey(after["activeWindow"]!.AsObject()))&&ArtifactProof.IntegerIs(w["selectedRaw"],1)));
    }
    internal static void Execution(BridgeSession s,JsonObject result,JsonObject parameters)
    {
        Raw(s,result);Need(Text(result,"state")=="owned_saved_copy_closed_unverified"&&ArtifactProof.Boolean(result["contextChangeAttempted"],true)&&Text(result,"operationId")==Text(result,"bridgeRequestId")&&JsonNode.DeepEquals(result["copyId"],parameters["copyId"])&&ArtifactProof.Boolean(result["savedFilesUnchanged"],true)&&ArtifactProof.Boolean(result["checkpointVerified"],true));
        var operation=Text(result,"operationId");var plan=Plan(s,operation,result,Text(parameters,"copyId"));Need(parameters.All(p=>JsonNode.DeepEquals(p.Value,plan[p.Key]))&&JsonNode.DeepEquals(result["before"],plan["before"]));var original=Pinned(s.Options.BridgeRoot,plan,"reopenVerificationRequestId","reopenVerificationReceiptSha256");WindowResult(plan["before"]!.AsObject(),result["after"]!.AsObject(),original["model"]!["id"]!.GetValue<int>());
        var journal=ArtifactProof.Journal(s.Options.BridgeRoot,operation);Need(Text(journal,"recordedState")=="executed_unverified"&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&journal["events"]!.AsArray().Count==5&&JsonNode.DeepEquals(journal["planSha256"],result["planSha256"]));Returned(journal,operation);
        var fence=JsonNode.Parse(FileArtifacts.ReadBytes(s.Options.BridgeRoot,"workingcopies/"+Text(parameters,"copyId")+"/mutation-fence.json",65536))!.AsObject();Need(FileArtifacts.Sha256(FileArtifacts.ReadBytes(s.Options.BridgeRoot,"workingcopies/"+Text(parameters,"copyId")+"/mutation-fence.json",65536))==Text(result,"executionFenceSha256")&&Text(fence,"operationId")==operation&&JsonNode.DeepEquals(fence["planSha256"],result["planSha256"]));
    }
    private static void Returned(JsonObject journal,string operation){var returned=journal["events"]!.AsArray().Where(e=>e!["state"]!.GetValue<string>()=="command_returned").ToArray();Need(returned.Length==1&&Text(returned[0]!["details"]!.AsObject(),"nativeRequestId")==operation);}
    internal static void Verification(BridgeSession s,JsonObject result,JsonObject parameters)
    {
        Raw(s,result);Need(Text(result,"state")=="owned_saved_copy_close_verified"&&Text(result,"verificationId")==Text(result,"bridgeRequestId")&&Text(result,"operationId")==Text(parameters,"closeRequestId")&&JsonNode.DeepEquals(result["copyId"],parameters["copyId"])&&Text(result,"recordedJournalState")=="succeeded_verified"&&ArtifactProof.Boolean(result["contextChangeAttempted"],false)&&ArtifactProof.Boolean(result["savedFilesUnchanged"],true)&&ArtifactProof.Boolean(result["noCommandReplay"],true));
        var original=Pinned(s.Options.BridgeRoot,parameters,"closeRequestId","closeReceiptSha256");Need(new[]{"owned_saved_copy_closed_unverified","owned_saved_copy_close_outcome_unknown"}.Contains(Text(original,"state"))&&ArtifactProof.Boolean(original["contextChangeAttempted"],true)&&ArtifactProof.Boolean(original["nativeProviderQualified"],true));var plan=Plan(s,Text(parameters,"closeRequestId"),original,Text(parameters,"copyId"));
        var before=plan["before"]!.AsObject();var after=Pinned(s.Options.BridgeRoot,parameters,"listingRequestId","listingSha256");var verified=Pinned(s.Options.BridgeRoot,plan,"reopenVerificationRequestId","reopenVerificationReceiptSha256");WindowResult(before,after,verified["model"]!["id"]!.GetValue<int>());
        var journal=ArtifactProof.Journal(s.Options.BridgeRoot,Text(parameters,"closeRequestId"));Need(Text(journal,"recordedState")=="succeeded_verified"&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&journal["events"]!.AsArray().Count==6&&JsonNode.DeepEquals(journal["planSha256"],result["planSha256"]));Returned(journal,Text(parameters,"closeRequestId"));
        var evidence=new JsonArray();foreach(var pair in new[]{("closeRequestId","closeReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})evidence.Add(new JsonObject{["requestId"]=parameters[pair.Item1]!.DeepClone(),["sha256"]=parameters[pair.Item2]!.DeepClone()});var terminal=journal["events"]!.AsArray().Last()!["details"]!.AsObject();Need(JsonNode.DeepEquals(terminal["evidence"],evidence)&&ArtifactProof.Boolean(terminal["noCommandReplay"],true));
        var fence=FileArtifacts.FixedPath(s.Options.BridgeRoot,"workingcopies",Text(parameters,"copyId"),"mutation-fence.json");Need(!File.Exists(fence)&&!Directory.Exists(fence));
    }
}
