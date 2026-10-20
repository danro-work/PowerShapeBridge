using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal static class SolidQuarterTurnProof
{
    internal const string Scope="one_solid_world_z_plus_90_mm_observed_samples";
    static void Need(bool ok){if(!ok)throw new ReferenceRejected("solid_quarter_turn_proof_differs");}
    static string Text(JsonObject o,string key)=>ArtifactProof.String(o,key,"solid_quarter_turn_proof_invalid");
    internal static (JsonObject Value,string Sha) Raw(string root,string id,string? sha=null)
    {
        Need(FileArtifacts.Token(id));var bytes=FileArtifacts.ReadBytes(root,Path.Combine("responses",id+".json"),16000000);var digest=FileArtifacts.Sha256(bytes);
        Need(sha is null||FileArtifacts.Token(sha,64)&&sha==digest);var value=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();
        if(value.ContainsKey("bridgeRequestId"))Need(Text(value,"bridgeRequestId")==id);else value["bridgeRequestId"]=id;
        return(value,digest);
    }
    internal static JsonObject Preparation(string root,JsonObject args)
    {
        var id=Text(args,"ownedRotationId");var (receipt,sha)=Raw(root,id);
        Need(receipt["state"]?.GetValue<string>()=="owned_rotation_prepared_observed"&&receipt["qualificationScope"]?.GetValue<string>()==Scope&&JsonNode.DeepEquals(receipt["ownedRotationSha256"],args["ownedRotationSha256"]));
        var packet=ArtifactProof.Verified(root,"ownedrotations",id,Text(args,"ownedRotationSha256"),65536);Need(JsonNode.DeepEquals(packet,receipt["ownedRotation"]));
        var bundle=ArtifactProof.Verified(root,"solidrotationdescriptors",id,Text(receipt,"solidWitnessBundleSha256"),16000000);
        Need(JsonNode.DeepEquals(bundle["ownedRotationId"],args["ownedRotationId"])&&JsonNode.DeepEquals(bundle["ownedRotationSha256"],args["ownedRotationSha256"]));
        foreach(var key in new[]{"bindingId","bindingSha256","copyId","ownershipSha256","windowContextSha256"})Need(JsonNode.DeepEquals(bundle[key],packet[key]));
        return new(){["ownedRotationId"]=id,["ownedRotationSha256"]=args["ownedRotationSha256"]!.DeepClone(),["preparationReceiptSha256"]=sha,["solidWitnessBundleSha256"]=receipt["solidWitnessBundleSha256"]!.DeepClone()};
    }
    internal static JsonObject Execution(string root,string id,string sha,bool terminal=false)
    {
        var (receipt,_)=Raw(root,id,sha);Need(receipt["state"]?.GetValue<string>()=="owned_rotation_executed_unverified"&&Text(receipt,"operationId")==id&&receipt["qualificationScope"]?.GetValue<string>()==Scope&&ArtifactProof.Boolean(receipt["geometryMutationAttempted"],true)&&ArtifactProof.Boolean(receipt["contextChangeAttempted"],false)&&ArtifactProof.Boolean(receipt["cadSaveAttempted"],false)&&ArtifactProof.Boolean(receipt["geometryVerified"],false)&&ArtifactProof.IntegerIs(receipt["returnCode"],1)&&ArtifactProof.IntegerIs(receipt["commandCallsAttempted"],1));
        var plan=ArtifactProof.Verified(root,"rotationexecutions",id,Text(receipt,"planSha256"),16000000);
        Need(JsonNode.DeepEquals(plan,receipt["plan"])&&Text(plan,"operationId")==id&&Text(plan,"operation")=="rotate_owned_entities"&&ArtifactProof.IntegerIs(plan["schemaVersion"],2)&&ArtifactProof.IntegerIs(plan["angleDegrees"],90)&&JsonNode.DeepEquals(plan["nativeAxis"],JsonNode.Parse("[0,0,0,0,0,1]"))&&plan["targets"] is JsonArray targets&&targets.Count==1);
        var prepared=Raw(root,Text(plan,"ownedRotationId"),Text(receipt,"preparationReceiptSha256")).Value;
        Need(JsonNode.DeepEquals(prepared["ownedRotationSha256"],plan["ownedRotationSha256"])&&JsonNode.DeepEquals(prepared["solidWitnessBundleSha256"],receipt["solidWitnessBundleSha256"]));
        var journal=ArtifactProof.Journal(root,id);Need(Text(journal,"state")== (terminal?"succeeded_verified":"executed_unverified")&&ArtifactProof.Boolean(journal["writerLockPresent"],false)&&Text(journal,"planSha256")==Text(receipt,"planSha256"));
        return receipt;
    }
    internal static string CheckedExecution(string root,JsonObject response,JsonObject args)
    {
        var id=Text(response,"bridgeRequestId");var raw=Raw(root,id);Need(JsonNode.DeepEquals(raw.Value,response));
        var receipt=Execution(root,id,raw.Sha);var plan=receipt["plan"]!.AsObject();
        foreach(var key in new[]{"ownedRotationId","ownedRotationSha256"})Need(JsonNode.DeepEquals(plan[key],args[key]));
        foreach(var key in new[]{"preparationReceiptSha256","solidWitnessBundleSha256"})Need(JsonNode.DeepEquals(receipt[key],args[key]));
        return raw.Sha;
    }
    internal static void CheckedVerification(string root,JsonObject result,JsonObject args)
    {
        var id=Text(args,"executionRequestId");var original=Execution(root,id,Text(args,"executionReceiptSha256"),true);var plan=original["plan"]!.AsObject();
        Need(result["state"]?.GetValue<string>()=="owned_solid_world_z_quarter_turn_verified_observed"&&result["qualificationScope"]?.GetValue<string>()==Scope&&Text(result,"executionRequestId")==id&&Text(result,"recordedJournalState")=="succeeded_verified"&&ArtifactProof.Boolean(result["copyFenceReleased"],true)&&ArtifactProof.Boolean(result["nativeConventionQualified"],true)&&ArtifactProof.Boolean(result["samplePostconditionsPassed"],true));
        foreach(var key in new[]{"geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","retryAllowed"})Need(ArtifactProof.Boolean(result[key],false));
        foreach(var key in new[]{"copyId","ownershipSha256"})Need(JsonNode.DeepEquals(result[key],plan[key]));
        var verificationId=Text(result,"verificationId");Need(verificationId==Text(result,"bridgeRequestId"));
        var raw=Raw(root,verificationId);Need(JsonNode.DeepEquals(raw.Value,result));
        var diagnostic=ArtifactProof.Verified(root,"rotationverifications",verificationId,Text(result,"diagnosticSha256"),16000000);
        foreach(var key in new[]{"executionRequestId","executionReceiptSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256"})Need(JsonNode.DeepEquals(diagnostic[key],args[key]));
        var journal=ArtifactProof.Journal(root,id);var last=journal["events"]!.AsArray().Last()!["details"]!.AsObject();Need(JsonNode.DeepEquals(last["diagnosticSha256"],result["diagnosticSha256"]));
        Need(!File.Exists(FileArtifacts.FixedPath(root,Path.Combine("workingcopies",Text(plan,"copyId"),"mutation-fence.json"))));
    }
}
