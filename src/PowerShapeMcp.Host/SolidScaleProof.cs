using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal static class SolidScaleProof
{
    internal const string Scope="one_owned_solid_world_uniform_scale_mm_sampled_membership_v1";
    static string ScopeFor(bool surface,bool curve=false)=>curve?CurveScaleWitnessProof.Scope:surface?SurfaceScaleWitnessProof.Scope:Scope;
    static string Folder(bool surface,string name,bool curve=false)=>curve?name.Replace("solidscale","curvescale",StringComparison.Ordinal):surface?name.Replace("solidscale","surfacescale",StringComparison.Ordinal):name;
    static void Need(bool ok){if(!ok)throw new ReferenceRejected("solid_scale_proof_differs");}
    static string Text(JsonObject o,string k)=>ArtifactProof.String(o,k,"solid_scale_proof_invalid");
    internal static (JsonObject Value,string Sha) Raw(string root,string id,string? sha=null)
    {
        Need(FileArtifacts.Token(id));var bytes=FileArtifacts.ReadBytes(root,Path.Combine("responses",id+".json"),16000000);var digest=FileArtifacts.Sha256(bytes);Need(sha is null||FileArtifacts.Token(sha,64)&&digest==sha);var value=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();
        if(value.ContainsKey("bridgeRequestId"))Need(Text(value,"bridgeRequestId")==id);else value["bridgeRequestId"]=id;return(value,digest);
    }
    internal static JsonObject Preparation(string root,JsonObject reference,bool surface=false,bool curve=false)
    {
        var id=Text(reference,"ownedScaleId");var raw=Raw(root,id);var receipt=raw.Value;
        Need(Text(receipt,"state")=="owned_uniform_scale_prepared_observed"&&Text(receipt,"qualificationScope")==ScopeFor(surface,curve)&&JsonNode.DeepEquals(receipt["ownedScaleSha256"],reference["ownedScaleSha256"]));
        var packet=ArtifactProof.Verified(root,"ownedscales",id,Text(reference,"ownedScaleSha256"),65536);Need(JsonNode.DeepEquals(packet,receipt["ownedScale"])&&Text(packet,"ownedScaleId")==id&&ArtifactProof.Boolean(packet["executionEnabled"],false)&&ArtifactProof.Boolean(packet["nativeProviderQualified"],false));
        var definition=UniformScaleDefinition.Read(packet["parameters"]!.AsObject());Need(definition.Factor>=0.01&&definition.Factor<=100);
        var bundle=ArtifactProof.Verified(root,Folder(surface,"solidscalebundles",curve),id,Text(receipt,"membershipBundleSha256"),16000000);
        Need(ArtifactProof.Fields(bundle,"schemaVersion","qualificationScope","ownedScaleId","ownedScaleSha256","bindingId","bindingSha256","copyId","ownershipSha256","workingSha256","windowContextSha256","measurement")&&ArtifactProof.IntegerIs(bundle["schemaVersion"],1)&&Text(bundle,"qualificationScope")==ScopeFor(surface,curve)&&JsonNode.DeepEquals(bundle["ownedScaleSha256"],reference["ownedScaleSha256"]));
        foreach(var k in new[]{"ownedScaleId","bindingId","bindingSha256","copyId","ownershipSha256","workingSha256","windowContextSha256"})Need(JsonNode.DeepEquals(bundle[k],packet[k]));
        var measurement=bundle["measurement"]!.AsObject();var source=ArtifactProof.Verified(root,"responses",Text(packet,"bindingInspectionRequestId"),Text(packet,"bindingInspectionSha256"),16000000);
        if(curve)CurveScaleWitnessProof.Source(source,measurement,definition,packet);else if(surface)SurfaceScaleWitnessProof.Source(source,measurement,definition,packet);else{
Need(ArtifactProof.Fields(measurement,"schemaVersion","profile","entityKey","sourceCompositeId","sourceObservationSha256","windowContextSha256","sourceBoundsWorldMmPaired","sourceWitnesses","sourceWitnessSha256","qualification")&&ArtifactProof.IntegerIs(measurement["schemaVersion"],1)&&Text(measurement,"profile")==PowerShapeGeometry.SolidScaleMembershipVerifier.Profile&&JsonNode.DeepEquals(measurement["windowContextSha256"],packet["windowContextSha256"])&&JsonNode.DeepEquals(measurement["sourceObservationSha256"],packet["nativeObservedPropertiesSha256"]));
        Need(packet["entityKeys"] is JsonArray keys&&keys.Count==1&&JsonNode.DeepEquals(keys[0],measurement["entityKey"])&&ArtifactProof.IntegerIs(measurement["entityKey"]![0],40)&&measurement["sourceCompositeId"]!.GetValue<int>()>0&&measurement["sourceWitnesses"] is JsonArray rows&&rows.Count==8&&measurement["sourceBoundsWorldMmPaired"] is JsonArray bounds&&bounds.Count==6&&FileArtifacts.Token(Text(measurement,"sourceWitnessSha256"),64));
        ValidateSource(source,measurement,definition);
        }
        return new(){["ownedScaleId"]=id,["ownedScaleSha256"]=reference["ownedScaleSha256"]!.DeepClone(),["preparationReceiptSha256"]=raw.Sha,["membershipBundleSha256"]=receipt["membershipBundleSha256"]!.DeepClone()};
    }
    static void ValidateSource(JsonObject source,JsonObject measurement,UniformScaleDefinition definition)
    {
        Need(ArtifactProof.IntegerIs(source["entityCount"],1)&&source["entities"] is JsonArray entities&&entities.Count==1&&ArtifactProof.Boolean(source["busy"],false)&&source["state"]?.GetValue<string>()=="succeeded_verified");
        foreach(var k in new[]{"contextBefore","contextAfter"})Need(JsonNode.DeepEquals(source[k],new JsonArray(0,0)));foreach(var k in new[]{"selectionBefore","selectionAfter"})Need(source[k] is JsonArray a&&a.Count==0);
        var body=source["entities"]![0]!.AsObject();var g=body["geometry"]!.AsObject();Need(ArtifactProof.IntegerIs(body["nativeTypeCode"],40)&&JsonNode.DeepEquals(body["nativeId"],measurement["entityKey"]![1])&&Text(g,"coordinateFrame")=="world");
        double[] Vector(JsonNode? n)=>n!.AsArray().Select(v=>v!.GetValue<double>()).ToArray();
        var witnesses=measurement["sourceWitnesses"]!.AsArray().Select((n,i)=>{var row=n!.AsObject();Need(ArtifactProof.Fields(row,"key","faceObserved","positionWorldMm")&&Text(row,"key")=="corner-"+i);return new PowerShapeGeometry.SolidScaleMembershipVerifier.Witness(Text(row,"key"),Text(row,"faceObserved"),Vector(row["positionWorldMm"]));}).ToArray();
        var invariants=new[]{"surfaces","nlinks_open","closedRaw","connectedRaw","watertightRaw","trimming_validRaw"}.Select(k=>g[k]!.GetValue<int>()).ToArray();var digest=Text(measurement,"sourceObservationSha256");
        var reading=new PowerShapeGeometry.SolidScaleMembershipVerifier.Reading(digest,digest,Text(measurement,"sourceWitnessSha256"),40,body["nativeId"]!.GetValue<int>(),"world",Text(source,"lengthUnitsRaw"),source["lengthUnitFactorRaw"]!.GetValue<double>(),source["generalToleranceRaw"]!.GetValue<double>(),ObservationPolicy.Digest(new JsonArray(source["areaUnitNameRaw"]?.DeepClone(),source["areaUnitFactorRaw"]?.DeepClone())),ObservationPolicy.Digest(new JsonArray(source["volumeUnitNameRaw"]?.DeepClone(),source["volumeUnitFactorRaw"]?.DeepClone())),g["areaRaw"]!.GetValue<double>(),g["volumeRaw"]!.GetValue<double>(),Vector(measurement["sourceBoundsWorldMmPaired"]),Vector(g["cogRaw"]),invariants,witnesses);
        try{PowerShapeGeometry.SolidScaleMembershipVerifier.Validate(reading);}catch(ArgumentException){throw new ReferenceRejected("solid_scale_source_measurements_invalid");}
        var centre=definition.Centre.ToArray();Need(centre.All(v=>Math.Abs(v)<=1000000)&&Enumerable.Range(0,6).All(i=>Math.Abs(centre[i/2]+definition.Factor*(reading.Bounds[i]-centre[i/2]))<=1000000)&&reading.Witnesses.Max(w=>Enumerable.Range(0,3).Max(i=>Math.Abs((definition.Factor-1)*(w.Position[i]-centre[i]))))>2*reading.DocumentToleranceMm);
    }
    internal static JsonObject Execution(string root,string id,string sha,bool terminal=false,bool surface=false,bool curve=false)
    {
        var receipt=Raw(root,id,sha).Value;Need(Text(receipt,"state")== (curve?"owned_curve_scale_executed_unverified":surface?"owned_surface_scale_executed_unverified":"owned_solid_scale_executed_unverified")&&Text(receipt,"operationId")==id&&Text(receipt,"qualificationScope")==ScopeFor(surface,curve)&&ArtifactProof.IntegerIs(receipt["commandCallsAttempted"],13)&&ArtifactProof.IntegerIs(receipt["returnCode"],1)&&ArtifactProof.Boolean(receipt["geometryMutationAttempted"],true)&&ArtifactProof.Boolean(receipt["contextChangeAttempted"],true)&&ArtifactProof.Boolean(receipt["cadSaveAttempted"],false)&&ArtifactProof.Boolean(receipt["geometryVerified"],false));
        var plan=ArtifactProof.Verified(root,Folder(surface,"solidscaleexecutions",curve),id,Text(receipt,"planSha256"),16000000);Need(JsonNode.DeepEquals(plan,receipt["plan"])&&ArtifactProof.IntegerIs(plan["schemaVersion"],1)&&Text(plan,"operationId")==id&&Text(plan,"operation")== (curve?"scale_one_owned_curve":surface?"scale_one_owned_surface":"scale_one_owned_solid")&&Text(plan,"qualificationScope")==ScopeFor(surface,curve)&&plan["commands"] is JsonArray commands&&commands.Count==13);
        var refs=plan["references"]!.AsObject();Need(JsonNode.DeepEquals(refs,Preparation(root,refs,surface,curve)));var packet=ArtifactProof.Verified(root,"ownedscales",Text(refs,"ownedScaleId"),Text(refs,"ownedScaleSha256"),65536);
        foreach(var k in new[]{"copyId","ownershipSha256","workingSha256","windowContextSha256","parameters"})Need(JsonNode.DeepEquals(plan[k],packet[k]));Need(JsonNode.DeepEquals(plan["entityKey"],packet["entityKeys"]![0]));
        if(curve)CurveScaleWitnessProof.Phases(root,id,receipt,plan,packet);else if(surface)SurfaceScaleWitnessProof.Phases(root,id,receipt,plan,packet);
        var journal=ArtifactProof.Journal(root,id);Need(Text(journal,"state")== (terminal?"succeeded_verified":"executed_unverified")&&Text(journal,"planSha256")==Text(receipt,"planSha256")&&ArtifactProof.Boolean(journal["writerLockPresent"],false));return receipt;
    }
    internal static string CheckedExecution(string root,JsonObject response,JsonObject args,bool surface=false,bool curve=false)
    {
        var id=Text(response,"bridgeRequestId");var raw=Raw(root,id);Need(JsonNode.DeepEquals(raw.Value,response));var receipt=Execution(root,id,raw.Sha,false,surface,curve);Need(JsonNode.DeepEquals(receipt["plan"]!["references"],args));return raw.Sha;
    }
    internal static void CheckedVerification(string root,JsonObject response,JsonObject args,bool surface=false,bool curve=false)
    {
        var raw=Raw(root,Text(response,"bridgeRequestId"));Need(JsonNode.DeepEquals(raw.Value,response));var original=Execution(root,Text(args,"executionRequestId"),Text(args,"executionReceiptSha256"),true,surface,curve);var plan=original["plan"]!.AsObject();
        Need(Text(response,"state")== (curve?"owned_curve_scale_verified_observed":surface?"owned_surface_scale_verified_observed":"owned_solid_scale_verified_observed")&&Text(response,"qualificationScope")==ScopeFor(surface,curve)&&Text(response,"executionRequestId")==Text(args,"executionRequestId")&&Text(response,"verificationId")==Text(response,"bridgeRequestId")&&ArtifactProof.Boolean(response["samplePostconditionsPassed"],true)&&ArtifactProof.Boolean(response["copyFenceReleased"],true)&&Text(response,"recordedJournalState")=="succeeded_verified");
        foreach(var k in new[]{"geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","retryAllowed","fullTopologyEquivalent"})Need(ArtifactProof.Boolean(response[k],false));foreach(var k in new[]{"copyId","ownershipSha256"})Need(JsonNode.DeepEquals(response[k],plan[k]));
        var diagnostic=ArtifactProof.Verified(root,Folder(surface,"solidscaleverifications",curve),Text(response,"verificationId"),Text(response,"diagnosticSha256"),16000000);foreach(var k in new[]{"executionRequestId","executionReceiptSha256","inspectionRequestId","inspectionSha256","listingRequestId","listingSha256"})Need(JsonNode.DeepEquals(diagnostic[k],args[k]));Need(Text(diagnostic,"profile")== (curve?CurveScaleWitnessProof.Profile:surface?SurfaceScaleWitnessProof.Profile:PowerShapeGeometry.SolidScaleMembershipVerifier.Profile)&&Text(diagnostic,"qualificationScope")==ScopeFor(surface,curve)&&ArtifactProof.Boolean(diagnostic["fullTopologyEquivalent"],false));
        if(surface||curve){var refs=plan["references"]!.AsObject();var packet=ArtifactProof.Verified(root,"ownedscales",Text(refs,"ownedScaleId"),Text(refs,"ownedScaleSha256"),65536);var bundle=ArtifactProof.Verified(root,Folder(surface,"solidscalebundles",curve),Text(refs,"ownedScaleId"),Text(refs,"membershipBundleSha256"),16000000);var source=ArtifactProof.Verified(root,"responses",Text(packet,"bindingInspectionRequestId"),Text(packet,"bindingInspectionSha256"),16000000);var after=ArtifactProof.Verified(root,"responses",Text(args,"inspectionRequestId"),Text(args,"inspectionSha256"),16000000);if(curve)CurveScaleWitnessProof.Diagnostic(source,after,bundle["measurement"]!.AsObject(),UniformScaleDefinition.Read(packet["parameters"]!.AsObject()),diagnostic);else SurfaceScaleWitnessProof.Diagnostic(source,after,bundle["measurement"]!.AsObject(),UniformScaleDefinition.Read(packet["parameters"]!.AsObject()),diagnostic);}
        var last=ArtifactProof.Journal(root,Text(args,"executionRequestId"))["events"]!.AsArray().Last()!["details"]!;Need(JsonNode.DeepEquals(last["diagnosticSha256"],response["diagnosticSha256"])&&!File.Exists(FileArtifacts.FixedPath(root,"workingcopies",Text(plan,"copyId"),"mutation-fence.json")));
    }
}
