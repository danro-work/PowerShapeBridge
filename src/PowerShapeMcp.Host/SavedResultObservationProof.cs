using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal static class SavedResultObservationProof
{
    internal const string Scope="current_saved_result_same_process_document_observation_v1";
    static void Need(bool ok){if(!ok)throw new ReferenceRejected("current_saved_result_proof_differs");}
    static string Text(JsonObject d,string key)=>ArtifactProof.String(d,key,"current_saved_result_proof_invalid");
    internal static JsonObject Parameters(JsonObject input,CandidateStore candidates,DocumentHandleStore documents)
    {
        Need(ArtifactProof.Fields(input,"verificationRequestId","verificationReceiptSha256","candidateDocumentHandle","activeDocumentHandle"));
        // Reuse the one-use active choice/minimum-expiry path, then use the
        // explicit original-verifier names expected by the Native observation.
        var standard=new JsonObject{["saveRequestId"]=input["verificationRequestId"]?.DeepClone(),["saveReceiptSha256"]=input["verificationReceiptSha256"]?.DeepClone(),["candidateDocumentHandle"]=input["candidateDocumentHandle"]?.DeepClone(),["activeDocumentHandle"]=input["activeDocumentHandle"]?.DeepClone()};
        var args=OwnedExecutionProof.VerificationParameters(standard,candidates,documents,false,true);
        args["verificationRequestId"]=args["saveRequestId"]!.DeepClone();args["verificationReceiptSha256"]=args["saveReceiptSha256"]!.DeepClone();args.Remove("saveRequestId");args.Remove("saveReceiptSha256");return args;
    }
    internal static JsonObject Check(string root,JsonObject reply,JsonObject args,OwnedSavedResultStore saved,DateTimeOffset now,bool fresh=true)
    {
        Need(ArtifactProof.Fields(args,"verificationRequestId","verificationReceiptSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256","modelId","windowNumber","expiresUtc"));
        foreach(string key in new[]{"verificationRequestId","inspectionRequestId","listingRequestId"})Need(FileArtifacts.Token(Text(args,key)));
        foreach(string key in new[]{"verificationReceiptSha256","inspectionSha256","listingSha256"})Need(FileArtifacts.Token(Text(args,key),64));
        string id=Text(reply,"bridgeRequestId");var raw=SolidScaleProof.Raw(root,id);Need(JsonNode.DeepEquals(raw.Value,reply)&&Text(reply,"state")=="current_saved_result_observed"&&Text(reply,"observationId")==id&&ArtifactProof.Boolean(reply["nativeProviderQualified"],true)&&ArtifactProof.Boolean(reply["executionAuthority"],false)&&ArtifactProof.Boolean(reply["sdkLeaseIssued"],false));
        var report=ArtifactProof.Verified(root,"savedresultobservations",id,Text(reply,"observationSha256"),16777216);
        Need(ArtifactProof.Fields(report,"schemaVersion","observationId","state","qualificationScope","parameters","nativeProviderQualified","executionAuthority","sdkLeaseIssued","cadSaveAttempted","contextChangeAttempted","geometryMutationAttempted","originalJournalModified","historicalWindowInventoryRecertified","fullTopologyEquivalent","originalVerificationRequestId","originalVerificationReceiptSha256","saveOperationId","targetId","targetSha256","savedResult","copyId","ownershipSha256","model","observedPropertiesSha256","currentWindowContextSha256","currentSavedFileHashes","sameContextObservationReads","expiresUtc","timestampUtc")&&JsonNode.DeepEquals(report,reply["observation"])&&JsonNode.DeepEquals(args,report["parameters"]));
        Need(ArtifactProof.IntegerIs(report["schemaVersion"],1)&&ArtifactProof.IntegerIs(report["sameContextObservationReads"],2)&&Text(report,"observationId")==id&&Text(report,"state")=="current_saved_result_observed"&&Text(report,"qualificationScope")==Scope&&ArtifactProof.Boolean(report["nativeProviderQualified"],true));
        foreach(string key in new[]{"executionAuthority","sdkLeaseIssued","cadSaveAttempted","contextChangeAttempted","geometryMutationAttempted","originalJournalModified","historicalWindowInventoryRecertified","fullTopologyEquivalent"})Need(ArtifactProof.Boolean(report[key],false));
        Need(JsonNode.DeepEquals(report["originalVerificationRequestId"],args["verificationRequestId"])&&JsonNode.DeepEquals(report["originalVerificationReceiptSha256"],args["verificationReceiptSha256"])&&JsonNode.DeepEquals(report["expiresUtc"],args["expiresUtc"]));
        var original=saved.VerifyHistorical(Text(args,"verificationRequestId"),Text(args,"verificationReceiptSha256"));
        foreach(string key in new[]{"saveOperationId","targetId","targetSha256","savedResult","copyId","ownershipSha256"})Need(JsonNode.DeepEquals(report[key],original[key]));
        var expected=ArtifactProof.Verified(root,"responses",Text(args,"inspectionRequestId"),Text(args,"inspectionSha256"),16777216);
        var listing=ArtifactProof.Verified(root,"responses",Text(args,"listingRequestId"),Text(args,"listingSha256"),16777216);
        var prior=original["evidence"]!.AsArray()[1]!.AsObject();var old=ArtifactProof.Verified(root,"responses",Text(prior,"requestId"),Text(prior,"sha256"),16777216);
        _=ObservationPolicy.Read(expected);foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId","productVersion","model"})Need(JsonNode.DeepEquals(expected[key],old[key]));
        foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId"})Need(JsonNode.DeepEquals(expected[key],listing[key]));
        Need(JsonNode.DeepEquals(report["model"],expected["model"])&&Text(listing,"state")=="documents_listed_verified"&&JsonNode.DeepEquals(listing["activeWindow"]?["modelId"],args["modelId"])&&JsonNode.DeepEquals(expected["model"]?["id"],args["modelId"])&&JsonNode.DeepEquals(listing["activeWindow"]?["windowNumber"],args["windowNumber"])&&JsonNode.DeepEquals(listing["activeWindow"]?["modelPath"],expected["model"]?["path"])&&JsonNode.DeepEquals(listing["activeWindow"]?["modelName"],expected["model"]?["name"]));
        Need(listing["windows"] is JsonArray windows&&windows.Count(w=>JsonNode.DeepEquals(w?["modelId"],args["modelId"]))==1);
        foreach(string key in new[]{"selectionBefore","selectionAfter"})Need(expected[key] is JsonArray selection&&selection.Count==0);
        foreach(string key in new[]{"contextBefore","contextAfter"})Need(expected[key] is JsonArray context&&context.Count==2&&context.All(v=>ArtifactProof.IntegerIs(v,0)));
        foreach(string key in new[]{"observedPropertiesSha256","currentWindowContextSha256"})Need(FileArtifacts.Token(Text(report,key),64)); // Native digest encoding is intentionally opaque here.
        Need(report["currentSavedFileHashes"] is JsonObject hashes&&hashes.All(p=>p.Value is JsonValue scalar&&scalar.TryGetValue<string>(out var sha)&&FileArtifacts.Token(sha,64)));
        DateTimeOffset Time(JsonObject d,string key)=>LeaseTime.Parse(Text(d,key),"current_saved_result_time_invalid");
        var issued=Time(report,"timestampUtc");var expiry=Time(report,"expiresUtc");var inspected=Time(expected,"timestampUtc");var listed=Time(listing,"timestampUtc");var before=Time(old,"timestampUtc");
        Need(issued<=now.AddSeconds(5)&&issued<expiry&&inspected>=before&&listed>=before&&inspected<=issued&&listed<=issued&&expiry<=inspected.AddSeconds(300)&&expiry<=listed.AddSeconds(300)&&(!fresh||expiry>now));
        return report;
    }
}
