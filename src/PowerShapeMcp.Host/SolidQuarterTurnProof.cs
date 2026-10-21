using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal static class SolidQuarterTurnProof
{
    internal const string Scope="one_solid_world_z_plus_90_mm_observed_samples";
    internal const string PivotedScope="one_solid_world_z_signed_quarter_turn_mm_observed_samples";
    internal const string PrincipalScope="one_solid_world_principal_signed_quarter_turn_mm_observed_samples";
    internal const string ObliqueScope="one_solid_world_oblique_signed_quarter_turn_mm_observed_samples";
    internal static string ScopeFor(JsonObject parameters)
    {
        var definition=RotationDefinition.Read(parameters);
        Need(definition.AngleDegrees==90||definition.AngleDegrees==-90);
        if(definition.UnitDirection.Count(v=>Math.Abs(v)==1)!=1||definition.UnitDirection.Count(v=>v==0)!=2)return ObliqueScope;
        if(!definition.UnitDirection.SequenceEqual(new double[]{0,0,1}))return PrincipalScope;
        return definition.AngleDegrees==90&&definition.Origin.All(v=>v==0)?Scope:PivotedScope;
    }
    internal static string VerificationStateFor(string scope)
    {
        Need(scope==Scope||scope==PivotedScope||scope==PrincipalScope||scope==ObliqueScope);
        return scope==ObliqueScope?"owned_solid_oblique_quarter_turn_verified_observed":scope==PrincipalScope?"owned_solid_principal_quarter_turn_verified_observed":"owned_solid_world_z_quarter_turn_verified_observed";
    }
    internal static string ExecutionScope(JsonObject plan)
    {
        Need(plan["nativeAxis"] is JsonArray a&&a.Count==6);var axis=plan["nativeAxis"]!.AsArray();
        var parameters=new JsonObject{["angleDegrees"]=plan["angleDegrees"]!.DeepClone(),["origin"]=new JsonArray(axis.Take(3).Select(v=>v!.DeepClone()).ToArray()),["direction"]=new JsonArray(axis.Skip(3).Select(v=>v!.DeepClone()).ToArray()),["coordinateFrame"]="world",["lengthUnits"]="document_raw"};
        var definition=RotationDefinition.Read(parameters);var actual=axis.Select(v=>v!.GetValue<double>()).ToArray();
        // Unit length is numeric, not textual/canonical JSON equality. Normalizing
        // an already unit oblique vector can change its last floating-point bit.
        Need(actual.All(double.IsFinite)&&Math.Abs(actual.Skip(3).Sum(v=>v*v)-1)<=1e-12);
        return ScopeFor(parameters);
    }
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
        Need(receipt["state"]?.GetValue<string>()=="owned_rotation_prepared_observed"&&JsonNode.DeepEquals(receipt["ownedRotationSha256"],args["ownedRotationSha256"]));
        var packet=ArtifactProof.Verified(root,"ownedrotations",id,Text(args,"ownedRotationSha256"),65536);Need(JsonNode.DeepEquals(packet,receipt["ownedRotation"]));
        var plan=ArtifactProof.Verified(root,"rotationplans",Text(packet,"rotationPlanId"),Text(packet,"rotationPlanSha256"),65536);
        Need(receipt["qualificationScope"]?.GetValue<string>()==ScopeFor(plan["parameters"]!.AsObject()));
        var bundle=ArtifactProof.Verified(root,"solidrotationdescriptors",id,Text(receipt,"solidWitnessBundleSha256"),16000000);
        Need(JsonNode.DeepEquals(bundle["ownedRotationId"],args["ownedRotationId"])&&JsonNode.DeepEquals(bundle["ownedRotationSha256"],args["ownedRotationSha256"]));
        foreach(var key in new[]{"bindingId","bindingSha256","copyId","ownershipSha256","windowContextSha256"})Need(JsonNode.DeepEquals(bundle[key],packet[key]));
        return new(){["ownedRotationId"]=id,["ownedRotationSha256"]=args["ownedRotationSha256"]!.DeepClone(),["preparationReceiptSha256"]=sha,["solidWitnessBundleSha256"]=receipt["solidWitnessBundleSha256"]!.DeepClone()};
    }
    internal static JsonObject Execution(string root,string id,string sha,bool terminal=false)
    {
        var (receipt,_)=Raw(root,id,sha);Need(receipt["state"]?.GetValue<string>()=="owned_rotation_executed_unverified"&&Text(receipt,"operationId")==id&&ArtifactProof.Boolean(receipt["geometryMutationAttempted"],true)&&ArtifactProof.Boolean(receipt["contextChangeAttempted"],false)&&ArtifactProof.Boolean(receipt["cadSaveAttempted"],false)&&ArtifactProof.Boolean(receipt["geometryVerified"],false)&&ArtifactProof.IntegerIs(receipt["returnCode"],1)&&ArtifactProof.IntegerIs(receipt["commandCallsAttempted"],1));
        var plan=ArtifactProof.Verified(root,"rotationexecutions",id,Text(receipt,"planSha256"),16000000);
        Need(JsonNode.DeepEquals(plan,receipt["plan"])&&Text(plan,"operationId")==id&&Text(plan,"operation")=="rotate_owned_entities"&&ArtifactProof.IntegerIs(plan["schemaVersion"],2)&&plan["targets"] is JsonArray targets&&targets.Count==1&&receipt["qualificationScope"]?.GetValue<string>()==ExecutionScope(plan));
        var prepared=Raw(root,Text(plan,"ownedRotationId"),Text(receipt,"preparationReceiptSha256")).Value;
        Need(JsonNode.DeepEquals(prepared["ownedRotationSha256"],plan["ownedRotationSha256"])&&JsonNode.DeepEquals(prepared["solidWitnessBundleSha256"],receipt["solidWitnessBundleSha256"])&&JsonNode.DeepEquals(prepared["qualificationScope"],receipt["qualificationScope"]));
        var packet=ArtifactProof.Verified(root,"ownedrotations",Text(plan,"ownedRotationId"),Text(plan,"ownedRotationSha256"),65536);
        var originalPlan=ArtifactProof.Verified(root,"rotationplans",Text(packet,"rotationPlanId"),Text(packet,"rotationPlanSha256"),65536);
        Need(JsonNode.DeepEquals(plan["nativeAxis"],originalPlan["nativeAxis"])&&JsonNode.DeepEquals(plan["angleDegrees"],originalPlan["parameters"]!["angleDegrees"]));
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
        Need(result["state"]?.GetValue<string>()==VerificationStateFor(Text(original,"qualificationScope"))&&JsonNode.DeepEquals(result["qualificationScope"],original["qualificationScope"])&&Text(result,"executionRequestId")==id&&Text(result,"recordedJournalState")=="succeeded_verified"&&ArtifactProof.Boolean(result["copyFenceReleased"],true)&&ArtifactProof.Boolean(result["nativeConventionQualified"],true)&&ArtifactProof.Boolean(result["samplePostconditionsPassed"],true));
        foreach(var key in new[]{"geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","retryAllowed"})Need(ArtifactProof.Boolean(result[key],false));
        foreach(var key in new[]{"copyId","ownershipSha256"})Need(JsonNode.DeepEquals(result[key],plan[key]));
        var verificationId=Text(result,"verificationId");Need(verificationId==Text(result,"bridgeRequestId"));
        var raw=Raw(root,verificationId);Need(JsonNode.DeepEquals(raw.Value,result));
        var diagnostic=ArtifactProof.Verified(root,"rotationverifications",verificationId,Text(result,"diagnosticSha256"),16000000);
        if(Text(original,"qualificationScope")==ObliqueScope){
            var profile=JsonNode.Parse("""{"name":"solid_oblique_mass_properties_v1","facePositionAbsoluteMm":0.000001,"normalAbsolute":0.000001,"cogAbsoluteMm":0.00001,"volumeAbsoluteRaw":0.000001,"volumeRelative":0.000001,"otherRelative":0.000000001,"fullTopologyEquivalent":false}""");
            Need(JsonNode.DeepEquals(result["numericVerificationProfile"],profile)&&JsonNode.DeepEquals(diagnostic["numericVerificationProfile"],profile));
        }
        foreach(var key in new[]{"executionRequestId","executionReceiptSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256"})Need(JsonNode.DeepEquals(diagnostic[key],args[key]));
        var journal=ArtifactProof.Journal(root,id);var last=journal["events"]!.AsArray().Last()!["details"]!.AsObject();Need(JsonNode.DeepEquals(last["diagnosticSha256"],result["diagnosticSha256"]));
        Need(!File.Exists(FileArtifacts.FixedPath(root,Path.Combine("workingcopies",Text(plan,"copyId"),"mutation-fence.json"))));
    }
}
