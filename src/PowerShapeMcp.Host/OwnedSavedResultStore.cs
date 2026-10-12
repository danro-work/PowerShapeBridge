using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Connection-local saved-byte proofs, not edit authorization or topology proof.
// Native rechecks physical file/directory identity before any later clone/open.
public sealed class OwnedSavedResultStore(string root,OwnedSaveTargetStore targets,CandidateStore candidates,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(string Request,string Sha,JsonObject Parameters,double Expires);
    private readonly Dictionary<string,Lease> saved=[];
    public void Clear()=>saved.Clear();
    public void Revoke(string handle)=>saved.Remove(handle);
    private static string Text(JsonObject value,string key)=>ArtifactProof.String(value,key,"saved_result_proof_invalid");
    private static void Require(bool condition,string code){if(!condition)throw new ReferenceRejected(code);}
    private JsonObject RawResponse(string id,string? sha=null,JsonObject? received=null)
    {
        Require(FileArtifacts.Token(id),"saved_result_response_identity_invalid");
        var bytes=FileArtifacts.ReadBytes(root,Path.Combine("responses",id+".json"),16777216);
        if(sha is not null)Require(FileArtifacts.Token(sha,64)&&FileArtifacts.Sha256(bytes)==sha,"saved_result_response_sha_differs");
        var response=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();
        if(response["bridgeRequestId"] is not null)Require(Text(response,"bridgeRequestId")==id,"saved_result_response_identity_invalid");
        response["bridgeRequestId"]=id;
        if(received is not null)Require(JsonNode.DeepEquals(response,received),"saved_result_response_body_differs");
        return response;
    }
    private JsonObject Target(JsonObject plan)
    {
        var id=Text(plan,"targetId");Require(FileArtifacts.Token(id),"saved_result_target_identity_invalid");
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("savedresults",id,"target.json"),65536);
        return targets.VerifyReceipt(new(){["targetId"]=id,["targetSha256"]=plan["targetSha256"]!.DeepClone(),["target"]=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())});
    }
    private void FileProof(JsonObject proof,string targetId)
    {
        Require(ArtifactProof.Fields(proof,"path","sha256","length","fileIdentity"),"saved_result_file_proof_invalid");
        var path=FileArtifacts.FixedPath(root,"savedresults",targetId,"Result_"+targetId+".psmodel");
        Require(ObservationPolicy.NormalizePath(Text(proof,"path"))==ObservationPolicy.NormalizePath(path)&&FileArtifacts.Token(Text(proof,"sha256"),64),"saved_result_fixed_file_differs");
        Require(proof["fileIdentity"] is JsonObject identity&&ArtifactProof.Fields(identity,"volumeSerial","fileIndex","creationTime")&&
            FileArtifacts.Token(Text(identity,"volumeSerial"),8)&&FileArtifacts.Token(Text(identity,"fileIndex"),16)&&FileArtifacts.Token(Text(identity,"creationTime"),16),"saved_result_file_identity_invalid");
        var length=JsonSerializer.SerializeToElement(proof["length"]);
        Require(length.ValueKind==JsonValueKind.Number&&length.TryGetInt64(out var count)&&count is >0 and <=1073741824,"saved_result_size_invalid");
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        Require(file.Length==length.GetInt64(),"saved_result_bytes_changed");
        var first=Convert.ToHexStringLower(SHA256.HashData(file));file.Position=0;
        Require(first==Text(proof,"sha256")&&first==Convert.ToHexStringLower(SHA256.HashData(file))&&file.Length==length.GetInt64(),"saved_result_bytes_changed");
    }
    private JsonObject Execution(JsonObject receipt,JsonObject? parameters,bool verified)
    {
        var id=Text(receipt,"operationId");RawResponse(id,received:receipt);
        var state=Text(receipt,"state");
        Require(state=="owned_save_as_executed_unverified"||verified&&state=="owned_save_as_outcome_unknown","saved_result_execution_state_invalid");
        Require(ArtifactProof.Boolean(receipt["cadSaveAttempted"],true)&&ArtifactProof.Boolean(receipt["geometryMutationAttempted"],false)&&
            ArtifactProof.Boolean(receipt["contextChangeAttempted"],true)&&ArtifactProof.Boolean(receipt["saveCommandReturned"],true)&&ArtifactProof.Boolean(receipt["copyFenced"],true),"saved_result_execution_receipt_invalid");
        if(!verified)Require(ArtifactProof.IntegerIs(receipt["commandCalls"],1)&&receipt["recordedJournalState"]?.GetValue<string>()=="executed_unverified","saved_result_execution_receipt_invalid");
        var plan=ArtifactProof.Verified(root,"saveexecutions",id,Text(receipt,"planSha256"),65536);
        Require(ArtifactProof.Fields(plan,"targetId","targetSha256","schemaVersion","operationId","operation","executionEnabled","copyId","ownershipSha256","resultPath","createdUtc")&&
            ArtifactProof.IntegerIs(plan["schemaVersion"],1)&&ArtifactProof.Boolean(plan["executionEnabled"],true)&&Text(plan,"operationId")==id&&Text(plan,"operation")=="save_owned_document_as"&&JsonNode.DeepEquals(plan,receipt["plan"]),"saved_result_execution_plan_differs");
        if(parameters is not null)Require(ArtifactProof.Fields(parameters,"targetId","targetSha256")&&parameters.All(p=>JsonNode.DeepEquals(p.Value,plan[p.Key])),"saved_result_execution_target_differs");
        var target=Target(plan);
        foreach(var field in new[]{"copyId","ownershipSha256","resultPath"})Require(JsonNode.DeepEquals(plan[field],target[field])&&JsonNode.DeepEquals(plan[field],receipt[field]),"saved_result_execution_dependency_differs");
        var claim=JsonNode.Parse(FileArtifacts.ReadBytes(root,Path.Combine("savedresults",Text(plan,"targetId"),"save-once.json"),65536))!.AsObject();
        Require(ArtifactProof.Fields(claim,"operationId","planSha256","targetId","automaticReplayForbidden")&&Text(claim,"operationId")==id&&JsonNode.DeepEquals(claim["planSha256"],receipt["planSha256"])&&JsonNode.DeepEquals(claim["targetId"],plan["targetId"])&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true),"saved_result_claim_differs");
        var journal=ArtifactProof.Journal(root,id);var journalState=Text(journal,"recordedState");
        Require(ArtifactProof.Boolean(journal["writerLockPresent"],false)&&JsonNode.DeepEquals(journal["planSha256"],receipt["planSha256"])&&
            (verified?journalState=="succeeded_verified":journalState=="executed_unverified"),"saved_result_journal_not_verified");
        var events=journal["events"]!.AsArray();Require(events.Count==(verified?6:5),"saved_result_journal_sequence_invalid");
        var returned=events.Single(e=>e!["state"]!.GetValue<string>()=="command_returned")!["details"]!.AsObject();
        Require(ArtifactProof.Fields(returned,"nativeRequestId","saveCommandReturned")&&Text(returned,"nativeRequestId")==id&&ArtifactProof.Boolean(returned["saveCommandReturned"],true),"saved_result_authoritative_return_missing");
        var fencePath=FileArtifacts.FixedPath(root,"workingcopies",Text(plan,"copyId"),"mutation-fence.json");
        if(verified)Require(!File.Exists(fencePath)&&!Directory.Exists(fencePath),"saved_result_fence_not_released");
        else
        {
            var bytes=FileArtifacts.ReadBytes(root,Path.GetRelativePath(root,fencePath),65536);Require(FileArtifacts.Sha256(bytes)==Text(receipt,"executionFenceSha256"),"saved_result_fence_sha_differs");
            var fence=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();
            Require(ArtifactProof.Fields(fence,"schemaVersion","operationId","planSha256","copyId","ownershipSha256","automaticReplayForbidden")&&ArtifactProof.IntegerIs(fence["schemaVersion"],1)&&Text(fence,"operationId")==id&&
                new[]{"planSha256","copyId","ownershipSha256"}.All(k=>JsonNode.DeepEquals(fence[k],receipt[k]))&&ArtifactProof.Boolean(fence["automaticReplayForbidden"],true),"saved_result_fence_differs");
        }
        Require(receipt["savedResult"] is JsonObject,"saved_result_file_proof_invalid");FileProof(receipt["savedResult"]!.AsObject(),Text(plan,"targetId"));return plan;
    }
    public void CheckedExecution(JsonObject result,JsonObject parameters)=>Execution(result,parameters,false);
    public JsonObject VerifyReceipt(JsonObject result,JsonObject parameters)
    {
        var verifyId=Text(result,"bridgeRequestId");RawResponse(verifyId,received:result);
        Require(result["state"]?.GetValue<string>()=="owned_save_as_verified_observed"&&ArtifactProof.Boolean(result["copyFenceReleased"],true)&&ArtifactProof.Boolean(result["noCommandReplay"],true)&&ArtifactProof.Boolean(result["savedFilesUnchanged"],true)&&
            new[]{"cadSaveAttempted","geometryMutationAttempted","contextChangeAttempted"}.All(k=>ArtifactProof.Boolean(result[k],false)),"saved_result_independent_receipt_invalid");
        Require(JsonNode.DeepEquals(result["saveOperationId"],parameters["saveRequestId"])&&JsonNode.DeepEquals(result["saveReceiptSha256"],parameters["saveReceiptSha256"])&&JsonNode.DeepEquals(result["expiresUtc"],parameters["expiresUtc"]),"saved_result_verification_identity_differs");
        var original=RawResponse(Text(parameters,"saveRequestId"),Text(parameters,"saveReceiptSha256"));var plan=Execution(original,null,true);
        foreach(var field in new[]{"targetId","targetSha256","copyId","ownershipSha256"})Require(JsonNode.DeepEquals(result[field],plan[field]),"saved_result_verified_dependency_differs");
        Require(JsonNode.DeepEquals(result["planSha256"],original["planSha256"])&&JsonNode.DeepEquals(result["savedResult"],original["savedResult"])&&result["recordedJournalState"]?.GetValue<string>()=="succeeded_verified","saved_result_verified_file_differs");
        var expected=ArtifactProof.Verified(root,"responses",Text(parameters,"inspectionRequestId"),Text(parameters,"inspectionSha256"),16777216);
        var listing=ArtifactProof.Verified(root,"responses",Text(parameters,"listingRequestId"),Text(parameters,"listingSha256"),16777216);
        Require(JsonNode.DeepEquals(result["model"],expected["model"])&&JsonNode.DeepEquals(listing["activeWindow"]?["modelId"],parameters["modelId"])&&JsonNode.DeepEquals(listing["activeWindow"]?["windowNumber"],parameters["windowNumber"]),"saved_result_verified_model_differs");
        Require(result["model"] is JsonObject,"saved_result_verified_model_differs");var model=result["model"]!.AsObject();
        Require(JsonNode.DeepEquals(model["id"],parameters["modelId"])&&ObservationPolicy.NormalizePath(Text(model,"path"))==ObservationPolicy.NormalizePath(Text(plan,"resultPath"))&&
            JsonNode.DeepEquals(listing["activeWindow"]?["modelPath"],model["path"])&&JsonNode.DeepEquals(listing["activeWindow"]?["modelName"],model["name"]),"saved_result_verified_model_differs");
        var evidence=new JsonArray(new JsonObject{["requestId"]=parameters["saveRequestId"]!.DeepClone(),["sha256"]=parameters["saveReceiptSha256"]!.DeepClone()},
            new JsonObject{["requestId"]=parameters["inspectionRequestId"]!.DeepClone(),["sha256"]=parameters["inspectionSha256"]!.DeepClone()},
            new JsonObject{["requestId"]=parameters["listingRequestId"]!.DeepClone(),["sha256"]=parameters["listingSha256"]!.DeepClone()});
        var terminal=ArtifactProof.Journal(root,Text(parameters,"saveRequestId"))["events"]!.AsArray().Last()!["details"]!.AsObject();
        Require(JsonNode.DeepEquals(evidence,result["evidence"])&&ArtifactProof.Boolean(terminal["noCommandReplay"],true)&&JsonNode.DeepEquals(terminal["savedResultSha256"],result["savedResult"]?["sha256"]),"saved_result_independent_evidence_differs");
        // Idempotent native re-verification may use new read receipts while the
        // already terminal journal retains its first independent evidence.
        Require(terminal["evidence"] is JsonArray prior&&prior.Count==3&&JsonNode.DeepEquals(prior[0],evidence[0]),"saved_result_terminal_evidence_differs");
        foreach(var entry in terminal["evidence"]!.AsArray())
        {
            Require(entry is JsonObject reference&&ArtifactProof.Fields(reference,"requestId","sha256"),"saved_result_terminal_evidence_differs");
            ArtifactProof.Verified(root,"responses",Text(entry!.AsObject(),"requestId"),Text(entry.AsObject(),"sha256"),16777216);
        }
        return plan;
    }
    public JsonObject Issue(JsonObject result,JsonObject parameters,string candidate)
    {
        var current=candidates.DocumentParameters(candidate);
        Require(new[]{"inspectionRequestId","inspectionSha256"}.All(k=>JsonNode.DeepEquals(current[k],parameters[k])),"saved_result_candidate_lost");
        VerifyReceipt(result,parameters);
        var expiry=LeaseTime.Parse(Text(parameters,"expiresUtc"),"saved_result_expired");
        var remaining=Math.Min((expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds,candidates.DocumentRemaining(candidate));
        remaining=Math.Min(remaining,LeaseTime.Remaining(Text(result,"timestampUtc"),utcNow(),"saved_result_expired").Remaining);
        Require(remaining>0&&remaining<=300,"saved_result_expired");
        foreach(var token in saved.Where(p=>p.Value.Expires<=now()).Select(p=>p.Key).ToArray())saved.Remove(token);
        if(saved.Count>=128)Clear();var handle=Guid.NewGuid().ToString("N");var id=Text(result,"bridgeRequestId");
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("responses",id+".json"),16777216);
        saved[handle]=new(id,FileArtifacts.Sha256(raw),parameters.DeepClone().AsObject(),now()+remaining);
        var output=result.DeepClone().AsObject();output["savedResultHandle"]=handle;output["savedResultLifetimeSeconds"]=(int)remaining;return output;
    }
    public JsonObject Parameters(string handle)
    {
        if(!saved.TryGetValue(handle,out var lease)||lease.Expires<=now()){saved.Remove(handle);throw new ReferenceRejected("saved_result_handle_unknown_or_expired");}
        var receipt=RawResponse(lease.Request,lease.Sha);VerifyReceipt(receipt,lease.Parameters);
        return new(){["verificationRequestId"]=lease.Request,["verificationReceiptSha256"]=lease.Sha};
    }
    public double Remaining(string handle)
    {
        Parameters(handle);
        return saved[handle].Expires-now();
    }
}
