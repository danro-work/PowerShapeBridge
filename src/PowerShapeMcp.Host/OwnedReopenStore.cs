using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Prepared saved-byte alias only. No OPEN, model binding, queue or COM call.
public sealed class OwnedReopenStore(string root,OwnedSavedResultStore saved,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(string Id,string Sha,JsonObject Source,double Expires);
    private readonly Dictionary<string,Lease> handles=[];
    public void Clear()=>handles.Clear();
    private static string Text(JsonObject value,string key)=>ArtifactProof.String(value,key,"saved_reopen_proof_invalid");
    private static void Require(bool condition,string code){if(!condition)throw new ReferenceRejected(code);}
    public JsonObject PrepareParameters(string handle)=>saved.Parameters(handle);
    public JsonObject VerifyReceipt(JsonObject result,JsonObject? parameters=null)
    {
        var id=Text(result,"reopenId");Require(FileArtifacts.Token(id)&&FileArtifacts.Token(Text(result,"reopenSha256"),64),"saved_reopen_identity_invalid");
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("savedreopens",id,"reopen.json"),65536);
        Require(FileArtifacts.Sha256(raw)==Text(result,"reopenSha256"),"saved_reopen_artifact_sha_differs");
        var plan=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        Require(ArtifactProof.Fields(plan,"schemaVersion","reopenId","operation","bridgeVersion","preparedUtc","expiresUtc","executionEnabled","verificationRequestId","verificationReceiptSha256","saveOperationId",
            "targetId","targetSha256","savedResult","seed","copyId","ownershipSha256","workingPath","workingSha256","checkpointPath","checkpointSha256","directoryIdentity")&&
            ArtifactProof.IntegerIs(plan["schemaVersion"],1)&&ArtifactProof.Boolean(plan["executionEnabled"],false)&&Text(plan,"reopenId")==id&&Text(plan,"copyId")==id&&Text(plan,"operation")=="reopen_owned_saved_result"&&JsonNode.DeepEquals(plan,result["reopen"]),"saved_reopen_plan_invalid");
        var source=new JsonObject{["verificationRequestId"]=plan["verificationRequestId"]!.DeepClone(),["verificationReceiptSha256"]=plan["verificationReceiptSha256"]!.DeepClone()};
        if(parameters is not null)Require(JsonNode.DeepEquals(parameters,source),"saved_reopen_source_identity_differs");
        var verified=ArtifactProof.Verified(root,"responses",Text(source,"verificationRequestId"),Text(source,"verificationReceiptSha256"),16777216);
        Require(verified["evidence"] is JsonArray entries&&entries.Count==3&&entries.All(e=>e is JsonObject reference&&ArtifactProof.Fields(reference,"requestId","sha256")),"saved_reopen_source_evidence_invalid");
        var evidence=verified["evidence"]!.AsArray();
        var listing=ArtifactProof.Verified(root,"responses",evidence[2]!["requestId"]!.GetValue<string>(),evidence[2]!["sha256"]!.GetValue<string>(),16777216);
        var proof=new JsonObject{["saveRequestId"]=verified["saveOperationId"]!.DeepClone(),["saveReceiptSha256"]=verified["saveReceiptSha256"]!.DeepClone(),
            ["inspectionRequestId"]=evidence[1]!["requestId"]!.DeepClone(),["inspectionSha256"]=evidence[1]!["sha256"]!.DeepClone(),["listingRequestId"]=evidence[2]!["requestId"]!.DeepClone(),["listingSha256"]=evidence[2]!["sha256"]!.DeepClone(),
            ["modelId"]=listing["activeWindow"]!["modelId"]!.DeepClone(),["windowNumber"]=listing["activeWindow"]!["windowNumber"]!.DeepClone(),["expiresUtc"]=verified["expiresUtc"]!.DeepClone()};
        saved.VerifyReceipt(verified,proof);
        foreach(var field in new[]{"targetId","targetSha256","savedResult"})Require(JsonNode.DeepEquals(plan[field],verified[field]),"saved_reopen_saved_dependency_differs");
        Require(JsonNode.DeepEquals(plan["saveOperationId"],verified["saveOperationId"]),"saved_reopen_save_identity_differs");
        Require(plan["savedResult"] is JsonObject&&plan["seed"] is JsonObject&&plan["directoryIdentity"] is JsonObject,"saved_reopen_file_records_invalid");
        var hash=Text(plan["savedResult"]!.AsObject(),"sha256");
        foreach(var key in new[]{"workingSha256","checkpointSha256"})Require(Text(plan,key)==hash,"saved_reopen_copied_hash_differs");
        var seed=plan["seed"]!.AsObject();Require(ArtifactProof.Fields(seed,"path","sha256","length","fileIdentity")&&Text(seed,"sha256")==hash&&JsonNode.DeepEquals(seed["length"],plan["savedResult"]!["length"]),"saved_reopen_seed_invalid");
        CheckPath(Text(seed,"path"),FileArtifacts.FixedPath(root,"savedreopens",id,"DiskReopen_"+id+".psmodel"));
        CheckPath(Text(plan,"workingPath"),FileArtifacts.FixedPath(root,"workingcopies",id,"Working_"+id+".psmodel"));
        CheckPath(Text(plan,"checkpointPath"),FileArtifacts.FixedPath(root,"workingcopies",id,"checkpoint","Before.psmodel"));
        Require(seed["fileIdentity"] is JsonObject,"saved_reopen_file_identity_invalid");
        Identity(seed["fileIdentity"]!.AsObject());Identity(plan["directoryIdentity"]!.AsObject());
        var ownershipRaw=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",id,"ownership.json"),65536);
        Require(FileArtifacts.Sha256(ownershipRaw)==Text(plan,"ownershipSha256"),"saved_reopen_ownership_sha_differs");
        var ownership=JsonNode.Parse(FileArtifacts.Parse(ownershipRaw).GetRawText())!.AsObject();
        Require(ArtifactProof.Fields(ownership,"schemaVersion","copyId","bridgeVersion","createdUtc","scope","executionEnabled","source","working","checkpoint")&&ArtifactProof.IntegerIs(ownership["schemaVersion"],2)&&
            Text(ownership,"copyId")==id&&ArtifactProof.Boolean(ownership["executionEnabled"],false)&&Text(ownership,"scope")=="saved_disk_bytes_only", "saved_reopen_ownership_invalid");
        Require(JsonNode.DeepEquals(ownership["source"],seed)&&JsonNode.DeepEquals(ownership["working"]?["initialSha256"],seed["sha256"])&&JsonNode.DeepEquals(ownership["checkpoint"]?["sha256"],seed["sha256"]),"saved_reopen_ownership_seed_differs");
        Require(ownership["working"] is JsonObject work&&ArtifactProof.Fields(work,"path","initialSha256","length","fileIdentity")&&work["fileIdentity"] is JsonObject&&JsonNode.DeepEquals(work["length"],seed["length"])&&
            ownership["checkpoint"] is JsonObject before&&ArtifactProof.Fields(before,"path","sha256","length","fileIdentity")&&before["fileIdentity"] is JsonObject&&JsonNode.DeepEquals(before["length"],seed["length"]),"saved_reopen_ownership_records_invalid");
        CheckPath(Text(ownership["working"]!.AsObject(),"path"),Text(plan,"workingPath"));CheckPath(Text(ownership["checkpoint"]!.AsObject(),"path"),Text(plan,"checkpointPath"));
        var seedIdentity=seed["fileIdentity"]!.AsObject();var workIdentity=ownership["working"]!["fileIdentity"]!.AsObject();var checkpointIdentity=ownership["checkpoint"]!["fileIdentity"]!.AsObject();
        Identity(workIdentity);Identity(checkpointIdentity);
        var ids=new[]{seedIdentity,workIdentity,checkpointIdentity,plan["savedResult"]!["fileIdentity"]!.AsObject()}.Select(i=>Text(i,"volumeSerial")+":"+Text(i,"fileIndex")).ToArray();
        Require(ids.Distinct(StringComparer.Ordinal).Count()==4,"saved_reopen_file_alias_invalid");
        foreach(var path in new[]{Text(seed,"path"),Text(plan,"workingPath"),Text(plan,"checkpointPath")})Bytes(path,hash,seed["length"]!);
        Require((File.GetAttributes(Text(seed,"path"))&FileAttributes.ReadOnly)!=0&&(File.GetAttributes(Text(plan,"checkpointPath"))&FileAttributes.ReadOnly)!=0,"saved_reopen_readonly_protection_lost");
        return plan;
    }
    private static void Identity(JsonObject value)=>Require(ArtifactProof.Fields(value,"volumeSerial","fileIndex","creationTime")&&FileArtifacts.Token(Text(value,"volumeSerial"),8)&&FileArtifacts.Token(Text(value,"fileIndex"),16)&&FileArtifacts.Token(Text(value,"creationTime"),16),"saved_reopen_file_identity_invalid");
    private static void CheckPath(string value,string expected)=>Require(ObservationPolicy.NormalizePath(value)==ObservationPolicy.NormalizePath(expected),"saved_reopen_fixed_path_differs");
    private void Bytes(string path,string hash,JsonNode length)
    {
        path=FileArtifacts.FixedPath(root,Path.GetRelativePath(root,path));var size=JsonSerializer.SerializeToElement(length);
        Require(size.ValueKind==JsonValueKind.Number&&size.TryGetInt64(out var count)&&count is >0 and <=1073741824,"saved_reopen_size_invalid");
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);Require(stream.Length==size.GetInt64(),"saved_reopen_bytes_changed");
        var first=Convert.ToHexStringLower(SHA256.HashData(stream));stream.Position=0;Require(first==hash&&Convert.ToHexStringLower(SHA256.HashData(stream))==hash&&stream.Length==size.GetInt64(),"saved_reopen_bytes_changed");
    }
    public JsonObject Issue(JsonObject result,JsonObject parameters,string savedHandle)
    {
        Require(result["state"]?.GetValue<string>()=="owned_saved_reopen_prepared_files"&&JsonNode.DeepEquals(result["bridgeRequestId"],result["reopenId"])&&
            new[]{"executionEnabled","cadSaveAttempted","contextChangeAttempted","geometryMutationAttempted","powerShapeConnectionAttempted"}.All(k=>ArtifactProof.Boolean(result[k],false)),"saved_reopen_native_receipt_invalid");
        Require(JsonNode.DeepEquals(PrepareParameters(savedHandle),parameters),"saved_reopen_saved_lease_lost");
        var plan=VerifyReceipt(result,parameters);Unclaimed(plan);
        var remaining=Math.Min(LeaseTime.Remaining(Text(plan,"preparedUtc"),utcNow(),"saved_reopen_expired").Remaining,(LeaseTime.Parse(Text(plan,"expiresUtc"),"saved_reopen_expired")-LeaseTime.PythonPrecision(utcNow())).TotalSeconds);
        remaining=Math.Min(remaining,saved.Remaining(savedHandle));
        Require(remaining>0&&remaining<=300,"saved_reopen_expired");
        foreach(var token in handles.Where(p=>p.Value.Expires<=now()).Select(p=>p.Key).ToArray())handles.Remove(token);
        if(handles.Count>=128)Clear();var handle=Guid.NewGuid().ToString("N");handles[handle]=new(Text(plan,"reopenId"),Text(result,"reopenSha256"),parameters.DeepClone().AsObject(),now()+remaining);
        var output=result.DeepClone().AsObject();output["ownedReopenHandle"]=handle;output["reopenLifetimeSeconds"]=(int)remaining;return output;
    }
    public JsonObject Parameters(string handle)
    {
        if(!handles.TryGetValue(handle,out var lease)||lease.Expires<=now()){handles.Remove(handle);throw new ReferenceRejected("saved_reopen_handle_unknown_or_expired");}
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("savedreopens",lease.Id,"reopen.json"),65536);var plan=VerifyReceipt(new(){["reopenId"]=lease.Id,["reopenSha256"]=lease.Sha,["reopen"]=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())},lease.Source);
        Unclaimed(plan);return new(){["reopenId"]=lease.Id,["reopenSha256"]=lease.Sha};
    }
    public void Revoke(string handle)=>handles.Remove(handle);
    private void Unclaimed(JsonObject plan)
    {
        var copy=Text(plan,"copyId");foreach(var name in new[]{"open-once.json","mutation-fence.json"})
        {var path=FileArtifacts.FixedPath(root,"workingcopies",copy,name);Require(!File.Exists(path)&&!Directory.Exists(path),"saved_reopen_copy_claimed_or_fenced");}
    }
}
