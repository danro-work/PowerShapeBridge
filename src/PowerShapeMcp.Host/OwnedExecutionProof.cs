using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Fixed one-use facade/proof functions only. No COM, execution, replay or fence removal.
public static class OwnedExecutionProof
{
    private static string Text(JsonObject value,string name,string code)=>ArtifactProof.String(value,name,code);
    public static JsonObject OpenParameters(JsonObject args,CandidateStore candidates,WorkingCopyStore copies,DocumentHandleStore documents)
    {
        ValidateTokens(args,"owned_open_arguments_invalid","workingCopyHandle","candidateDocumentHandle","activeDocumentHandle");
        var result=candidates.DocumentParameters(Text(args,"candidateDocumentHandle","owned_open_arguments_invalid"));
        var copy=copies.InspectParameters(Text(args,"workingCopyHandle","owned_open_arguments_invalid"));
        var choice=documents.Consume(Text(args,"activeDocumentHandle","owned_open_arguments_invalid"));
        var expiry=MinimumExpiry(result,choice);
        foreach(var (key,value) in copy)result[key]=value?.DeepClone();CopyChoice(result,choice);result["expiresUtc"]=LeaseTime.Format(expiry);return result;
    }
    public static JsonObject VerificationParameters(JsonObject args,CandidateStore candidates,DocumentHandleStore documents,bool translation,bool save=false,bool reopen=false)
    {
        var request=reopen?"reopenRequestId":save?"saveRequestId":translation?"executionRequestId":"openRequestId";var sha=reopen?"reopenReceiptSha256":save?"saveReceiptSha256":translation?"executionReceiptSha256":"openReceiptSha256";
        var code=save?"owned_save_verification_arguments_invalid":translation?"owned_translation_verification_arguments_invalid":"owned_open_verification_arguments_invalid";
        if(!ArtifactProof.Fields(args,request,sha,"candidateDocumentHandle","activeDocumentHandle")||!FileArtifacts.Token(Text(args,sha,code),64))throw new ReferenceRejected(code);
        foreach(var key in new[]{request,"candidateDocumentHandle","activeDocumentHandle"})if(!FileArtifacts.Token(Text(args,key,code)))throw new ReferenceRejected(code);
        var result=candidates.DocumentParameters(Text(args,"candidateDocumentHandle",code));var choice=documents.Consume(Text(args,"activeDocumentHandle",code));
        var expiry=MinimumExpiry(result,choice);CopyChoice(result,choice);result["expiresUtc"]=LeaseTime.Format(expiry);result[request]=args[request]!.DeepClone();result[sha]=args[sha]!.DeepClone();return result;
    }
    public static JsonObject SavedReopenParameters(JsonObject args,CandidateStore candidates,OwnedReopenStore reopens,DocumentHandleStore documents)
    {
        ValidateTokens(args,"saved_reopen_arguments_invalid","ownedReopenHandle","candidateDocumentHandle","activeDocumentHandle");
        var result=candidates.DocumentParameters(Text(args,"candidateDocumentHandle","saved_reopen_arguments_invalid"));
        var handle=Text(args,"ownedReopenHandle","saved_reopen_arguments_invalid");var preparedExpiry=reopens.ExpiresUtc(handle);
        var copy=reopens.Consume(handle);var choice=documents.Consume(Text(args,"activeDocumentHandle","saved_reopen_arguments_invalid"));
        var expiry=MinimumExpiry(result,choice);if(preparedExpiry<expiry)expiry=preparedExpiry;
        foreach(var field in copy)result[field.Key]=field.Value!.DeepClone();CopyChoice(result,choice);result["expiresUtc"]=LeaseTime.Format(expiry);return result;
    }
    public static void OpenReceipt(string root,JsonObject result,JsonObject parameters)
    {
        var code="owned_open_native_receipt_invalid";var id=Text(result,"operationId",code);var sha=Text(result,"planSha256",code);
        if(result["state"]?.GetValue<string>()!="working_copy_opened_observed"||!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64)||result["bridgeRequestId"]?.GetValue<string>()!=id||
           !JsonNode.DeepEquals(result["copyId"],parameters["copyId"])||!JsonNode.DeepEquals(result["ownershipSha256"],parameters["ownershipSha256"])||
           !ArtifactProof.Boolean(result["contextChangeAttempted"],true)||!ArtifactProof.Boolean(result["geometryMutationAttempted"],false)||!ArtifactProof.Boolean(result["cadSaveAttempted"],false))throw new ReferenceRejected(code);
        JsonObject plan;try{plan=ArtifactProof.Verified(root,"copyopenplans",id,sha,65536);}catch(ReferenceRejected){throw new ReferenceRejected("owned_open_plan_sha_differs");}
        if(!JsonNode.DeepEquals(plan,result["plan"])||parameters.Any(p=>!JsonNode.DeepEquals(plan[p.Key],p.Value))||plan["operation"]?.GetValue<string>()!="open_working_copy"||!ArtifactProof.Boolean(plan["geometryExecutionEnabled"],false)||plan["operationId"]?.GetValue<string>()!=id)throw new ReferenceRejected("owned_open_plan_identity_differs");
    }
    public static void TranslationReceipt(string root,JsonObject result,JsonObject parameters,bool surface=false)
    {
        var code="owned_translation_execution_receipt_invalid";var id=Text(result,"operationId",code);var sha=Text(result,"planSha256",code);
        if(result["state"]?.GetValue<string>()!="owned_translation_executed_unverified"||!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64)||result["bridgeRequestId"]?.GetValue<string>()!=id||
           !ArtifactProof.Boolean(result["geometryMutationAttempted"],true)||!ArtifactProof.Boolean(result["contextChangeAttempted"],false)||!ArtifactProof.Boolean(result["cadSaveAttempted"],false)||
           !ArtifactProof.IntegerIs(result["returnCode"],1)||!ArtifactProof.Boolean(result["copyFenced"],true)||result["recordedJournalState"]?.GetValue<string>()!="executed_unverified")throw new ReferenceRejected(code);
        JsonObject plan;try{plan=ArtifactProof.Verified(root,"ownedexecutions",id,sha,65536);}catch(ReferenceRejected){throw new ReferenceRejected("owned_translation_plan_sha_differs");}
        if(!JsonNode.DeepEquals(plan,result["plan"])||!ArtifactProof.Boolean(plan["executionEnabled"],true)||plan["operationId"]?.GetValue<string>()!=id||plan["operation"]?.GetValue<string>()!=(surface?"translate_owned_surface":"translate_owned_solid")||surface&&!ArtifactProof.IntegerIs(plan["schemaVersion"],2)||parameters.Any(p=>!JsonNode.DeepEquals(plan[p.Key],p.Value)))throw new ReferenceRejected("owned_translation_execution_plan_differs");
        if(surface){
            if(plan["measurementProfile"]?.GetValue<string>()!="owned_surface_grid_translation_v1"||plan["boundsBeforeWorldRaw"]is not null)throw new ReferenceRejected(code);
            ExecutionTools.ValidateSurfaceGrid(plan["surfaceGrid"]!.AsObject());var source=ArtifactProof.Verified(root,"responses",Text(plan,"inspectionRequestId",code),Text(plan,"inspectionSha256",code),16000000);
            var p=new JsonObject{["mode"]="select_surface_single",["entityKey"]=new JsonArray(21,SurfaceReadTools.Integer(plan["nativeId"])),["grid"]=plan["surfaceGrid"]!.DeepClone()};
            var capture=plan["captureBefore"]!.AsObject();OwnedSurfaceSelectionProof.Capture(capture,p,source,0);if(!JsonNode.DeepEquals(capture["compositeId"],plan["compositeId"]))throw new ReferenceRejected(code);
            var vector=plan["vectorRaw"]!.AsArray();if(vector.Count!=3||vector.All(v=>SurfaceReadTools.Number(v)==0)||vector.Any(v=>Math.Abs(SurfaceReadTools.Number(v))>1000000))throw new ReferenceRejected(code);
        }
    }
    public static void CheckedTranslationVerification(JsonObject result,JsonObject parameters)
    {
        if(!JsonNode.DeepEquals(result["executionOperationId"],parameters["executionRequestId"])||!JsonNode.DeepEquals(result["executionReceiptSha256"],parameters["executionReceiptSha256"])||
           result["recordedJournalState"]?.GetValue<string>()!="succeeded_verified"||!ArtifactProof.Boolean(result["copyFenceReleased"],true)||!ArtifactProof.Boolean(result["noCommandReplay"],true)||
           !ArtifactProof.Boolean(result["geometryMutationAttempted"],false)||!ArtifactProof.Boolean(result["contextChangeAttempted"],false)||!ArtifactProof.Boolean(result["cadSaveAttempted"],false))throw new ReferenceRejected("owned_translation_independent_receipt_invalid");
    }
    private static void ValidateTokens(JsonObject args,string code,params string[] names)
    {if(!ArtifactProof.Fields(args,names)||names.Any(n=>!FileArtifacts.Token(Text(args,n,code))))throw new ReferenceRejected(code);}
    private static DateTimeOffset MinimumExpiry(JsonObject candidate,JsonObject choice)
    {var a=LeaseTime.Parse(candidate["expiresUtc"]!.GetValue<string>(),"operation_time_invalid");var b=LeaseTime.Parse(choice["expiresUtc"]!.GetValue<string>(),"operation_time_invalid");return a<b?a:b;}
    private static void CopyChoice(JsonObject result,JsonObject choice)
    {foreach(var field in new[]{"listingRequestId","listingSha256","windowNumber","modelId"})result[field]=choice[field]!.DeepClone();}
}
