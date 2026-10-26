using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class ExecutionTools(BridgeSession session)
{
    [McpServerTool(Name="activate_document",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Activate one already-open document using a fresh one-use listing handle. Native worker rechecks the listing immediately before its single activation command. Unknown outcome must not be retried.")]
    public Task<CallToolResult> ActivateDocument([Description("Unconsumed document handle from this connection's list_documents.")]string documentHandle,CancellationToken cancellationToken)=>Run("activate_document",new(){["documentHandle"]=documentHandle},cancellationToken);

    [McpServerTool(Name="open_working_copy",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Open this connection's owned saved-byte working copy in a new window after fresh candidate/listing checks. Consumes CAD leases before submission. No save or geometry command; requires independent verification and never retries an unknown outcome.")]
    public Task<CallToolResult> OpenWorkingCopy([Description("Owned copy handle from preparation.")]string workingCopyHandle,[Description("Fresh observed active candidate document.")]string candidateDocumentHandle,[Description("One-use active document choice from a fresh listing.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run("open_working_copy",new(){["workingCopyHandle"]=workingCopyHandle,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    [McpServerTool(Name="verify_working_copy_open",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Independently verify an owned open from its original receipt and fresh observations; issue a new owned-copy lease on success. May append its journal and, when enabled, an immutable outcome-resolution record. Does not replay open or issue CAD commands; unavailable in read-only server.")]
    public Task<CallToolResult> VerifyWorkingCopyOpen([Description("Original open request ID.")]string openRequestId,[Description("Original native open receipt SHA256.")]string openReceiptSha256,[Description("Fresh observed candidate document.")]string candidateDocumentHandle,[Description("Fresh one-use active document listing choice.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run("verify_working_copy_open",new(){["openRequestId"]=openRequestId,["openReceiptSha256"]=openReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    [McpServerTool(Name="execute_owned_translation",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false),Description("Execute one prepared owned SOLID translation through the existing single native worker. Consume all CAD leases before submission. A successful return is only executed_unverified and remains fenced until separate fresh verification. No save, replay, inverse or automatic retry.")]
    public Task<CallToolResult> ExecuteOwnedTranslation([Description("Prepared owned-operation handle in this connection.")]string ownedOperationHandle,CancellationToken cancellationToken)=>Run("execute_owned_translation",new(){["ownedOperationHandle"]=ownedOperationHandle},cancellationToken);

    [McpServerTool(Name="verify_owned_translation",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Verify the original translation receipt and authoritative journal using fresh native observations. May append its journal and an enabled immutable outcome-resolution record. The native verifier releases only its matching fence on verified success; no command replay or CAD mutation. Unavailable in read-only server.")]
    public Task<CallToolResult> VerifyOwnedTranslation([Description("Original execution request ID.")]string executionRequestId,[Description("Original native execution receipt SHA256.")]string executionReceiptSha256,[Description("Fresh candidate document observation.")]string candidateDocumentHandle,[Description("Fresh one-use active listing choice.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run("verify_owned_translation",new(){["executionRequestId"]=executionRequestId,["executionReceiptSha256"]=executionReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    [McpServerTool(Name="execute_owned_surface_translation",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false)]
    [Description("Execute one prepared explicit SURFACE translation in an owned mm/WORLD copy among1..8SURFACEs, with an explicit known UV grid. Reuses the coupled owned translate_entities plan/vector and one direct native TranslateItem call. Target sampled positions must shift; normals/visibility/layout/non-positional properties/other surface samples/files/windows stay unchanged. Native bounds unavailable; not full topology. Consumes all CAD leases before submission; fenced until separate verification. No selection/save/inverse/replay. Full only; separate experimental flag.")]
    public Task<CallToolResult> ExecuteOwnedSurfaceTranslation([Description("Prepared coupled owned-operation handle in this connection, one SURFACE target, nonzero WORLD vector in document raw mm.")]string ownedOperationHandle,[Description("Exact known tStart,tEnd,tDivisions,uStart,uEnd,uDivisions for every retained surface. Increasing ranges; integer divisions1..4. Domain is not inferred.")]System.Text.Json.JsonElement surfaceGrid,CancellationToken cancellationToken)=>Run("execute_owned_surface_translation",new(){["ownedOperationHandle"]=ownedOperationHandle,["surfaceGrid"]=JsonNode.Parse(surfaceGrid.GetRawText())},cancellationToken);

    [McpServerTool(Name="verify_owned_surface_translation",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Independently verify an original SURFACE translation using authoritative journal/return, immutable before-grid and fresh candidate/listing plus fresh native grid measurements. Complete journal and release only matching fence after observed success. No CAD command, inverse, save, replay or full topology claim. Full only.")]
    public Task<CallToolResult> VerifyOwnedSurfaceTranslation([Description("Original native execution request ID.")]string executionRequestId,[Description("Original raw execution SHA256,64 lowercase hex.")]string executionReceiptSha256,[Description("Fresh post-return candidate document.")]string candidateDocumentHandle,[Description("Fresh one-use active listing choice.")]string activeDocumentHandle,CancellationToken cancellationToken)=>Run("verify_owned_surface_translation",new(){["executionRequestId"]=executionRequestId,["executionReceiptSha256"]=executionReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    internal async Task<CallToolResult> Run(string name,JsonObject args,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed(name);bool curveRotation=name.Contains("curve_rotation",StringComparison.Ordinal);bool curve=curveRotation||name.Contains("curve_translation",StringComparison.Ordinal);bool surface=name.Contains("surface_translation",StringComparison.Ordinal);bool translate=name is "execute_owned_translation" or "execute_owned_surface_translation" or "execute_owned_curve_translation" or "execute_owned_curve_rotation";var timer=Stopwatch.StartNew();var acquired=false;var submitted=false;string? request=null;
        try
        {
            if(!await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken))return DiagnosticTools.Result(Stopped("queue_timeout_before_submission"),true);
            acquired=true;
            if(cancellationToken.IsCancellationRequested)return DiagnosticTools.Result(Stopped("cancelled_before_submission"),true);
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return DiagnosticTools.Result(Stopped("maintenance_before_submission"),true);
            var parameters=name switch
            {
                "activate_document"=>session.Documents.Consume(Text(args,"documentHandle")),
                "open_working_copy"=>OwnedExecutionProof.OpenParameters(args,session.Candidates,session.Copies,session.Documents),
                "execute_owned_surface_translation"=>SurfaceParameters(session,args),
                "execute_owned_curve_translation"=>CurveParameters(session,args),
                "execute_owned_curve_rotation"=>CurveRotationParameters(session,args),
                "execute_owned_translation"=>session.Owned.ValidationParameters(Text(args,"ownedOperationHandle")),
                _=>OwnedExecutionProof.VerificationParameters(args,session.Candidates,session.Documents,name is "verify_owned_translation" or "verify_owned_surface_translation" or "verify_owned_curve_translation" or "verify_owned_curve_rotation")
            };
            if(curve&&!translate)CurveTranslationProof.Execution(session.Options.BridgeRoot,Text(args,"executionRequestId"),Text(args,"executionReceiptSha256"),false,curveRotation);
            if(translate||name is "activate_document" or "open_working_copy")session.RevokeCadLeases(translate);
            submitted=true;
            var data=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=parameters},cancellationToken,session.Options.Timeout-timer.Elapsed);
            request=data["bridgeRequestId"]?.GetValue<string>();var state=data["state"]?.GetValue<string>();
            if(name=="activate_document")data["documentHandleConsumed"]=true;
            if(cancellationToken.IsCancellationRequested&&!BeforeSubmission(state))
                data=Unknown(name,name=="open_working_copy"?"working_copy_open_outcome_unknown":"cancelled_outcome_unknown",request);
            else if(state==Success[name])
            {
                switch(name)
                {
                    case "execute_owned_curve_rotation":CurveTranslationProof.Receipt(session.Options.BridgeRoot,data,parameters,true);break;
                    case "verify_owned_curve_rotation":CurveTranslationProof.Verified(session.Options.BridgeRoot,data,parameters,true);break;
                    case "execute_owned_curve_translation":CurveTranslationProof.Receipt(session.Options.BridgeRoot,data,parameters);break;
                    case "verify_owned_curve_translation":CurveTranslationProof.Verified(session.Options.BridgeRoot,data,parameters);break;
                    case "open_working_copy":OwnedExecutionProof.OpenReceipt(session.Options.BridgeRoot,data,parameters);break;
                    case "execute_owned_surface_translation":case "execute_owned_translation":OwnedExecutionProof.TranslationReceipt(session.Options.BridgeRoot,data,parameters,surface);break;
                    case "verify_owned_surface_translation":OwnedExecutionProof.CheckedTranslationVerification(data,parameters);break;
                    case "verify_owned_translation":OwnedExecutionProof.CheckedTranslationVerification(data,parameters);data=session.RecordResolution(name,data,parameters,cancellationToken);break;
                    case "verify_working_copy_open":
                        if(!JsonNode.DeepEquals(data["openOperationId"],args["openRequestId"])||!JsonNode.DeepEquals(data["openReceiptSha256"],args["openReceiptSha256"])||!JsonNode.DeepEquals(data["expiresUtc"],parameters["expiresUtc"]))throw new ReferenceRejected("verified_open_native_identity_differs");
                        data=session.RecordResolution(name,data,parameters,cancellationToken);data=session.Copies.IssueVerifiedOpen(data);break;
                }
                if(session.RecoveryResolutionEnabled&&name.StartsWith("verify_",StringComparison.Ordinal)&&(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout))
                {
                    if(name=="verify_working_copy_open")session.Copies.Clear();
                    data=Unknown(name,cancellationToken.IsCancellationRequested?"cancelled_outcome_unknown":"timeout_outcome_unknown",request);
                }
            }
            else if(state?.EndsWith("_outcome_unknown",StringComparison.Ordinal)==true)
                data=Unknown(name,state,request,data);
            if(name=="open_working_copy"&&data["state"]?.GetValue<string>()!="working_copy_opened_observed"&&data["state"]?.GetValue<string>()!="working_copy_open_rejected"&&!BeforeSubmission(data["state"]?.GetValue<string>()))session.Copies.Clear();
            return DiagnosticTools.Result(data,data["state"]?.GetValue<string>()!=Success[name]);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested)
        {return DiagnosticTools.Result(submitted?Unknown(name,"cancelled_outcome_unknown",request):Stopped("cancelled_before_submission"),true);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if(submitted&&name=="open_working_copy")session.Copies.Clear();
            var state=name switch
            {
                "activate_document"=>submitted?"document_activation_outcome_unknown":"document_choice_rejected",
                "open_working_copy"=>submitted?"working_copy_open_outcome_unknown":"working_copy_open_rejected",
                "execute_owned_curve_rotation"=>submitted?"owned_curve_rotation_outcome_unknown":"owned_curve_rotation_rejected_before_change",
                "execute_owned_translation" or "execute_owned_surface_translation" or "execute_owned_curve_translation"=>submitted?"owned_translation_outcome_unknown":"owned_translation_rejected_before_change",
                "verify_working_copy_open"=>"owned_open_verification_rejected",
                "verify_owned_curve_rotation"=>"owned_curve_rotation_verification_rejected",
                _=>"owned_translation_verification_rejected"
            };
            var data=submitted?Unknown(name,state,request):Stopped(state);
            data["error"]=new JsonObject{["code"]=error is ReferenceRejected rejected?rejected.Code:"owned_execution_proof_unavailable",["message"]=error.Message};
            return DiagnosticTools.Result(data,true);
        }
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private static JsonObject Stopped(string state)=>new(){["state"]=state,["contextChangeAttempted"]=false,["geometryMutationAttempted"]=false,["cadSaveAttempted"]=false,["retryAllowed"]=false};
    private static JsonObject Unknown(string name,string state,string? request,JsonObject? source=null)
    {
        var data=source?.DeepClone().AsObject()??new JsonObject();data["state"]=state;
        if(request is not null)data["bridgeRequestId"]=request;
        data["geometryMutationAttempted"]=name is "execute_owned_translation" or "execute_owned_surface_translation" or "execute_owned_curve_translation" or "execute_owned_curve_rotation";data["contextChangeAttempted"]=name is "open_working_copy" or "activate_document";
        data["cadSaveAttempted"]=false;data["retryAllowed"]=false;data["localArtifactsMayExist"]=true;
        if(name=="activate_document")data["documentHandleConsumed"]=true;
        if(name.StartsWith("verify_",StringComparison.Ordinal))data["qualification"]="Journal append may be unknown; no CAD command was issued.";
        return data;
    }
    private static JsonObject CurveRotationParameters(BridgeSession s,JsonObject a){var r=a["curveRange"]!.AsObject();CurveTranslationProof.Range(r);var p=s.OwnedRotations.ValidationParameters(Text(a,"ownedRotationHandle"));p["curveRange"]=r.DeepClone();return p;}
    private static JsonObject CurveParameters(BridgeSession s,JsonObject a){var r=a["curveRange"]!.AsObject();CurveTranslationProof.Range(r);var p=s.Owned.ValidationParameters(Text(a,"ownedOperationHandle"));p["curveRange"]=r.DeepClone();return p;}
    private static JsonObject SurfaceParameters(BridgeSession session,JsonObject input)
    {
        var grid=input["surfaceGrid"]!.AsObject();ValidateSurfaceGrid(grid);var p=session.Owned.ValidationParameters(Text(input,"ownedOperationHandle"));p["surfaceGrid"]=grid.DeepClone();return p;
    }
    internal static void ValidateSurfaceGrid(JsonObject grid){SurfaceReadTools.Grid(grid);if(SurfaceReadTools.Integer(grid["tDivisions"])>4||SurfaceReadTools.Integer(grid["uDivisions"])>4)throw new ReferenceRejected("surface_translation_grid_invalid");}
    private static bool BeforeSubmission(string? state)=>state is "cancelled_before_submission" or "queue_timeout_before_submission" or "maintenance_before_submission" or "failed_before_submission";
    private static string Text(JsonObject args,string name)=>args[name]!.GetValue<string>();
    private static readonly Dictionary<string,string> Success=new(){["execute_owned_curve_rotation"]="owned_curve_rotation_executed_unverified",["verify_owned_curve_rotation"]="owned_curve_rotation_verified_observed",["execute_owned_curve_translation"]="owned_translation_executed_unverified",["verify_owned_curve_translation"]="owned_translation_verified_observed",["activate_document"]="document_active_verified",["open_working_copy"]="working_copy_opened_observed",["verify_working_copy_open"]="owned_open_verified_observed",["execute_owned_surface_translation"]="owned_translation_executed_unverified",["verify_owned_surface_translation"]="owned_translation_verified_observed",["execute_owned_translation"]="owned_translation_executed_unverified",["verify_owned_translation"]="owned_translation_verified_observed"};
    public static McpServerTool[] Create(BridgeSession session)
    {
        var methods=new ExecutionTools(session);using var stream=typeof(ExecutionTools).Assembly.GetManifestResourceStream("execution-tools.json")!;
        var schemas=JsonNode.Parse(stream)!.AsArray().Select(t=>t!.AsObject()).ToDictionary(t=>t["name"]!.GetValue<string>());
        var surfaceExecute=schemas["execute_owned_translation"].DeepClone().AsObject();surfaceExecute["inputSchema"]!["properties"]!["surfaceGrid"]=SurfaceReadTools.GridSchema(4);surfaceExecute["inputSchema"]!["required"]!.AsArray().Add("surfaceGrid");schemas["execute_owned_surface_translation"]=surfaceExecute;schemas["verify_owned_surface_translation"]=schemas["verify_owned_translation"].DeepClone().AsObject();
        var result=new List<McpServerTool>();
        foreach(var method in typeof(ExecutionTools).GetMethods(BindingFlags.Instance|BindingFlags.Public).Where(m=>m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            var name=method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;if(!session.ExecutionEnabled(name))continue;
            var schema=schemas[name]["inputSchema"]!.DeepClone().AsObject();
            foreach(var parameter in method.GetParameters())
                if(schema["properties"]?[parameter.Name!] is JsonObject property&&parameter.GetCustomAttribute<DescriptionAttribute>() is { } description)property["description"]=description.Description;
            result.Add(new StrictTool(McpServerTool.Create(method,methods),session,schema,name=="execute_owned_surface_translation"?x=>ValidateSurfaceGrid(x["surfaceGrid"]!.AsObject()):null));
        }
        return result.ToArray();
    }
}
