using System.Text.Json.Nodes;
namespace PowerShapeMcp;

internal static class SurfaceSetTranslationProof
{
    internal const string NumericalProfile="owned_surface_set_grid_translation_numeric_normals_v1";
    internal static JsonObject NormalPolicy()=>new(){["schemaVersion"]=1,["targetNormalComponentToleranceRaw"]=1e-12,["unitNormalLengthToleranceRaw"]=1e-9,["targetPositionToleranceMm"]=1e-6,["nontargetSamplesExact"]=true};
    internal const string Profile="owned_surface_set_grid_translation_v1";
    private static void Need(bool value){if(!value)throw new ReferenceRejected("surface_set_translation_proof_invalid");}
    private static int I(JsonNode? value)=>SurfaceReadTools.Integer(value);
    private static double N(JsonNode? value)=>SurfaceReadTools.Number(value);
    private static string S(JsonObject value,string name)=>ArtifactProof.String(value,name,"surface_set_translation_proof_invalid");
    private static DateTimeOffset Time(JsonObject value)=>LeaseTime.Parse(S(value,"timestampUtc"),"surface_set_time_invalid");
    internal static JsonArray Keys(JsonObject plan)
    {
        var keys=plan["targetEntityKeys"]!.AsArray();Need(keys.Count is >=2 and <=8);
        var seen=new HashSet<int>();foreach(var key in keys){var k=key!.AsArray();Need(k.Count==2&&I(k[0])==21&&I(k[1])>0&&seen.Add(I(k[1])));}
        return keys;
    }
    internal static void Preparation(string root,JsonObject parameters)
    {
        var packet=ArtifactProof.Verified(root,"ownedoperations",S(parameters,"ownedOperationId"),S(parameters,"ownedOperationSha256"),65536);Need(S(packet,"ownedOperationId")==S(parameters,"ownedOperationId")&&ArtifactProof.Boolean(packet["executionEnabled"],false));
        var operation=ArtifactProof.Verified(root,"operationplans",S(packet,"operationPlanId"),S(packet,"operationPlanSha256"),65536);Need(S(operation,"operation")=="translate_entities"&&operation["entitySets"]is JsonArray sets&&sets.Count==1);_=Keys(new JsonObject{["targetEntityKeys"]=operation["entitySets"]![0]!["entityKeys"]!.DeepClone()});
    }
    internal static void Post(JsonObject before,JsonObject after,JsonObject original,JsonObject current,JsonArray vector,JsonArray targets,bool numerical=false,JsonObject? rotationPlan=null)
    {
        var parameters=original["parameters"]!.AsObject();OwnedSurfaceSelectionProof.Capture(original,parameters,before,0);OwnedSurfaceSelectionProof.Capture(current,parameters,after,0);
        foreach(var field in OperationPlanStore.ObservedFields)Need(JsonNode.DeepEquals(before[field],after[field]));
        if(rotationPlan is not null){SurfaceSetRotationProof.Post(original,current,targets,rotationPlan);return;}Need(vector.Count==3&&targets.Count is >=1 and <=8);var seen=new HashSet<int>();foreach(var key in targets){var k=key!.AsArray();Need(k.Count==2&&I(k[0])==21&&I(k[1])>0&&seen.Add(I(k[1])));}
        var normalized=current.DeepClone().AsObject();var oldRows=original["witnesses"]!.AsArray();var newRows=normalized["witnesses"]!.AsArray();Need(oldRows.Count==newRows.Count&&targets.All(k=>oldRows.Count(r=>JsonNode.DeepEquals(r!["entityKey"],k))==1));
        for(int row=0;row<oldRows.Count;row++)
        {
            if(!targets.Any(k=>JsonNode.DeepEquals(oldRows[row]!["entityKey"],k)))continue;
            Need(JsonNode.DeepEquals(oldRows[row]!["entityKey"],newRows[row]!["entityKey"]));var a=oldRows[row]!["samples"]!["positionsWorldRaw"]!.AsArray();var b=newRows[row]!["samples"]!["positionsWorldRaw"]!.AsArray();Need(a.Count==b.Count&&a.Count>=4);
            for(int p=0;p<a.Count;p++)for(int axis=0;axis<3;axis++)Need(Math.Abs(N(b[p]![axis])-(N(a[p]![axis])+N(vector[axis])))<=1e-6);
            if(numerical){var on=oldRows[row]!["samples"]!["normalsWorldRaw"]!.AsArray();var nn=newRows[row]!["samples"]!["normalsWorldRaw"]!.AsArray();Need(on.Count==nn.Count&&on.Count==a.Count);for(int i=0;i<on.Count;i++){Need(on[i]!.AsArray().Count==3&&nn[i]!.AsArray().Count==3);Need(Math.Abs(Math.Sqrt(on[i]!.AsArray().Sum(v=>N(v)*N(v)))-1)<=1e-9&&Math.Abs(Math.Sqrt(nn[i]!.AsArray().Sum(v=>N(v)*N(v)))-1)<=1e-9);for(int axis=0;axis<3;axis++)Need(Math.Abs(N(on[i]![axis])-N(nn[i]![axis]))<=1e-12);}newRows[row]!["samples"]!["normalsWorldRaw"]=on.DeepClone();}
            newRows[row]!["samples"]!["positionsWorldRaw"]=a.DeepClone();
        }
        Need(JsonNode.DeepEquals(original,normalized));
    }
    private static JsonObject Step(string root,string id,int index,string kind,string sha)
    {
        Need(FileArtifacts.Token(sha,64));var bytes=FileArtifacts.ReadBytes(root,Path.Combine("surfacesetsteps",id,$"{index:D4}.{kind}.json"),16000000);Need(FileArtifacts.Sha256(bytes)==sha);return JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();
    }
    internal static JsonObject Execution(string root,string id,string sha,bool terminal=false,bool numerical=false,bool rotation=false)
    {
        var raw=SolidQuarterTurnProof.Raw(root,id,sha).Value;
        Need(S(raw,"state")==(rotation?"owned_surface_set_rotation_executed_unverified":"owned_surface_set_translation_executed_unverified")&&S(raw,"operationId")==id&&S(raw,"bridgeRequestId")==id&&S(raw,"qualificationScope")== (rotation?SurfaceSetRotationProof.Profile:numerical?NumericalProfile:Profile)&&ArtifactProof.Boolean(raw["geometryMutationAttempted"],true)&&ArtifactProof.Boolean(raw["contextChangeAttempted"],false)&&ArtifactProof.Boolean(raw["cadSaveAttempted"],false)&&I(raw["returnCode"])==1&&ArtifactProof.Boolean(raw["copyFenced"],true)&&S(raw,"recordedJournalState")=="executed_unverified"&&ArtifactProof.Boolean(raw["savedFilesUnchanged"],true)&&ArtifactProof.Boolean(raw["sameWorkerPostconditionsPassed"],true));
        var plan=ArtifactProof.Verified(root,"ownedexecutions",id,S(raw,"planSha256"),16000000);Need(JsonNode.DeepEquals(plan,raw["plan"])&&I(plan["schemaVersion"])==(rotation?9:numerical?8:7)&&S(plan,"operation")==(rotation?"rotate_owned_surface_set":"translate_owned_surface_set")&&S(plan,"measurementProfile")== (rotation?SurfaceSetRotationProof.Profile:numerical?NumericalProfile:Profile)&&S(plan,"operationId")==id&&ArtifactProof.Boolean(plan["executionEnabled"],true)&&plan["boundsBeforeWorldRaw"]is null);
        if(rotation)Need(!numerical&&JsonNode.DeepEquals(plan["normalPolicy"],SurfaceSetRotationProof.Policy())&&JsonNode.DeepEquals(raw["normalPolicy"],SurfaceSetRotationProof.Policy()));else if(numerical)Need(JsonNode.DeepEquals(plan["normalPolicy"],NormalPolicy())&&JsonNode.DeepEquals(raw["normalPolicy"],NormalPolicy()));else Need(!plan.ContainsKey("normalPolicy"));
        var keys=Keys(plan);var composites=plan["targetCompositeIds"]!.AsArray();Need(composites.Count==keys.Count&&composites.Select(I).All(v=>v>0)&&composites.Select(I).Distinct().Count()==keys.Count&&I(plan["nativeId"])==I(keys[0]![1])&&JsonNode.DeepEquals(plan["compositeId"],composites[0])&&I(raw["commandCalls"])==keys.Count&&I(raw["commandCallsAttempted"])==keys.Count);
        var packet=ArtifactProof.Verified(root,rotation?"ownedrotations":"ownedoperations",S(plan,rotation?"ownedRotationId":"ownedOperationId"),S(plan,rotation?"ownedRotationSha256":"ownedOperationSha256"),65536);foreach(var field in new[]{rotation?"rotationPlanId":"operationPlanId",rotation?"rotationPlanSha256":"operationPlanSha256","bindingId","bindingSha256","copyId","ownershipSha256","workingSha256","windowContextSha256"})Need(JsonNode.DeepEquals(plan[field],packet[field]));Need(ArtifactProof.Boolean(packet["executionEnabled"],false));
        var operation=ArtifactProof.Verified(root,rotation?"rotationplans":"operationplans",S(plan,rotation?"rotationPlanId":"operationPlanId"),S(plan,rotation?"rotationPlanSha256":"operationPlanSha256"),65536);
        if(rotation){Need(S(operation,"operation")=="rotate_entities"&&JsonNode.DeepEquals(operation["entityKeys"],keys)&&ArtifactProof.Boolean(operation["executionEnabled"],false));_=SurfaceSetRotationProof.Definition(plan,operation);var setRefs=operation["entitySet"]!.AsObject();var set=ArtifactProof.Verified(root,"entitysets",S(setRefs,"entitySetId"),S(setRefs,"entitySetSha256"),65536);Need(JsonNode.DeepEquals(set["entityKeys"],keys));}
        else{var sets=operation["entitySets"]!.AsArray();Need(S(operation,"operation")=="translate_entities"&&ArtifactProof.Boolean(operation["executionEnabled"],false)&&sets.Count==1&&JsonNode.DeepEquals(sets[0]!["entityKeys"],keys)&&JsonNode.DeepEquals(operation["parameters"]!["vector"],plan["vectorRaw"]));var set=ArtifactProof.Verified(root,"entitysets",sets[0]!["entitySetId"]!.GetValue<string>(),sets[0]!["entitySetSha256"]!.GetValue<string>(),65536);foreach(var field in set)Need(JsonNode.DeepEquals(field.Value,sets[0]![field.Key]));}
        ExecutionTools.ValidateSurfaceGrid(plan["surfaceGrid"]!.AsObject());var before=ArtifactProof.Verified(root,"responses",S(plan,"inspectionRequestId"),S(plan,"inspectionSha256"),16000000);var capture=plan["captureBefore"]!.AsObject();var parameters=new JsonObject{["mode"]="select_surface_single",["entityKey"]=keys[0]!.DeepClone(),["grid"]=plan["surfaceGrid"]!.DeepClone()};OwnedSurfaceSelectionProof.Capture(capture,parameters,before,0);Need(JsonNode.DeepEquals(capture["compositeId"],composites[0]));
        var vector=rotation?new JsonArray(0,0,0):plan["vectorRaw"]!.AsArray();if(!rotation)Need(vector.Count==3&&vector.Any(v=>N(v)!=0)&&vector.All(v=>Math.Abs(N(v))<=1000000));
        var journalFolder=FileArtifacts.FixedPath(root,"operations",id);var events=Directory.GetFiles(journalFolder,"*.json").Order(StringComparer.Ordinal).ToArray();Need(events.Length==(terminal?6:5));string? previous=null;JsonObject? returned=null;string[] states=["prepared","execution_claimed","command_started","command_returned","executed_unverified","succeeded_verified"];
        for(int i=0;i<events.Length;i++)
        {
            Need(Path.GetFileName(events[i])==$"{i:D6}.json");var bytes=FileArtifacts.ReadBytes(root,Path.Combine("operations",id,Path.GetFileName(events[i])),65536);var e=JsonNode.Parse(FileArtifacts.Parse(bytes).GetRawText())!.AsObject();Need(I(e["sequence"])==i&&S(e,"operationId")==id&&JsonNode.DeepEquals(e["planSha256"],raw["planSha256"])&&S(e,"state")==states[i]&&e["previousSha256"]?.GetValue<string>()==previous);previous=FileArtifacts.Sha256(bytes);if(i==3){Need(I(e["details"]!["returnCode"])==1&&I(e["details"]!["setStepCount"])==keys.Count);returned=e;}
        }
        var proof=raw["stepProof"]!.AsObject();Need(ArtifactProof.Fields(proof,"schemaVersion","profile","operationId","planSha256","targetCount","completedSteps","phase","lastSha256","records")&&I(proof["schemaVersion"])==1&&S(proof,"profile")== (rotation?SurfaceSetRotationProof.Profile:numerical?NumericalProfile:Profile)&&S(proof,"operationId")==id&&JsonNode.DeepEquals(proof["planSha256"],raw["planSha256"])&&I(proof["targetCount"])==keys.Count&&I(proof["completedSteps"])==keys.Count&&S(proof,"phase")=="ready");
        var rows=proof["records"]!.AsArray();Need(rows.Count==keys.Count);var stepFolder=FileArtifacts.FixedPath(root,"surfacesetsteps",id);Need(Directory.GetFiles(stepFolder).Length==keys.Count*3&&Directory.GetDirectories(stepFolder).Length==0);previous=null;var prior=LeaseTime.Parse(S(plan,"createdUtc"),"surface_set_time_invalid");
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i]!.AsObject();Need(ArtifactProof.Fields(row,"index","startedSha256","returnedSha256","observedSha256")&&I(row["index"])==i);
            foreach(var kind in new[]{"started","returned","observed"})
            {
                var e=Step(root,id,i,kind,S(row,kind+"Sha256"));Need(e.Count==(kind=="started"?9:kind=="returned"?10:11)&&I(e["schemaVersion"])==1&&S(e,"recordType")==kind&&S(e,"operationId")==id&&JsonNode.DeepEquals(e["planSha256"],raw["planSha256"])&&I(e["index"])==i&&JsonNode.DeepEquals(e["entityKey"],keys[i])&&JsonNode.DeepEquals(e["compositeId"],composites[i])&&e["previousSha256"]?.GetValue<string>()==previous&&Time(e)>=prior&&Time(e)<=Time(returned!));prior=Time(e);previous=S(row,kind+"Sha256");
                if(kind=="returned")Need(I(e["returnCode"])==1);
                if(kind=="observed")Post(before,e["observation"]!.AsObject(),capture,e["capture"]!.AsObject(),vector,new JsonArray(keys.Take(i+1).Select(k=>k!.DeepClone()).ToArray()),numerical,rotation?plan:null);
            }
        }
        Need(S(proof,"lastSha256")==previous&&returned!["details"]!["stepChainSha256"]!.GetValue<string>()==previous);
        var claim=JsonNode.Parse(FileArtifacts.ReadBytes(root,Path.Combine(rotation?"rotationplans":"operationplans",S(plan,rotation?"rotationPlanId":"operationPlanId")+".execution-once.json"),65536))!.AsObject();Need(S(claim,"operationId")==id&&JsonNode.DeepEquals(claim["planSha256"],raw["planSha256"])&&ArtifactProof.Boolean(claim["automaticReplayForbidden"],true));
        if(!terminal){var fence=FileArtifacts.ReadBytes(root,Path.Combine("workingcopies",S(plan,"copyId"),"mutation-fence.json"),65536);Need(FileArtifacts.Sha256(fence)==S(raw,"executionFenceSha256"));}
        return plan;
    }
    internal static void Receipt(string root,JsonObject reply,JsonObject parameters,bool numerical=false,bool rotation=false)
    {
        var raw=SolidQuarterTurnProof.Raw(root,S(reply,"bridgeRequestId"));Need(JsonNode.DeepEquals(raw.Value,reply));var plan=Execution(root,S(reply,"bridgeRequestId"),raw.Sha,false,numerical,rotation);foreach(var field in parameters)Need(JsonNode.DeepEquals(plan[field.Key],field.Value));reply["executionReceiptSha256"]=raw.Sha;reply["intentConsumed"]=true;
    }
    internal static void Verified(string root,JsonObject reply,JsonObject parameters,bool numerical=false,bool rotation=false)
    {
        var raw=SolidQuarterTurnProof.Raw(root,S(reply,"bridgeRequestId"));Need(JsonNode.DeepEquals(raw.Value,reply)&&S(reply,"state")==(rotation?"owned_surface_set_rotation_verified_observed":"owned_surface_set_translation_verified_observed")&&S(reply,"qualificationScope")== (rotation?SurfaceSetRotationProof.Profile:numerical?NumericalProfile:Profile));if(rotation)Need(JsonNode.DeepEquals(reply["normalPolicy"],SurfaceSetRotationProof.Policy()));else if(numerical)Need(JsonNode.DeepEquals(reply["normalPolicy"],NormalPolicy()));OwnedExecutionProof.CheckedTranslationVerification(reply,parameters);var plan=Execution(root,S(parameters,"executionRequestId"),S(parameters,"executionReceiptSha256"),true,numerical,rotation);
        var before=ArtifactProof.Verified(root,"responses",S(plan,"inspectionRequestId"),S(plan,"inspectionSha256"),16000000);var after=ArtifactProof.Verified(root,"responses",S(parameters,"inspectionRequestId"),S(parameters,"inspectionSha256"),16000000);Post(before,after,plan["captureBefore"]!.AsObject(),reply["surfaceCaptureAfter"]!.AsObject(),rotation?new JsonArray(0,0,0):plan["vectorRaw"]!.AsArray(),Keys(plan),numerical,rotation?plan:null);Need(!File.Exists(FileArtifacts.FixedPath(root,"workingcopies",S(plan,"copyId"),"mutation-fence.json")));
    }
}
