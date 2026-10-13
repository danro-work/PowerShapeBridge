using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// An independent-verification decision record, never an execution permit.
// Called only after the operation-specific SDK proof check; no CAD/IPC here.
public sealed class OutcomeResolutionJournal(string root,Func<DateTimeOffset>? clock=null)
{
    private readonly Func<DateTimeOffset> now=clock??(()=>DateTimeOffset.UtcNow);
    private static string Text(JsonObject value,string key)=>ArtifactProof.String(value,key,"resolution_evidence_invalid");
    private static void Require(bool ok,string code){if(!ok)throw new ReferenceRejected(code);}
    private static readonly Dictionary<string,(string Operation,string Request,string Sha,string ResultId,string State)> Kinds=new()
    {
        ["verify_owned_translation"]=("translate_owned_solid","executionRequestId","executionReceiptSha256","executionOperationId","owned_translation_verified_observed"),
        ["verify_working_copy_open"]=("open_working_copy","openRequestId","openReceiptSha256","openOperationId","owned_open_verified_observed"),
        ["verify_owned_save_as"]=("save_owned_document_as","saveRequestId","saveReceiptSha256","saveOperationId","owned_save_as_verified_observed"),
        ["verify_owned_saved_reopen"]=("open_owned_saved_result","reopenRequestId","reopenReceiptSha256","reopenOperationId","owned_saved_reopen_verified_observed")
    };
    public JsonObject Record(string verifier,JsonObject receipt,JsonObject parameters,CancellationToken cancellationToken=default)
    {
        Require(Kinds.TryGetValue(verifier,out var kind),"resolution_verifier_unsupported");
        cancellationToken.ThrowIfCancellationRequested();var operationId=Text(parameters,kind.Request);var verifyId=Text(receipt,"bridgeRequestId");
        Require(FileArtifacts.Token(operationId)&&FileArtifacts.Token(verifyId)&&operationId!=verifyId,"resolution_identity_invalid");
        var raw=FileArtifacts.ReadBytes(root,"responses/"+verifyId+".json",16777216);var original=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();original["bridgeRequestId"]=verifyId;
        Require(JsonNode.DeepEquals(original,receipt)&&Text(receipt,"state")==kind.State&&Text(receipt,kind.ResultId)==operationId&&Text(receipt,"recordedJournalState")=="succeeded_verified"&&
            JsonNode.DeepEquals(receipt[kind.Sha],parameters[kind.Sha]),"resolution_verifier_receipt_differs");
        Require(new[]{"cadSaveAttempted","geometryMutationAttempted","contextChangeAttempted"}.All(k=>ArtifactProof.Boolean(receipt[k],false)),"resolution_verifier_command_flags_invalid");
        var expiry=LeaseTime.Parse(Text(parameters,"expiresUtc"),"resolution_expired");var observed=LeaseTime.Parse(Text(receipt,"timestampUtc"),"resolution_timestamp_invalid");
        var current=LeaseTime.PythonPrecision(now());Require(current<expiry&&observed<=current&&current-observed<=TimeSpan.FromSeconds(300)&&
            (verifier=="verify_owned_translation"||JsonNode.DeepEquals(receipt["expiresUtc"],parameters["expiresUtc"])),"resolution_observation_expired");
        var recorded=new RecoveryInspection(root).Read(operationId,TimeSpan.FromSeconds(30),cancellationToken);
        Require(Text(recorded,"state")=="recovery_inspected_recorded"&&Text(recorded,"operation")==kind.Operation&&Text(recorded,"assessment")=="historically_verified_requires_fresh_context"&&ArtifactProof.Boolean(recorded["authoritativeSuccessReturnRecorded"],true),"resolution_authoritative_success_missing");
        var journal=recorded["journal"]!.AsObject();var events=journal["events"]!.AsArray();var returned=events.Single(e=>e!["state"]!.GetValue<string>()=="command_returned")!;
        var returnedUtc=LeaseTime.Parse(returned["timestampUtc"]!.GetValue<string>(),"resolution_return_time_invalid");
        var evidence=new JsonArray();Observation? inspection=null;
        foreach(var pair in new[]{(kind.Request,kind.Sha),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})
        {
            var id=Text(parameters,pair.Item1);var sha=Text(parameters,pair.Item2);var item=ArtifactProof.Verified(root,"responses",id,sha,16777216);
            if(pair.Item1!=kind.Request)
            {
                var stamp=LeaseTime.Parse(Text(item,"timestampUtc"),"resolution_read_time_invalid");
                Require(stamp>returnedUtc&&stamp<=observed&&stamp<=current&&current-stamp<=TimeSpan.FromSeconds(300),"resolution_reads_not_fresh_after_return");
                Require(pair.Item1=="inspectionRequestId"?JsonNode.DeepEquals(item["model"]?["id"],parameters["modelId"]):
                    JsonNode.DeepEquals(item["activeWindow"]?["modelId"],parameters["modelId"])&&JsonNode.DeepEquals(item["activeWindow"]?["windowNumber"],parameters["windowNumber"]),"resolution_current_document_differs");
                if(pair.Item1=="inspectionRequestId")inspection=ObservationPolicy.Read(item);
                else Require(Text(item,"state")=="documents_listed_verified"&&inspection is not null&&
                    new[]{"processId","processStartedUtc","windowsSessionId"}.All(k=>JsonNode.DeepEquals(item[k],inspection.Identity[k])),"resolution_current_process_differs");
            }
            evidence.Add(new JsonObject{["requestId"]=id,["sha256"]=sha});
        }
        Require(receipt["evidence"] is JsonArray refs&&refs.Count==3&&evidence.All(e=>refs.Any(r=>JsonNode.DeepEquals(e,r))),"resolution_current_evidence_differs");
        Require(JsonNode.DeepEquals(receipt["planSha256"],recorded["planSha256"])&&JsonNode.DeepEquals(receipt["copyId"],recorded["plan"]?["copyId"])&&
            JsonNode.DeepEquals(receipt["ownershipSha256"],recorded["plan"]?["ownershipSha256"]),"resolution_plan_owner_differs");
        var record=new JsonObject{["schemaVersion"]=1,["operationId"]=operationId,["verificationRequestId"]=verifyId,["verifier"]=verifier,
            ["operation"]=kind.Operation,["copyId"]=recorded["plan"]!["copyId"]!.DeepClone(),["planSha256"]=recorded["planSha256"]!.DeepClone(),
            ["journalSha256"]=recorded["journalSha256"]!.DeepClone(),["verificationReceiptSha256"]=FileArtifacts.Sha256(raw),
            ["observedUtc"]=receipt["timestampUtc"]!.DeepClone(),["validUntilUtc"]=parameters["expiresUtc"]!.DeepClone(),["evidence"]=evidence,
            ["observedDocumentIdentity"]=inspection!.Identity.DeepClone(),["observedPropertiesSha256"]=inspection.Digest,
            ["outcome"]="succeeded_verified_at_observation",["executionAuthorized"]=false,["retryAllowed"]=false,["checkpointRestoreAuthorized"]=false,
            ["qualification"]="Operation-specific verifier accepted fresh observations at the recorded time. This immutable historical decision grants no current CAD references, execution, replay, fence release or checkpoint restore."};
        cancellationToken.ThrowIfCancellationRequested();Require(LeaseTime.PythonPrecision(now())<expiry,"resolution_expired_before_publication");
        Require(JsonNode.DeepEquals(ArtifactProof.Journal(root,operationId),journal),"resolution_journal_changed_before_publication");
        var relative="recoveryresolutions/"+operationId+"/"+verifyId+".json";var path=FileArtifacts.FixedPath(root,relative);
        string recordSha;
        if(File.Exists(path))
        {
            var existing=FileArtifacts.ReadBytes(root,relative,65536);Require(JsonNode.DeepEquals(JsonNode.Parse(FileArtifacts.Parse(existing).GetRawText()),record),"resolution_existing_record_differs");recordSha=FileArtifacts.Sha256(existing);
        }
        else recordSha=ArtifactProof.Freeze(root,relative,record);
        return new(){["operationId"]=operationId,["verificationRequestId"]=verifyId,["resolutionSha256"]=recordSha,["outcome"]="succeeded_verified_at_observation",
            ["executionAuthorized"]=false,["retryAllowed"]=false,["checkpointRestoreAuthorized"]=false};
    }
    public JsonObject Read(string operationId,CancellationToken cancellationToken=default)
    {
        Require(FileArtifacts.Token(operationId),"resolution_identity_invalid");var records=new JsonArray();
        var result=new JsonObject{["state"]="resolution_history_read",["operationId"]=operationId,["records"]=records,
            ["executionAuthorized"]=false,["retryAllowed"]=false,["checkpointRestoreAuthorized"]=false,
            ["qualification"]="Immutable decisions at earlier verification times. They grant no current CAD context, document lease, execution or recovery permission."};
        try
        {
            var folder=FileArtifacts.FixedPath(root,"recoveryresolutions",operationId);if(!Directory.Exists(folder))return result;
            var paths=Directory.GetFiles(folder,"*.json").Order(StringComparer.Ordinal).ToArray();Require(paths.Length<=32,"resolution_history_limit");
            foreach(var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();var id=Path.GetFileNameWithoutExtension(path);Require(FileArtifacts.Token(id),"resolution_record_name_invalid");
                var raw=FileArtifacts.ReadBytes(root,"recoveryresolutions/"+operationId+"/"+id+".json",65536);var record=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
                Require(ArtifactProof.Fields(record,"schemaVersion","operationId","verificationRequestId","verifier","operation","copyId","planSha256","journalSha256","verificationReceiptSha256","observedUtc","validUntilUtc","evidence","observedDocumentIdentity","observedPropertiesSha256","outcome","executionAuthorized","retryAllowed","checkpointRestoreAuthorized","qualification")&&
                    ArtifactProof.IntegerIs(record["schemaVersion"],1)&&Text(record,"operationId")==operationId&&Text(record,"verificationRequestId")==id&&
                    Text(record,"outcome")=="succeeded_verified_at_observation"&&new[]{"executionAuthorized","retryAllowed","checkpointRestoreAuthorized"}.All(k=>ArtifactProof.Boolean(record[k],false)),"resolution_record_invalid");
                Require(Kinds.TryGetValue(Text(record,"verifier"),out var kind)&&Text(record,"operation")==kind.Operation,"resolution_verifier_unsupported");
                var receipt=ArtifactProof.Verified(root,"responses",id,Text(record,"verificationReceiptSha256"),16777216);
                Require(Text(receipt,"state")==kind.State&&Text(receipt,kind.ResultId)==operationId&&JsonNode.DeepEquals(receipt["timestampUtc"],record["observedUtc"])&&
                    JsonNode.DeepEquals(receipt["copyId"],record["copyId"])&&JsonNode.DeepEquals(receipt["planSha256"],record["planSha256"]),"resolution_record_receipt_differs");
                Require(record["evidence"] is JsonArray evidence&&evidence.Count==3&&receipt["evidence"] is JsonArray refs&&refs.Count==3&&
                    evidence.All(e=>refs.Any(r=>JsonNode.DeepEquals(e,r))),"resolution_record_evidence_differs");
                var evidenceItems=record["evidence"]!.AsArray();
                foreach(var item in evidenceItems)
                {
                    Require(item is JsonObject&&ArtifactProof.Fields(item.AsObject(),"requestId","sha256"),"resolution_record_evidence_invalid");
                    ArtifactProof.Verified(root,"responses",Text(item!.AsObject(),"requestId"),Text(item.AsObject(),"sha256"),16777216);
                }
                Require(Text(evidenceItems[0]!.AsObject(),"requestId")==operationId&&Text(evidenceItems[0]!.AsObject(),"sha256")==Text(receipt,kind.Sha),"resolution_original_evidence_differs");
                var inspectionRef=evidenceItems[1]!.AsObject();var snapshot=ArtifactProof.Verified(root,"responses",Text(inspectionRef,"requestId"),Text(inspectionRef,"sha256"),16777216);
                var inspection=ObservationPolicy.Read(snapshot);Require(JsonNode.DeepEquals(inspection.Identity,record["observedDocumentIdentity"])&&inspection.Digest==Text(record,"observedPropertiesSha256"),"resolution_record_observation_differs");
                var journal=ArtifactProof.Journal(root,operationId);Require(Text(journal,"journalSha256")==Text(record,"journalSha256")&&Text(journal,"planSha256")==Text(record,"planSha256")&&Text(journal,"recordedState")=="succeeded_verified","resolution_record_journal_differs");
                var view=record.DeepClone().AsObject();view["resolutionSha256"]=FileArtifacts.Sha256(raw);view["currentContextChecked"]=false;records.Add(view);
            }
            return result;
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception error)when(error is ReferenceRejected or IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {result["state"]="resolution_history_unavailable";result["errorCode"]=error is ReferenceRejected rejected?rejected.Code:"resolution_record_unreadable";return result;}
    }
}
