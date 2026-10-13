using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Checks original/terminal raw artifacts before issuing new current-document leases.
public static class OwnedReopenProof
{
    private static string Text(JsonObject value,string key)=>ArtifactProof.String(value,key,"saved_reopen_execution_proof_invalid");
    private static void Require(bool condition,string code){if(!condition)throw new ReferenceRejected(code);}
    public static JsonObject Execution(BridgeSession session,JsonObject receipt,JsonObject? parameters,bool verified=false)
    {
        var root=session.Options.BridgeRoot;var id=Text(receipt,"operationId");
        Require(FileArtifacts.Token(id)&&Text(receipt,"bridgeRequestId")==id,"saved_reopen_execution_identity_invalid");
        var raw=FileArtifacts.ReadBytes(root,"responses/"+id+".json",16777216);var original=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();original["bridgeRequestId"]=id;
        Require(JsonNode.DeepEquals(original,receipt),"saved_reopen_execution_raw_receipt_differs");
        Require(receipt["state"]?.GetValue<string>()=="owned_saved_reopen_executed_unverified"||verified&&receipt["state"]?.GetValue<string>()=="owned_saved_reopen_outcome_unknown","saved_reopen_execution_state_invalid");
        Require(ArtifactProof.Boolean(receipt["contextChangeAttempted"],true)&&ArtifactProof.Boolean(receipt["cadSaveAttempted"],false)&&ArtifactProof.Boolean(receipt["geometryMutationAttempted"],false),"saved_reopen_execution_flags_invalid");
        if(!verified)Require(ArtifactProof.Boolean(receipt["observableContentChecked"],true)&&ArtifactProof.Boolean(receipt["checkpointVerified"],true)&&ArtifactProof.Boolean(receipt["savedFilesUnchanged"],true)&&receipt["recordedJournalState"]?.GetValue<string>()=="executed_unverified","saved_reopen_execution_postconditions_missing");
        var plan=ArtifactProof.Verified(root,"copyopenplans",id,Text(receipt,"planSha256"),65536);
        Require(ArtifactProof.Fields(plan,"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256","modelId","windowNumber","expiresUtc","schemaVersion","operationId","operation","bridgeVersion","createdUtc","geometryExecutionEnabled","workingPath","reopenId","reopenSha256")&&
            ArtifactProof.IntegerIs(plan["schemaVersion"],1)&&ArtifactProof.Boolean(plan["geometryExecutionEnabled"],false)&&Text(plan,"operationId")==id&&Text(plan,"operation")=="open_owned_saved_result"&&JsonNode.DeepEquals(plan,receipt["plan"]),"saved_reopen_execution_plan_differs");
        if(parameters is not null)Require(parameters.All(p=>JsonNode.DeepEquals(p.Value,plan[p.Key])),"saved_reopen_execution_intent_differs");
        var reopenId=Text(plan,"reopenId");var preparedRaw=FileArtifacts.ReadBytes(root,"savedreopens/"+reopenId+"/reopen.json",65536);
        var prepared=session.Reopens.VerifyReceipt(new(){["reopenId"]=reopenId,["reopenSha256"]=plan["reopenSha256"]!.DeepClone(),["reopen"]=JsonNode.Parse(FileArtifacts.Parse(preparedRaw).GetRawText())});
        foreach(var field in new[]{"copyId","ownershipSha256","workingPath","reopenId"})Require(JsonNode.DeepEquals(plan[field],prepared[field])&&JsonNode.DeepEquals(receipt[field],plan[field]),"saved_reopen_execution_dependency_differs");
        Require(JsonNode.DeepEquals(receipt["reopenSha256"],plan["reopenSha256"])&&JsonNode.DeepEquals(receipt["sourceSavedResultSha256"],prepared["savedResult"]?["sha256"]),"saved_reopen_execution_saved_hash_differs");
        var claim=JsonNode.Parse(FileArtifacts.ReadBytes(root,"workingcopies/"+Text(plan,"copyId")+"/open-once.json",65536))!.AsObject();
        Require(ArtifactProof.Fields(claim,"operationId","planSha256","copyId","ownershipSha256","automaticReplayForbidden")&&Text(claim,"operationId")==id&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true)&&new[]{"planSha256","copyId","ownershipSha256"}.All(k=>JsonNode.DeepEquals(claim[k],receipt[k])),"saved_reopen_open_claim_differs");
        var journal=ArtifactProof.Journal(root,id);Require(ArtifactProof.Boolean(journal["writerLockPresent"],false)&&JsonNode.DeepEquals(journal["planSha256"],receipt["planSha256"])&&Text(journal,"recordedState")== (verified?"succeeded_verified":"executed_unverified"),"saved_reopen_execution_journal_invalid");
        var fence=FileArtifacts.FixedPath(root,"workingcopies",Text(plan,"copyId"),"mutation-fence.json");Require(!File.Exists(fence)&&!Directory.Exists(fence),"saved_reopen_copy_fenced");
        var events=journal["events"]!.AsArray();Require(events.Count==(verified?6:5),"saved_reopen_journal_sequence_invalid");
        var returned=events.Single(e=>e!["state"]!.GetValue<string>()=="command_returned")!["details"]!.AsObject();
        Require(ArtifactProof.Fields(returned,"nativeRequestId")&&Text(returned,"nativeRequestId")==id,"saved_reopen_authoritative_return_missing");
        return plan;
    }
    public static JsonObject Verification(BridgeSession session,JsonObject result,JsonObject parameters)
    {
        var root=session.Options.BridgeRoot;var id=Text(result,"bridgeRequestId");var raw=FileArtifacts.ReadBytes(root,"responses/"+id+".json",16777216);
        var received=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();received["bridgeRequestId"]=id;
        Require(JsonNode.DeepEquals(result,received)&&result["state"]?.GetValue<string>()=="owned_saved_reopen_verified_observed"&&result["recordedJournalState"]?.GetValue<string>()=="succeeded_verified"&&
            new[]{"cadSaveAttempted","contextChangeAttempted","geometryMutationAttempted","executionEnabled"}.All(k=>ArtifactProof.Boolean(result[k],false))&&
            new[]{"noCommandReplay","savedFilesUnchanged","observableContentChecked"}.All(k=>ArtifactProof.Boolean(result[k],true)),"saved_reopen_independent_receipt_invalid");
        Require(JsonNode.DeepEquals(result["reopenOperationId"],parameters["reopenRequestId"])&&JsonNode.DeepEquals(result["reopenReceiptSha256"],parameters["reopenReceiptSha256"])&&JsonNode.DeepEquals(result["expiresUtc"],parameters["expiresUtc"]),"saved_reopen_independent_identity_differs");
        var original=ArtifactProof.Verified(root,"responses",Text(parameters,"reopenRequestId"),Text(parameters,"reopenReceiptSha256"),16777216);original["bridgeRequestId"]=parameters["reopenRequestId"]!.DeepClone();
        var plan=Execution(session,original,null,true);
        foreach(var field in new[]{"reopenId","reopenSha256","copyId","ownershipSha256","planSha256","sourceSavedResultSha256"})Require(JsonNode.DeepEquals(result[field],original[field]),"saved_reopen_independent_dependency_differs");
        var expected=ArtifactProof.Verified(root,"responses",Text(parameters,"inspectionRequestId"),Text(parameters,"inspectionSha256"),16777216);
        var listing=ArtifactProof.Verified(root,"responses",Text(parameters,"listingRequestId"),Text(parameters,"listingSha256"),16777216);
        Require(JsonNode.DeepEquals(result["model"],expected["model"])&&JsonNode.DeepEquals(result["model"]?["id"],parameters["modelId"])&&JsonNode.DeepEquals(listing["activeWindow"]?["modelId"],parameters["modelId"])&&
            JsonNode.DeepEquals(listing["activeWindow"]?["windowNumber"],parameters["windowNumber"])&&
            ObservationPolicy.NormalizePath(Text(result["model"]!.AsObject(),"path"))==ObservationPolicy.NormalizePath(Text(plan,"workingPath")),"saved_reopen_verified_current_model_differs");
        var evidence=new JsonArray();foreach(var pair in new[]{("reopenRequestId","reopenReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})evidence.Add(new JsonObject{["requestId"]=parameters[pair.Item1]!.DeepClone(),["sha256"]=parameters[pair.Item2]!.DeepClone()});
        var terminal=ArtifactProof.Journal(root,Text(parameters,"reopenRequestId"))["events"]!.AsArray().Last()!["details"]!.AsObject();
        Require(JsonNode.DeepEquals(result["evidence"],evidence)&&ArtifactProof.Boolean(terminal["noCommandReplay"],true)&&JsonNode.DeepEquals(terminal["sourceSavedResultSha256"],result["sourceSavedResultSha256"])&&JsonNode.DeepEquals(terminal["ownedModelId"],parameters["modelId"])&&
            terminal["evidence"] is JsonArray old&&old.Count==3&&JsonNode.DeepEquals(old[0],evidence[0]),"saved_reopen_independent_evidence_differs");
        foreach(var entry in terminal["evidence"]!.AsArray())ArtifactProof.Verified(root,"responses",Text(entry!.AsObject(),"requestId"),Text(entry.AsObject(),"sha256"),16777216);
        Require(Text(result,"bindingId")==id,"saved_reopen_new_binding_identity_invalid");
        var bindingArgs=new JsonObject{["copyId"]=plan["copyId"]!.DeepClone(),["ownershipSha256"]=plan["ownershipSha256"]!.DeepClone(),["inspectionRequestId"]=parameters["inspectionRequestId"]!.DeepClone(),["inspectionSha256"]=parameters["inspectionSha256"]!.DeepClone(),["expiresUtc"]=parameters["expiresUtc"]!.DeepClone()};
        session.Bindings.VerifyReceipt(result,bindingArgs);return plan;
    }
    public static JsonObject IssueBinding(BridgeSession session,JsonObject result,JsonObject parameters,string candidate)
    {
        Verification(session,result,parameters);
        var current=session.Candidates.DocumentParameters(candidate);Require(new[]{"inspectionRequestId","inspectionSha256"}.All(k=>JsonNode.DeepEquals(current[k],parameters[k])),"saved_reopen_candidate_lost");
        var copyReceipt=result.DeepClone().AsObject();copyReceipt["state"]="owned_open_verified_observed";copyReceipt["openOperationId"]=result["reopenOperationId"]!.DeepClone();
        var issued=session.Copies.IssueVerifiedOpen(copyReceipt);var copy=Text(issued,"workingCopyHandle");
        var bindingReceipt=result.DeepClone().AsObject();bindingReceipt["state"]="working_document_bound_observed";
        var args=session.Bindings.PrepareParameters(candidate,copy);args["expiresUtc"]=parameters["expiresUtc"]!.DeepClone();
        var bound=session.Bindings.Issue(bindingReceipt,args,candidate,copy,LeaseTime.Parse(Text(parameters,"expiresUtc"),"saved_reopen_expired"));
        var output=result.DeepClone().AsObject();foreach(var key in new[]{"workingDocumentBindingHandle","bindingLifetimeSeconds"})output[key]=bound[key]!.DeepClone();
        output["workingCopyHandle"]=copy;output["workingCopyLifetimeSeconds"]=issued["workingCopyLifetimeSeconds"]!.DeepClone();return output;
    }
}
