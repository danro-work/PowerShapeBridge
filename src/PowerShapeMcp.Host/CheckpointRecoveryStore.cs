using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Connection-scoped, one-use filesystem intent. No CAD/OPEN/resolution authority.
public sealed class CheckpointRecoveryStore(string root,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(JsonObject Parameters,double Expires);
    private readonly Dictionary<string,Lease> handles=[];
    public void Clear()=>handles.Clear();
    public void Revoke(string handle)=>handles.Remove(handle);
    private static void Require(bool ok,string code){if(!ok)throw new ReferenceRejected(code);}
    private static string Text(JsonObject value,string key)=>ArtifactProof.String(value,key,"checkpoint_recovery_proof_invalid");
    private static void Identity(JsonObject value)=>Require(ArtifactProof.Fields(value,"volumeSerial","fileIndex","creationTime")&&FileArtifacts.Token(Text(value,"volumeSerial"),8)&&FileArtifacts.Token(Text(value,"fileIndex"),16)&&FileArtifacts.Token(Text(value,"creationTime"),16),"checkpoint_recovery_identity_invalid");
    private void PathEquals(string path,string expected)=>Require(ObservationPolicy.NormalizePath(path)==ObservationPolicy.NormalizePath(expected),"checkpoint_recovery_path_invalid");
    private void Bytes(JsonObject value,string hash,string path,string hashField="sha256")
    {
        PathEquals(Text(value,"path"),path);Require(Text(value,hashField)==hash&&FileArtifacts.Token(hash,64),"checkpoint_recovery_hash_invalid");
        Require(value["length"] is JsonValue size&&size.TryGetValue<long>(out var length)&&length is >0 and <=1073741824,"checkpoint_recovery_size_invalid");
        path=FileArtifacts.FixedPath(root,Path.GetRelativePath(root,path));
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        var expected=value["length"]!.GetValue<long>();Require(stream.Length==expected,"checkpoint_recovery_bytes_changed");
        Require(Convert.ToHexStringLower(SHA256.HashData(stream))==hash,"checkpoint_recovery_bytes_changed");stream.Position=0;
        Require(Convert.ToHexStringLower(SHA256.HashData(stream))==hash&&stream.Length==expected,"checkpoint_recovery_bytes_changed");Identity(value["fileIdentity"]!.AsObject());
    }
    private JsonObject VerifyPlan(string id,string sha)
    {
        var plan=ArtifactProof.Verified(root,"checkpointrecoveries",id,sha,65536);
        Require(ArtifactProof.Fields(plan,"schemaVersion","recoveryId","operation","bridgeVersion","preparedUtc","expiresUtc","cadExecutionEnabled","originalOutcomeResolved","origin","checkpoint")
            &&ArtifactProof.IntegerIs(plan["schemaVersion"],1)&&Text(plan,"recoveryId")==id&&Text(plan,"operation")=="copy_owned_checkpoint"
            &&ArtifactProof.Boolean(plan["cadExecutionEnabled"],false)&&ArtifactProof.Boolean(plan["originalOutcomeResolved"],false),"checkpoint_recovery_plan_invalid");
        var origin=plan["origin"]!.AsObject();Require(ArtifactProof.Fields(origin,"operationId","operationPlanSha256","planFolder","copyId","ownershipSha256"),"checkpoint_recovery_origin_invalid");
        var folder=Text(origin,"planFolder");Require(new[]{"ownedexecutions","saveexecutions","copyopenplans"}.Contains(folder),"checkpoint_recovery_origin_location_invalid");
        var original=ArtifactProof.Verified(root,folder,Text(origin,"operationId"),Text(origin,"operationPlanSha256"),65536);
        Require(JsonNode.DeepEquals(original["operationId"],origin["operationId"])&&JsonNode.DeepEquals(original["copyId"],origin["copyId"])&&JsonNode.DeepEquals(original["ownershipSha256"],origin["ownershipSha256"])
            &&ArtifactProof.IntegerIs(original["schemaVersion"],1),"checkpoint_recovery_origin_identity_differs");
        var operation=Text(original,"operation");Require(folder=="ownedexecutions"&&operation=="translate_owned_solid"||folder=="saveexecutions"&&operation=="save_owned_document_as"||folder=="copyopenplans"&&operation is "open_working_copy" or "open_owned_saved_result","checkpoint_recovery_origin_operation_invalid");
        var copy=Text(origin,"copyId");Require(FileArtifacts.Token(copy),"checkpoint_recovery_origin_copy_invalid");
        var ownerRaw=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",copy,"ownership.json"),65536);
        Require(FileArtifacts.Sha256(ownerRaw)==Text(origin,"ownershipSha256"),"checkpoint_recovery_owner_sha_differs");
        var owner=JsonNode.Parse(FileArtifacts.Parse(ownerRaw).GetRawText())!.AsObject();
        Require(ArtifactProof.Fields(owner,"schemaVersion","copyId","bridgeVersion","createdUtc","scope","executionEnabled","source","working","checkpoint")
            &&JsonNode.DeepEquals(owner["copyId"],origin["copyId"])&&Text(owner,"scope")=="saved_disk_bytes_only"&&ArtifactProof.Boolean(owner["executionEnabled"],false)
            &&(ArtifactProof.IntegerIs(owner["schemaVersion"],1)||ArtifactProof.IntegerIs(owner["schemaVersion"],2)),"checkpoint_recovery_owner_invalid");
        Require(JsonNode.DeepEquals(owner["checkpoint"],plan["checkpoint"]),"checkpoint_recovery_checkpoint_role_differs");
        var checkpoint=plan["checkpoint"]!.AsObject();Require(ArtifactProof.Fields(checkpoint,"path","sha256","length","fileIdentity"),"checkpoint_recovery_checkpoint_invalid");
        var path=FileArtifacts.FixedPath(root,"workingcopies",copy,"checkpoint","Before.psmodel");Bytes(checkpoint,Text(checkpoint,"sha256"),path);
        Require((File.GetAttributes(path)&FileAttributes.ReadOnly)!=0,"checkpoint_recovery_checkpoint_writable");
        return plan;
    }
    public JsonObject Issue(JsonObject result,JsonObject parameters)
    {
        Require(result["state"]?.GetValue<string>()=="checkpoint_recovery_prepared_files"&&new[]{"geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","powerShapeConnectionAttempted","originalOutcomeResolved"}.All(key=>ArtifactProof.Boolean(result[key],false)),"checkpoint_recovery_receipt_invalid");
        var id=Text(result,"recoveryId");Require(JsonNode.DeepEquals(result["bridgeRequestId"],result["recoveryId"]),"checkpoint_recovery_receipt_identity_differs");
        var sha=Text(result,"recoverySha256");var plan=VerifyPlan(id,sha);
        Require(JsonNode.DeepEquals(plan,result["recovery"])&&JsonNode.DeepEquals(plan["origin"]?["operationId"],parameters["operationId"])&&JsonNode.DeepEquals(plan["origin"]?["operationPlanSha256"],parameters["operationPlanSha256"]),"checkpoint_recovery_source_differs");
        Require(!File.Exists(FileArtifacts.FixedPath(root,"checkpointrecoveries",id+".copy-once.json")),"checkpoint_recovery_already_claimed");
        var remaining=Math.Min(LeaseTime.Remaining(Text(plan,"preparedUtc"),utcNow(),"checkpoint_recovery_expired").Remaining,(LeaseTime.Parse(Text(plan,"expiresUtc"),"checkpoint_recovery_expired")-LeaseTime.PythonPrecision(utcNow())).TotalSeconds);
        Require(remaining>0&&remaining<=300,"checkpoint_recovery_expired");foreach(var expired in handles.Where(pair=>pair.Value.Expires<=now()).Select(pair=>pair.Key).ToArray())handles.Remove(expired);
        Require(handles.Count<128,"checkpoint_recovery_handle_limit");var handle=Guid.NewGuid().ToString("N");handles[handle]=new(new(){["recoveryId"]=id,["recoverySha256"]=sha},now()+remaining);
        var output=result.DeepClone().AsObject();output["checkpointRecoveryHandle"]=handle;return output;
    }
    public JsonObject Consume(string handle)
    {
        Require(handles.Remove(handle,out var lease)&&lease.Expires>now(),"checkpoint_recovery_handle_unknown_or_expired");
        var parameters=lease!.Parameters.DeepClone().AsObject();VerifyPlan(Text(parameters,"recoveryId"),Text(parameters,"recoverySha256"));return parameters;
    }
    public JsonObject CheckCreated(JsonObject result,JsonObject parameters)
    {
        Require(result["state"]?.GetValue<string>()=="checkpoint_working_copy_prepared_disk"&&new[]{"geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","powerShapeConnectionAttempted","originalOutcomeResolved"}.All(key=>ArtifactProof.Boolean(result[key],false))&&ArtifactProof.Boolean(result["originalArtifactsUnchanged"],true),"checkpoint_copy_receipt_invalid");
        var id=Text(result,"copyId");Require(FileArtifacts.Token(id)&&Text(result,"bridgeRequestId")==id&&JsonNode.DeepEquals(parameters["recoveryId"],result["recoveryId"])&&JsonNode.DeepEquals(parameters["recoverySha256"],result["recoverySha256"]),"checkpoint_copy_identity_differs");
        var recovery=VerifyPlan(Text(parameters,"recoveryId"),Text(parameters,"recoverySha256"));
        var creation=ArtifactProof.Verified(root,"checkpointcopyplans",id,Text(result,"creationPlanSha256"),65536);
        Require(ArtifactProof.Fields(creation,"schemaVersion","operationId","operation","recoveryId","recoverySha256","origin","checkpoint","cadExecutionEnabled","originalOutcomeResolved","createdUtc")
            &&ArtifactProof.IntegerIs(creation["schemaVersion"],1)&&ArtifactProof.Boolean(creation["cadExecutionEnabled"],false)&&ArtifactProof.Boolean(creation["originalOutcomeResolved"],false)
            &&JsonNode.DeepEquals(creation,result["creationPlan"])&&Text(creation,"operationId")==id&&Text(creation,"operation")=="materialize_owned_checkpoint"
            &&JsonNode.DeepEquals(creation["origin"],recovery["origin"])&&JsonNode.DeepEquals(creation["checkpoint"],recovery["checkpoint"])&&JsonNode.DeepEquals(creation["recoveryId"],parameters["recoveryId"])&&JsonNode.DeepEquals(creation["recoverySha256"],parameters["recoverySha256"]),"checkpoint_copy_plan_differs");
        var claimRaw=FileArtifacts.ReadBytes(root,Path.Combine("checkpointrecoveries",Text(parameters,"recoveryId")+".copy-once.json"),65536);var claim=JsonNode.Parse(FileArtifacts.Parse(claimRaw).GetRawText())!.AsObject();
        Require(ArtifactProof.Fields(claim,"operationId","recoveryId","planSha256","automaticReplayForbidden")&&Text(claim,"operationId")==id&&JsonNode.DeepEquals(claim["planSha256"],result["creationPlanSha256"])&&JsonNode.DeepEquals(claim["recoveryId"],parameters["recoveryId"])&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true),"checkpoint_copy_claim_differs");
        var proofRaw=FileArtifacts.ReadBytes(root,Path.Combine("checkpointcopyplans",id+".result.json"),16777216);Require(FileArtifacts.Sha256(proofRaw)==Text(result,"materializationProofSha256"),"checkpoint_copy_proof_sha_differs");
        var proof=JsonNode.Parse(FileArtifacts.Parse(proofRaw).GetRawText())!.AsObject();var expected=result.DeepClone().AsObject();expected.Remove("bridgeRequestId");expected.Remove("materializationProofSha256");Require(JsonNode.DeepEquals(proof,expected),"checkpoint_copy_proof_body_differs");
        var ownerRaw=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",id,"ownership.json"),65536);Require(FileArtifacts.Sha256(ownerRaw)==Text(result,"ownershipSha256"),"checkpoint_copy_owner_sha_differs");var owner=JsonNode.Parse(FileArtifacts.Parse(ownerRaw).GetRawText())!.AsObject();
        Require(ArtifactProof.Fields(owner,"schemaVersion","copyId","bridgeVersion","createdUtc","scope","executionEnabled","source","working","checkpoint")
            &&JsonNode.DeepEquals(owner,result["ownership"])&&ArtifactProof.IntegerIs(owner["schemaVersion"],2)&&Text(owner,"copyId")==id&&Text(owner,"scope")=="saved_disk_bytes_only"&&ArtifactProof.Boolean(owner["executionEnabled"],false)&&JsonNode.DeepEquals(owner["source"],recovery["checkpoint"]),"checkpoint_copy_owner_differs");
        var source=recovery["checkpoint"]!.AsObject();var hash=Text(source,"sha256");var work=owner["working"]!.AsObject();var checkpoint=owner["checkpoint"]!.AsObject();
        Require(ArtifactProof.Fields(work,"path","initialSha256","length","fileIdentity")&&ArtifactProof.Fields(checkpoint,"path","sha256","length","fileIdentity"),"checkpoint_copy_file_records_invalid");
        Bytes(work,hash,FileArtifacts.FixedPath(root,"workingcopies",id,"Working_"+id+".psmodel"),"initialSha256");Bytes(checkpoint,hash,FileArtifacts.FixedPath(root,"workingcopies",id,"checkpoint","Before.psmodel"));
        Require(JsonNode.DeepEquals(work["length"],source["length"])&&JsonNode.DeepEquals(checkpoint["length"],source["length"])
            &&(File.GetAttributes(Text(checkpoint,"path"))&FileAttributes.ReadOnly)!=0,"checkpoint_copy_protection_differs");
        Require(new[]{source,work,checkpoint}.Select(record=>Text(record["fileIdentity"]!.AsObject(),"volumeSerial")+":"+Text(record["fileIdentity"]!.AsObject(),"fileIndex")).Distinct().Count()==3,"checkpoint_copy_alias_invalid");
        Require(result["originalArtifactHashes"] is JsonObject,"checkpoint_copy_origin_hashes_missing");
        foreach(var pair in result["originalArtifactHashes"]!.AsObject())
        {
            var path=FileArtifacts.FixedPath(root,Path.GetRelativePath(root,pair.Key));using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            Require(Convert.ToHexStringLower(SHA256.HashData(stream))==pair.Value!.GetValue<string>(),"checkpoint_copy_original_artifact_changed");
        }
        var output=result.DeepClone().AsObject();output["state"]="working_copy_prepared_disk";return output;
    }
}
