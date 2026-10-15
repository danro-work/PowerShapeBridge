using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Recorded artifact inspection only. Never grants execution or changes a journal.
public sealed class RecoveryInspection(string root)
{
    private static readonly string[] PlanFolders=["ownedexecutions","saveexecutions","copyopenplans","levelmoveexecutions"];
    private static string Text(JsonObject data,string key)=>ArtifactProof.String(data,key,"recovery_artifact_invalid");
    private static void Require(bool ok,string code){if(!ok)throw new ReferenceRejected(code);}
    public JsonObject Read(string operationId,TimeSpan budget,CancellationToken cancellationToken=default)
    {
        Require(FileArtifacts.Token(operationId),"recovery_operation_identity_invalid");var timer=Stopwatch.StartNew();
        var output=new JsonObject{["state"]="recovery_inspected_recorded",["operationId"]=operationId,["outcome"]="unknown",["retryAllowed"]=false,["executionAuthorized"]=false,
            ["cadConnectionAttempted"]=false,["contextChangeAttempted"]=false,["geometryMutationAttempted"]=false,["cadSaveAttempted"]=false,["artifactWritesAttempted"]=false,
            ["qualification"]="Point-in-time recorded artifacts and saved bytes only; no current CAD context, command replay, fence/claim release, journal repair, inverse, Undo or checkpoint restore.",
            ["requiredNextStep"]="Read fresh CAD context and use the operation-specific independent verifier. Missing authoritative return remains unknown; checkpoint recovery requires a separate accepted scenario."};
        try
        {
            void Deadline(){cancellationToken.ThrowIfCancellationRequested();if(timer.Elapsed>=budget)throw new ReferenceRejected("recovery_inspection_deadline");}
            Deadline();var read=new OperationJournal(root).Read(operationId);Require(read["state"]?.GetValue<string>()=="operation_receipt_read"&&read["receipt"] is JsonObject,"recovery_journal_unavailable");
            var journal=read["receipt"]!.AsObject();
            if(journal["events"] is not JsonArray)return ReadLegacyActivation(operationId,read,journal,output,Deadline);
            output["journal"]=journal.DeepClone();output["journalSha256"]=journal["journalSha256"]?.DeepClone();
            var plans=PlanFolders.Select(folder=>(Folder:folder,Path:FileArtifacts.FixedPath(root,folder,operationId+".json"))).Where(p=>File.Exists(p.Path)||Directory.Exists(p.Path)).ToArray();
            Require(plans.Length==1,"recovery_plan_missing_or_ambiguous");Deadline();
            // Level execution plans retain the complete 1000-slot observation.
            // Keep the original small limit for every other supported format.
            int planLimit=plans[0].Folder=="levelmoveexecutions"?16000000:65536;
            var planRaw=FileArtifacts.ReadBytes(root,Path.GetRelativePath(root,plans[0].Path),planLimit);var planSha=FileArtifacts.Sha256(planRaw);
            Require(Text(journal,"planSha256")==planSha,"recovery_plan_sha_differs");var plan=JsonNode.Parse(FileArtifacts.Parse(planRaw).GetRawText())!.AsObject();
            Require(Text(plan,"operationId")==operationId&&ArtifactProof.IntegerIs(plan["schemaVersion"],1)&&FileArtifacts.Token(Text(plan,"copyId"))&&FileArtifacts.Token(Text(plan,"ownershipSha256"),64),"recovery_plan_identity_invalid");
            var operation=Text(plan,"operation");Require(operation is "translate_owned_solid" or "save_owned_document_as" or "open_working_copy" or "open_owned_saved_result" or "move_owned_solid_to_level","recovery_operation_unsupported");
            Require((operation=="translate_owned_solid"&&plans[0].Folder=="ownedexecutions")||(operation=="save_owned_document_as"&&plans[0].Folder=="saveexecutions")||(operation is "open_working_copy" or "open_owned_saved_result"&&plans[0].Folder=="copyopenplans")||(operation=="move_owned_solid_to_level"&&plans[0].Folder=="levelmoveexecutions"),"recovery_plan_location_invalid");
            if(operation=="move_owned_solid_to_level")output["requiredNextStep"]="Use adopt_owned_level_move_execution in a full-server connection, then fresh CAD inspection/listing and verify_owned_level_move. This recorded inspection does not resolve today's CAD state or authorize a retry/checkpoint restore.";
            output["operation"]=operation;output["planSha256"]=planSha;output["plan"]=plan.DeepClone();
            JsonObject Json(string relative,string? expectedSha=null)
            {
                Deadline();var bytes=FileArtifacts.ReadBytes(root,relative,65536);if(expectedSha is not null)Require(FileArtifacts.Token(expectedSha,64)&&FileArtifacts.Sha256(bytes)==expectedSha,"recovery_dependency_sha_differs");
                return JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();
            }
            var copy=Text(plan,"copyId");var ownership=Json("workingcopies/"+copy+"/ownership.json",Text(plan,"ownershipSha256"));
            Require(ArtifactProof.Fields(ownership,"schemaVersion","copyId","bridgeVersion","createdUtc","scope","executionEnabled","source","working","checkpoint")&&
                (ArtifactProof.IntegerIs(ownership["schemaVersion"],1)||ArtifactProof.IntegerIs(ownership["schemaVersion"],2))&&Text(ownership,"copyId")==copy&&Text(ownership,"scope")=="saved_disk_bytes_only"&&ArtifactProof.Boolean(ownership["executionEnabled"],false),"recovery_ownership_invalid");
            var workingName=ArtifactProof.IntegerIs(ownership["schemaVersion"],1)?"Working.psmodel":"Working_"+copy+".psmodel";
            var working=FileArtifacts.FixedPath(root,"workingcopies",copy,workingName);var checkpoint=FileArtifacts.FixedPath(root,"workingcopies",copy,"checkpoint","Before.psmodel");
            Require(ownership["working"] is JsonObject&&ownership["checkpoint"] is JsonObject,"recovery_ownership_roles_invalid");
            var workRecord=ownership["working"]!.AsObject();var beforeRecord=ownership["checkpoint"]!.AsObject();
            Require(ObservationPolicy.NormalizePath(Text(workRecord,"path"))==ObservationPolicy.NormalizePath(working)&&ObservationPolicy.NormalizePath(Text(beforeRecord,"path"))==ObservationPolicy.NormalizePath(checkpoint),"recovery_owned_paths_invalid");
            var files=new JsonArray();bool Bytes(string path,string expected,bool readonlyRequired)
            {
                Deadline();Require(FileArtifacts.Token(expected,64),"recovery_saved_hash_invalid");using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                Require(stream.Length is >0 and <=1073741824,"recovery_file_size_invalid");var length=stream.Length;
                string Hash(){using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];int readCount;while((readCount=stream.Read(buffer))>0){Deadline();sha.AppendData(buffer,0,readCount);}return Convert.ToHexStringLower(sha.GetHashAndReset());}
                var first=Hash();stream.Position=0;Require(Hash()==first&&stream.Length==length,"recovery_file_changed_during_read");
                bool readonlyFlag=(File.GetAttributes(path)&FileAttributes.ReadOnly)!=0;bool matches=first==expected&&(!readonlyRequired||readonlyFlag);
                files.Add(new JsonObject{["path"]=path,["sha256"]=first,["expectedSha256"]=expected,["length"]=length,["matchesRecordedBytes"]=first==expected,["readOnly"]=readonlyFlag,["roleGuardSatisfied"]=matches});return matches;
            }
            bool workMatches=Bytes(working,Text(workRecord,"initialSha256"),false),checkpointMatches=Bytes(checkpoint,Text(beforeRecord,"sha256"),true);output["files"]=files;
            output["workingSavedBytesMatch"]=workMatches;output["checkpointSavedBytesMatch"]=checkpointMatches;output["fileIdentityChecked"]=false;
            output["fileQualification"]="Double SHA/size with ReadWrite sharing for inspection only. This does not exclude writers or establish physical file identity, current CAD content, units or topology. Native verifier must recheck identities/context.";
            var recorded=Text(journal,"recordedState");var events=journal["events"]!.AsArray();var started=events.Where(e=>e!["state"]?.GetValue<string>()=="command_started").ToArray();var returned=events.Where(e=>e!["state"]?.GetValue<string>()=="command_returned").ToArray();
            Require(started.Length<=1&&returned.Length<=1,"recovery_command_sequence_invalid");output["commandStartedRecorded"]=started.Length==1;output["commandReturnedRecorded"]=returned.Length==1;
            bool authoritative=false;int? returnCode=null;
            if(returned.Length==1)
            {
                var details=returned[0]!["details"]!.AsObject();Require(Text(details,"nativeRequestId")==operationId,"recovery_return_identity_differs");
                if(operation=="translate_owned_solid")
                {Require(ArtifactProof.Fields(details,"nativeRequestId","returnCode")&&details["returnCode"] is JsonValue value&&value.TryGetValue<int>(out _),"recovery_return_code_invalid");returnCode=details["returnCode"]!.GetValue<int>();authoritative=returnCode==1;}
                else if(operation=="move_owned_solid_to_level")
                {
                    Require(ArtifactProof.Fields(details,"nativeRequestId","returnCode","nativeReturnType")&&details["returnCode"] is JsonValue code&&code.TryGetValue<int>(out _),"recovery_return_code_invalid");
                    returnCode=details["returnCode"]!.GetValue<int>();var nativeType=Text(details,"nativeReturnType");
                    output["recordedNativeReturnType"]=nativeType;authoritative=returnCode==1&&nativeType=="System.Boolean";
                }
                else if(operation=="save_owned_document_as")authoritative=ArtifactProof.Fields(details,"nativeRequestId","saveCommandReturned")&&ArtifactProof.Boolean(details["saveCommandReturned"],true);
                else authoritative=ArtifactProof.Fields(details,"nativeRequestId");
            }
            output["authoritativeSuccessReturnRecorded"]=authoritative;output["recordedReturnCode"]=returnCode;
            string? claimPath=null;JsonObject? claim=null;
            if(operation=="translate_owned_solid")
            {
                var packet=ArtifactProof.Verified(root,"ownedoperations",Text(plan,"ownedOperationId"),Text(plan,"ownedOperationSha256"),65536);
                Require(JsonNode.DeepEquals(packet["copyId"],plan["copyId"])&&JsonNode.DeepEquals(packet["ownershipSha256"],plan["ownershipSha256"])&&FileArtifacts.Token(Text(packet,"operationPlanId")),"recovery_packet_identity_invalid");
                claimPath="operationplans/"+Text(packet,"operationPlanId")+".execution-once.json";
            }
            else if(operation=="move_owned_solid_to_level")
            {
                var packet=ArtifactProof.Verified(root,"ownedlevelmoves",Text(plan,"ownedLevelMoveId"),Text(plan,"ownedLevelMoveSha256"),16000000);
                Require(Text(packet,"operation")=="move_entity_to_level"&&JsonNode.DeepEquals(packet["ownedLevelMoveId"],plan["ownedLevelMoveId"])&&JsonNode.DeepEquals(packet["copyId"],plan["copyId"])&&JsonNode.DeepEquals(packet["ownershipSha256"],plan["ownershipSha256"]),"recovery_packet_identity_invalid");
                claimPath="ownedlevelmoves/"+Text(plan,"ownedLevelMoveId")+".execution-once.json";
            }
            else if(operation=="save_owned_document_as")
            {Require(FileArtifacts.Token(Text(plan,"targetId")),"recovery_save_target_invalid");claimPath="savedresults/"+Text(plan,"targetId")+"/save-once.json";}
            else claimPath="workingcopies/"+copy+"/open-once.json";
            bool claimPresent=File.Exists(FileArtifacts.FixedPath(root,claimPath));if(claimPresent)
            {
                claim=Json(claimPath);Require(Text(claim,"operationId")==operationId&&Text(claim,"planSha256")==planSha&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true),"recovery_claim_differs");
                Require(operation=="translate_owned_solid"?ArtifactProof.Fields(claim,"operationId","planSha256","ownedOperationId","copyId","automaticReplayForbidden")&&JsonNode.DeepEquals(claim["ownedOperationId"],plan["ownedOperationId"]):
                    operation=="move_owned_solid_to_level"?ArtifactProof.Fields(claim,"operationId","planSha256","ownedLevelMoveId","copyId","automaticReplayForbidden")&&JsonNode.DeepEquals(claim["ownedLevelMoveId"],plan["ownedLevelMoveId"]):
                    operation=="save_owned_document_as"?ArtifactProof.Fields(claim,"operationId","planSha256","targetId","automaticReplayForbidden"):
                    ArtifactProof.Fields(claim,"operationId","planSha256","copyId","ownershipSha256","automaticReplayForbidden")&&JsonNode.DeepEquals(claim["ownershipSha256"],plan["ownershipSha256"]),"recovery_claim_schema_or_dependency_invalid");
                Require(operation=="save_owned_document_as"?JsonNode.DeepEquals(claim["targetId"],plan["targetId"]):JsonNode.DeepEquals(claim["copyId"],plan["copyId"]),"recovery_claim_owner_differs");
            }
            output["claimPresent"]=claimPresent;output["claim"]=claim?.DeepClone();
            var fencePath=FileArtifacts.FixedPath(root,"workingcopies",copy,"mutation-fence.json");bool fencePresent=File.Exists(fencePath);output["fencePresent"]=fencePresent;
            if(fencePresent)
            {
                var fence=Json(Path.GetRelativePath(root,fencePath));Require(ArtifactProof.Fields(fence,"schemaVersion","operationId","planSha256","copyId","ownershipSha256","automaticReplayForbidden")&&ArtifactProof.IntegerIs(fence["schemaVersion"],1)&&
                    Text(fence,"operationId")==operationId&&Text(fence,"planSha256")==planSha&&Text(fence,"copyId")==copy&&Text(fence,"ownershipSha256")==Text(plan,"ownershipSha256")&&ArtifactProof.Boolean(fence["automaticReplayForbidden"],true),"recovery_fence_foreign_or_invalid");
            }
            Require(recorded=="prepared"||recorded=="rejected_before_change"||claimPresent,"recovery_claim_missing");
            bool usesFence=operation is "translate_owned_solid" or "save_owned_document_as" or "move_owned_solid_to_level";
            bool terminal=recorded=="succeeded_verified"||recorded=="rejected_before_change";
            if(terminal)
            {
                foreach(var reference in events.Last()!["details"]!["evidence"]!.AsArray())
                    ArtifactProof.Verified(root,"responses",Text(reference!.AsObject(),"requestId"),Text(reference.AsObject(),"sha256"),16777216);
            }
            bool locked=ArtifactProof.Boolean(journal["writerLockPresent"],true);
            string assessment=locked?"journal_writer_or_partial_publication":!checkpointMatches?"checkpoint_protection_changed":!workMatches?"working_saved_bytes_changed":usesFence&&!terminal&&recorded!="prepared"&&!fencePresent?"unresolved_fence_missing":
                recorded=="succeeded_verified"?(usesFence&&fencePresent?"verified_record_but_fence_retained":"historically_verified_requires_fresh_context"):
                recorded=="rejected_before_change"?"recorded_rejection_before_change":recorded=="recovery_required"?"recorded_recovery_required":
                returned.Length==1&&!authoritative?"non_success_or_unproven_return_requires_recovery":authoritative?"returned_requires_independent_verification":started.Length==1?"started_without_authoritative_return":
                "no_authoritative_command_start_recorded";
            output["assessment"]=assessment;
            output["resolutionHistory"]=new OutcomeResolutionJournal(root).Read(operationId,cancellationToken);
            // Even a verified journal does not establish today's CAD state. This
            // reader NEVER clears unknown or issues execution/recovery authority.
            output["outcome"]="not_resolved_by_recorded_inspection";
            Deadline();var final=new OperationJournal(root).Read(operationId);Require(JsonNode.DeepEquals(final,read),"recovery_journal_changed_during_inspection");
            Require(FileArtifacts.Sha256(FileArtifacts.ReadBytes(root,Path.GetRelativePath(root,plans[0].Path),planLimit))==planSha,"recovery_plan_changed_during_inspection");
            return output;
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){output["state"]="recovery_inspection_cancelled";output["errorCode"]="cancelled_read_only_inspection";return output;}
        catch(Exception error)when(error is ReferenceRejected or IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {output["state"]="recovery_inspection_unavailable";output["errorCode"]=error is ReferenceRejected rejected?rejected.Code:"recovery_artifact_unreadable";output["error"]=error.Message;return output;}
    }

    private JsonObject ReadLegacyActivation(string id,JsonObject original,JsonObject receipt,JsonObject output,Action deadline)
    {
        var state=Text(receipt,"state");var phase=Text(receipt,"phase");
        Require(Text(receipt,"operationId")==id&&ArtifactProof.Boolean(receipt["geometryMutationAttempted"],false)
            &&receipt["contextChangeAttempted"] is JsonValue attempted&&attempted.TryGetValue<bool>(out _),"recovery_legacy_activation_invalid");
        Require((phase=="before_activate"&&state=="context_change_outcome_pending")
            ||(phase=="completed"&&state=="document_active_verified")
            ||(phase=="stopped"&&state is "context_change_outcome_unknown" or "rejected_before_context_change"),"recovery_legacy_activation_state_invalid");
        Require(!receipt.ContainsKey("cadSaveAttempted")||ArtifactProof.Boolean(receipt["cadSaveAttempted"],false),"recovery_legacy_activation_save_invalid");
        Require(DateTimeOffset.TryParse(Text(receipt,"updatedUtc"),System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,out _),"recovery_legacy_activation_time_invalid");
        var once=FileArtifacts.FixedPath(root,"operations",id+".once");var temporary=FileArtifacts.FixedPath(root,"operations",id+".json.tmp");
        Require(!Directory.Exists(once)&&!Directory.Exists(temporary),"recovery_legacy_activation_marker_invalid");
        bool claimed=File.Exists(once),partial=File.Exists(temporary);
        if(claimed)Require(FileArtifacts.ReadBytes(root,Path.GetRelativePath(root,once),64).Length==0,"recovery_legacy_activation_claim_invalid");
        Require(phase=="stopped"&&!ArtifactProof.Boolean(receipt["contextChangeAttempted"],true)||claimed,"recovery_legacy_activation_claim_missing");
        output["operation"]="activate_document";output["journalFormat"]="legacy_single_receipt";
        output["journal"]=receipt.DeepClone();output["journalSha256"]=original["receiptSha256"]!.DeepClone();
        output["claimPresent"]=claimed;output["temporaryPublicationPresent"]=partial;
        output["activationAttemptRecorded"]=receipt["contextChangeAttempted"]!.DeepClone();
        output["commandStartedRecorded"]=false;output["commandReturnedRecorded"]=false;
        output["authoritativeSuccessReturnRecorded"]=false;output["currentContextChecked"]=false;
        output["savedFilesChecked"]=false;output["checkpointRestoreAuthorized"]=false;
        output["assessment"]=partial?"legacy_activation_partial_publication":phase=="completed"?"legacy_activation_historical_observation_requires_fresh_context":
            phase=="before_activate"||ArtifactProof.Boolean(receipt["contextChangeAttempted"],true)?"legacy_activation_without_authoritative_return":"legacy_activation_recorded_rejection";
        output["outcome"]="not_resolved_by_recorded_inspection";
        output["qualification"]="Legacy activation receipt/opaque once marker only; no append-only command-return proof. Saved-file paths in the receipt are data and are not opened. No current CAD verification, journal repair, replay, inverse or checkpoint restore authority.";
        deadline();Require(JsonNode.DeepEquals(original,new OperationJournal(root).Read(id))&&claimed==File.Exists(once)&&partial==File.Exists(temporary),"recovery_legacy_activation_changed_during_inspection");
        if(claimed)Require(FileArtifacts.ReadBytes(root,Path.GetRelativePath(root,once),64).Length==0,"recovery_legacy_activation_claim_invalid");
        return output;
    }
}
