using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal sealed class OwnedDgkTargetStore(string root,CandidateStore candidates,DocumentBindingStore bindings,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(string Set,string Binding,JsonObject Source,JsonObject Reference,double Expires);
    private readonly Dictionary<string,Lease> targets=[];
    private const string Code="dgk_target_receipt_invalid";
    private static string Text(JsonObject value,string field)=>ArtifactProof.String(value,field,Code);
    private static void Require(bool ok,string code=Code){if(!ok)throw new ReferenceRejected(code);}
    private static readonly string[] References=["bindingId","bindingSha256","entitySetId","entitySetSha256"];
    public void Clear()=>targets.Clear();
    public void Revoke(string handle)=>targets.Remove(handle);
    public JsonObject PrepareParameters(string setHandle,string bindingHandle,JsonObject parameters)
    {
        var definition=DgkTargetDefinition.Read(parameters);var setArgs=candidates.SetParameters(setHandle);var bindingArgs=bindings.ValidationParameters(bindingHandle);
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(bindingArgs,"bindingId"),Text(bindingArgs,"bindingSha256"),65536);
        bindings.VerifyReceipt(new(){["bindingId"]=bindingArgs["bindingId"]!.DeepClone(),["bindingSha256"]=bindingArgs["bindingSha256"]!.DeepClone(),["binding"]=binding.DeepClone()});
        var set=OperationPlanStore.ReadSet(root,setArgs,LeaseTime.PythonPrecision(utcNow()));
        var bound=ArtifactProof.Verified(root,"responses",Text(binding,"inspectionRequestId"),Text(binding,"inspectionSha256"),16000000);
        Require(candidates.Context==set.Context&&JsonNode.DeepEquals(OperationPlanStore.SourceObservations(set.Source),OperationPlanStore.SourceObservations(bound)),"dgk_target_set_binding_context_differs");
        var result=bindingArgs.DeepClone().AsObject();foreach(var pair in setArgs)result[pair.Key]=pair.Value?.DeepClone();result["parameters"]=definition;return result;
    }
    private static bool Same(string a,string b)=>ObservationPolicy.NormalizePath(a)==ObservationPolicy.NormalizePath(b);
    private static string HashFile(string path)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        Require(file.Length is >0 and <=1073741824,"dgk_target_owned_bytes_size_invalid");return Convert.ToHexStringLower(SHA256.HashData(file));
    }
    private void Ownership(JsonObject plan,JsonObject source)
    {
        var id=Text(plan,"copyId");var raw=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",id,"ownership.json"),65536);
        Require(FileArtifacts.Sha256(raw)==Text(plan,"ownershipSha256"),"dgk_target_owner_sha_differs");var owner=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        Require(ArtifactProof.Fields(owner,"schemaVersion","copyId","bridgeVersion","createdUtc","scope","executionEnabled","source","working","checkpoint")&&
            (ArtifactProof.IntegerIs(owner["schemaVersion"],1)||ArtifactProof.IntegerIs(owner["schemaVersion"],2))&&Text(owner,"copyId")==id&&
            Text(owner,"scope")=="saved_disk_bytes_only"&&ArtifactProof.Boolean(owner["executionEnabled"],false));
        var work=owner["working"]!.AsObject();var checkpoint=owner["checkpoint"]!.AsObject();var original=owner["source"]!.AsObject();
        var workPath=FileArtifacts.FixedPath(root,"workingcopies",id,ArtifactProof.IntegerIs(owner["schemaVersion"],1)?"Working.psmodel":"Working_"+id+".psmodel");
        var cpPath=FileArtifacts.FixedPath(root,"workingcopies",id,"checkpoint","Before.psmodel");
        Require(Same(Text(plan,"workingPath"),workPath)&&Same(Text(work,"path"),workPath)&&Same(Text(plan,"checkpointPath"),cpPath)&&Same(Text(checkpoint,"path"),cpPath)&&
            Same(Text(source["model"]!.AsObject(),"path"),workPath)&&!Same(Text(original,"path"),workPath)&&!Same(Text(original,"path"),cpPath),"dgk_target_owned_layout_differs");
        var cpSha=Text(checkpoint,"sha256");Require(FileArtifacts.Token(cpSha,64)&&Text(original,"sha256")==cpSha&&Text(work,"initialSha256")==cpSha&&Text(plan,"checkpointSha256")==cpSha);
        long Size(JsonObject value){var element=JsonSerializer.SerializeToElement(value["length"]);Require(element.ValueKind==JsonValueKind.Number&&element.TryGetInt64(out var size)&&size is >0 and <=1073741824);return element.GetInt64();}
        Require(Size(original)==Size(checkpoint)&&Size(work)==Size(checkpoint));
        Require((File.GetAttributes(cpPath)&FileAttributes.ReadOnly)!=0&&new FileInfo(cpPath).Length==Size(checkpoint)&&HashFile(cpPath)==cpSha&&HashFile(workPath)==Text(plan,"workingSha256"),"dgk_target_owned_bytes_differs");
        // Retained native identities/live context remain native responsibilities.
    }
    private static void DirectoryIdentity(JsonNode? value)
    {
        Require(value is JsonObject);var identity=value!.AsObject();
        Require(ArtifactProof.Fields(identity,"volumeSerial","fileIndex","creationTime")&&FileArtifacts.Token(Text(identity,"volumeSerial"),8)&&
            FileArtifacts.Token(Text(identity,"fileIndex"),16)&&FileArtifacts.Token(Text(identity,"creationTime"),16)&&Text(identity,"fileIndex")!=new string('0',16));
    }
    public JsonObject Receipt(JsonObject result,JsonObject request)
    {
        var id=Text(result,"targetId");var sha=Text(result,"targetSha256");Require(FileArtifacts.Token(id)&&FileArtifacts.Token(sha,64)&&Text(result,"bridgeRequestId")==id&&Text(result,"state")=="owned_dgk_target_prepared_files");
        foreach(var flag in new[]{"executionEnabled","exportAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","powerShapeConnectionAttempted","retryAllowed","exportSettingsQualified","nativeProviderQualified"})Require(ArtifactProof.Boolean(result[flag],false));
        var raw=FileArtifacts.ReadBytes(root,Path.Combine("dgkexports",id,"target.json"),65536);Require(FileArtifacts.Sha256(raw)==sha,"dgk_target_sha_differs");
        var plan=JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
        Require(ArtifactProof.Fields(plan,"bindingId","bindingSha256","entitySetId","entitySetSha256","parameters","schemaVersion","targetId","operation","format","selectionPolicy","parametersSha256","preparedUtc","expiresUtc","executionEnabled","exportSettingsQualified","nativeProviderQualified","copyId","ownershipSha256","inspectionRequestId","inspectionSha256","nativeObservedPropertiesSha256","windowContextSha256","entityKeys","workingPath","workingSha256","checkpointPath","checkpointSha256","bundleDirectory","targetPath","artifactManifest","directoryIdentity","bundleDirectoryIdentity","fenceObservation","qualification")&&
            ArtifactProof.IntegerIs(plan["schemaVersion"],1)&&Text(plan,"targetId")==id&&Text(plan,"operation")=="export_owned_dgk"&&Text(plan,"format")=="dgk"&&
            Text(plan,"selectionPolicy")=="explicit_target_set; no empty-selection fallback"&&JsonNode.DeepEquals(plan,result["target"]));
        foreach(var flag in new[]{"executionEnabled","exportSettingsQualified","nativeProviderQualified"})Require(ArtifactProof.Boolean(plan[flag],false));
        Require(request.Count==5&&request.All(pair=>JsonNode.DeepEquals(plan[pair.Key],pair.Value)),"dgk_target_request_differs");
        foreach(var name in new[]{"bindingId","entitySetId","copyId","inspectionRequestId"})Require(FileArtifacts.Token(Text(plan,name)));
        foreach(var name in new[]{"bindingSha256","entitySetSha256","ownershipSha256","inspectionSha256","nativeObservedPropertiesSha256","workingSha256","checkpointSha256","windowContextSha256","parametersSha256"})Require(FileArtifacts.Token(Text(plan,name),64));
        var parameters=DgkTargetDefinition.Read(plan["parameters"]!.AsObject());
        var digest=ObservationPolicy.Digest(parameters);
        Require(digest==Text(plan,"parametersSha256")&&JsonNode.DeepEquals(plan["artifactManifest"],DgkTargetDefinition.Manifest(id,parameters)),"dgk_target_manifest_parameters_differs");
        var folder=FileArtifacts.FixedPath(root,"dgkexports",id);var bundle=FileArtifacts.FixedPath(root,"dgkexports",id,"artifacts");
        Require(Same(Text(plan,"bundleDirectory"),bundle)&&Same(Text(plan,"targetPath"),Path.Combine(bundle,"Geometry_"+id+".dgk")),"dgk_target_fixed_bundle_differs");
        DirectoryIdentity(plan["directoryIdentity"]);DirectoryIdentity(plan["bundleDirectoryIdentity"]);
        Require(JsonNode.DeepEquals(plan["directoryIdentity"],WindowsDirectoryIdentity.Read(folder))&&
            JsonNode.DeepEquals(plan["bundleDirectoryIdentity"],WindowsDirectoryIdentity.Read(bundle)),"dgk_target_directory_identity_differs");
        var binding=ArtifactProof.Verified(root,"documentbindings",Text(plan,"bindingId"),Text(plan,"bindingSha256"),65536);
        bindings.VerifyReceipt(new(){["bindingId"]=plan["bindingId"]!.DeepClone(),["bindingSha256"]=plan["bindingSha256"]!.DeepClone(),["binding"]=binding.DeepClone()});
        foreach(var field in new[]{"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","windowContextSha256"})Require(JsonNode.DeepEquals(plan[field],binding[field]),"dgk_target_binding_dependency_differs");
        Require(JsonNode.DeepEquals(plan["nativeObservedPropertiesSha256"],binding["observedPropertiesSha256"]));
        var set=OperationPlanStore.ReadSet(root,new(){["entitySetId"]=plan["entitySetId"]!.DeepClone(),["entitySetSha256"]=plan["entitySetSha256"]!.DeepClone()},LeaseTime.PythonPrecision(utcNow()));
        var source=ArtifactProof.Verified(root,"responses",Text(plan,"inspectionRequestId"),Text(plan,"inspectionSha256"),16000000);
        Require(JsonNode.DeepEquals(OperationPlanStore.SourceObservations(source),OperationPlanStore.SourceObservations(set.Source))&&JsonNode.DeepEquals(plan["entityKeys"],set.Plan["entityKeys"]));
        var expiry=LeaseTime.Parse(Text(plan,"expiresUtc"),Code);Require(expiry==new[]{set.Expiry,LeaseTime.Parse(Text(binding,"expiresUtc"),Code)}.Min());
        _=LeaseTime.Remaining(Text(plan,"preparedUtc"),utcNow(),Code);Require(expiry>LeaseTime.PythonPrecision(utcNow()),"dgk_target_expired");
        foreach(var field in new[]{"contextBefore","contextAfter"})Require(JsonNode.DeepEquals(source[field],JsonNode.Parse("[0,0]")));
        foreach(var field in new[]{"selectionBefore","selectionAfter"})Require(source[field] is JsonArray selected&&selected.Count==0);
        Ownership(plan,source);
        var reservation=JsonNode.Parse(FileArtifacts.ReadJson(root,Path.Combine("dgkexports",id+".reservation.json"),65536).GetRawText())!.AsObject();
        Require(ArtifactProof.Fields(reservation,"schemaVersion","targetId","format","bindingId","bindingSha256","parametersSha256","automaticReplayForbidden")&&
            ArtifactProof.IntegerIs(reservation["schemaVersion"],1)&&Text(reservation,"targetId")==id&&Text(reservation,"format")=="dgk"&&ArtifactProof.Boolean(reservation["automaticReplayForbidden"],true));
        foreach(var field in new[]{"bindingId","bindingSha256","parametersSha256"})Require(JsonNode.DeepEquals(reservation[field],plan[field]));
        var fence=plan["fenceObservation"]!.AsObject();var path=FileArtifacts.FixedPath(root,"workingcopies",Text(plan,"copyId"),"mutation-fence.json");
        if(ArtifactProof.Boolean(fence["present"],false))Require(ArtifactProof.Fields(fence,"present")&&!File.Exists(path)&&!Directory.Exists(path),"dgk_target_fence_changed");
        else
        {
            Require(ArtifactProof.Fields(fence,"present","operationId","planSha256","fenceSha256","journalState","summarySha256")&&ArtifactProof.Boolean(fence["present"],true)&&Text(fence,"journalState")=="executed_unverified");
            Require(FileArtifacts.Sha256(FileArtifacts.ReadBytes(root,Path.GetRelativePath(root,path),65536))==Text(fence,"fenceSha256"),"dgk_target_fence_changed");
            var operation=Text(fence,"operationId");var journal=ArtifactProof.Journal(root,operation);
            Require(journal["state"]?.GetValue<string>()=="executed_unverified"&&Text(journal,"planSha256")==Text(fence,"planSha256"));
            var events=journal["events"]!.AsArray();var returned=events.Single(value=>value!["state"]!.GetValue<string>()=="command_returned")!.AsObject();
            var execution=ArtifactProof.Verified(root,"rotationexecutions",operation,Text(fence,"planSha256"),16000000);
            Require(ArtifactProof.IntegerIs(execution["schemaVersion"],2)&&Text(execution,"operationId")==operation&&Text(execution,"operation")=="rotate_owned_entities"&&
                Text(execution,"copyId")==Text(plan,"copyId")&&Text(execution,"ownershipSha256")==Text(plan,"ownershipSha256"));
            var summary=JsonNode.Parse(FileArtifacts.ReadJson(root,Path.Combine("rotationcommands",operation,"summary.json"),16000000).GetRawText())!.AsObject();
            Require(FileArtifacts.Sha256(FileArtifacts.ReadBytes(root,Path.Combine("rotationcommands",operation,"summary.json"),16000000))==Text(fence,"summarySha256")&&
                JsonNode.DeepEquals(events.Last()!["details"]!["summarySha256"],fence["summarySha256"])&&Text(summary,"operationId")==operation&&
                Text(summary,"planSha256")==Text(fence,"planSha256")&&ArtifactProof.Boolean(summary["geometryVerified"],false));
            var rotated=execution["targets"]!.AsArray();var commands=summary["commands"]!.AsArray();Require(rotated.Count is >0 and <=128&&commands.Count==rotated.Count&&
                ArtifactProof.IntegerIs(returned["details"]!["commandCalls"],rotated.Count)&&ArtifactProof.Boolean(returned["details"]!["allTargetsReturnedSuccess"],true)&&
                LeaseTime.Parse(Text(source,"timestampUtc"),Code)>LeaseTime.Parse(Text(returned,"timestampUtc"),Code));
            var rotationClaim=JsonNode.Parse(FileArtifacts.ReadJson(root,Path.Combine("rotationplans",Text(execution,"rotationPlanId")+".execution-once.json"),65536).GetRawText())!.AsObject();
            Require(Text(rotationClaim,"operationId")==operation&&Text(rotationClaim,"planSha256")==Text(fence,"planSha256")&&Text(rotationClaim,"copyId")==Text(plan,"copyId")&&ArtifactProof.Boolean(rotationClaim["automaticReplayForbidden"],true));
            for(var index=0;index<commands.Count;index++)
            {
                var command=commands[index]!.AsObject();Require(ArtifactProof.IntegerIs(command["index"],index)&&ArtifactProof.IntegerIs(command["returnCode"],1)&&JsonNode.DeepEquals(command["entityKey"],rotated[index]!["entityKey"]));
                var prefix=Path.Combine("rotationcommands",operation,index.ToString("D6",System.Globalization.CultureInfo.InvariantCulture));
                JsonObject Step(string suffix,string digest){var bytes=FileArtifacts.ReadBytes(root,prefix+suffix,65536);Require(FileArtifacts.Sha256(bytes)==digest);return JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();}
                var started=Step(".started.json",Text(command,"startedSha256"));var completed=Step(".returned.json",Text(command,"returnedSha256"));
                Require(Text(started,"operationId")==operation&&Text(completed,"operationId")==operation&&Text(started,"planSha256")==Text(fence,"planSha256")&&Text(completed,"planSha256")==Text(fence,"planSha256")&&
                    ArtifactProof.IntegerIs(started["index"],index)&&ArtifactProof.IntegerIs(completed["index"],index)&&JsonNode.DeepEquals(started["entityKey"],rotated[index]!["entityKey"])&&
                    JsonNode.DeepEquals(started["compositeId"],rotated[index]!["compositeId"])&&Text(completed,"startedSha256")==Text(command,"startedSha256")&&ArtifactProof.IntegerIs(completed["returnCode"],1));
            }
        }
        Require(Directory.Exists(bundle)&&!Directory.EnumerateFileSystemEntries(bundle).Any(),"dgk_target_output_collision");
        foreach(var pathToCheck in new[]{Path.Combine(folder,"export-once.json"),FileArtifacts.FixedPath(root,"workingcopies",Text(plan,"copyId"),"geometry-export-fence.json")})Require(!File.Exists(pathToCheck)&&!Directory.Exists(pathToCheck),"dgk_target_claim_or_export_fence");
        return plan;
    }
    public JsonObject Issue(JsonObject result,JsonObject request,string setHandle,string bindingHandle)
    {
        Require(JsonNode.DeepEquals(PrepareParameters(setHandle,bindingHandle,request["parameters"]!.AsObject()),request),"dgk_target_dependency_lost");
        var plan=Receipt(result,request);var time=LeaseTime.Remaining(Text(plan,"preparedUtc"),utcNow(),Code);
        var seconds=new[]{time.Remaining,(LeaseTime.Parse(Text(plan,"expiresUtc"),Code)-LeaseTime.PythonPrecision(utcNow())).TotalSeconds,candidates.SetRemaining(setHandle),bindings.Remaining(bindingHandle)}.Min();
        Require(seconds>0,"dgk_target_expired");foreach(var item in targets.Where(pair=>pair.Value.Expires<=now()).Select(pair=>pair.Key).ToArray())targets.Remove(item);
        if(targets.Count>=128)Clear();var handle=Guid.NewGuid().ToString("N");
        targets[handle]=new(setHandle,bindingHandle,request.DeepClone().AsObject(),new(){["targetId"]=result["targetId"]!.DeepClone(),["targetSha256"]=result["targetSha256"]!.DeepClone()},now()+seconds);
        var issued=result.DeepClone().AsObject();issued["ownedDgkTargetHandle"]=handle;issued["dgkTargetLifetimeSeconds"]=(int)seconds;return issued;
    }
    public JsonObject ValidationParameters(string handle)
    {
        Require(targets.TryGetValue(handle,out var lease)&&lease.Expires>now(),"dgk_target_handle_unknown_or_expired");
        Require(JsonNode.DeepEquals(PrepareParameters(lease!.Set,lease.Binding,lease.Source["parameters"]!.AsObject()),lease.Source),"dgk_target_dependency_changed");
        var id=Text(lease.Reference,"targetId");var plan=JsonNode.Parse(FileArtifacts.ReadJson(root,Path.Combine("dgkexports",id,"target.json"),65536).GetRawText())!.AsObject();
        var result=new JsonObject{["targetId"]=id,["targetSha256"]=lease.Reference["targetSha256"]!.DeepClone(),["bridgeRequestId"]=id,["state"]="owned_dgk_target_prepared_files",["target"]=plan};
        foreach(var flag in new[]{"executionEnabled","exportAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","powerShapeConnectionAttempted","retryAllowed","exportSettingsQualified","nativeProviderQualified"})result[flag]=false;
        Receipt(result,lease.Source);return lease.Reference.DeepClone().AsObject();
    }
}
