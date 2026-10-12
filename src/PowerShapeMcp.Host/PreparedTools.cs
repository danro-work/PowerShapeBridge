using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class PreparedTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_entity_set",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Prepare an immutable observed SOLID/SURFACE/COMPCURVE set from this connection's candidate handles. Native worker rechecks receipt/context/expiry; no geometry edit or later execution authorization.")]
    public Task<CallToolResult> PrepareEntitySet([Description("Candidate document handle from this connection's inspection.")]string candidateDocumentHandle,[Description("One to 128 distinct candidate entity handles in that document; not read references.")]string[] candidateEntityHandles,CancellationToken cancellationToken)=>Run("prepare_entity_set",new(){["candidateDocumentHandle"]=candidateDocumentHandle,["candidateEntityHandles"]=Array(candidateEntityHandles)},cancellationToken);

    [McpServerTool(Name="validate_entity_set",ReadOnly=true,Destructive=false,OpenWorld=false),Description("Freshly validate this connection's observed set by native receipt and context. No CAD edit or lasting authorization; unavailable in read-only server.")]
    public Task<CallToolResult> ValidateEntitySet([Description("Set handle issued in this connection.")]string entitySetHandle,CancellationToken cancellationToken)=>Run("validate_entity_set",new(){["entitySetHandle"]=entitySetHandle},cancellationToken);

    [McpServerTool(Name="prepare_operation_plan",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Freeze a disabled local plan from verified observed sets and explicit qualified parameters. Writes a prepared journal only; no native command or CAD connection.")]
    public Task<CallToolResult> PrepareOperationPlan([Description("translate_entities or intersect_entity_sets; both are passive plans here.")]string operation,[Description("One set for translation, two distinct sets for intersection.")]string[] entitySetHandles,[Description("Exact world/document_raw/move vector or qualified zero offset parameters.")]JsonElement parameters,CancellationToken cancellationToken)=>Run("prepare_operation_plan",new(){["operation"]=operation,["entitySetHandles"]=Array(entitySetHandles),["parameters"]=JsonNode.Parse(parameters.GetRawText())},cancellationToken);

    [McpServerTool(Name="inspect_operation_plan",ReadOnly=true,Destructive=false,OpenWorld=false),Description("Inspect this connection's passive plan, source byte hashes, dependencies, expiry and journal. No live CAD read or execution authorization; absent from read-only server.")]
    public Task<CallToolResult> InspectOperationPlan([Description("Local plan handle from this connection.")]string operationPlanHandle,CancellationToken cancellationToken)=>Run("inspect_operation_plan",new(){["operationPlanHandle"]=operationPlanHandle},cancellationToken);

    [McpServerTool(Name="prepare_working_copy",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Create owned saved-byte Working.psmodel and independent read-only checkpoint from an explicit saved source/SHA. Native file identity/alias guards apply; no CAD open/save or unsaved-state copy.")]
    public Task<CallToolResult> PrepareWorkingCopy([Description("Absolute saved .psmodel path with no ambiguous segments or streams.")]string sourcePath,[Description("SHA256 of the saved source bytes, lowercase 64 hex.")]string sourceSha256,CancellationToken cancellationToken)=>Run("prepare_working_copy",new(){["sourcePath"]=sourcePath,["sourceSha256"]=sourceSha256},cancellationToken);

    [McpServerTool(Name="inspect_working_copy",ReadOnly=true,Destructive=false,OpenWorld=false),Description("Inspect owned file identities/paths/checkpoint and point-in-time saved working bytes by this connection's lease. No CAD connection; absent from read-only server.")]
    public Task<CallToolResult> InspectWorkingCopy([Description("Owned copy handle from this connection.")]string workingCopyHandle,CancellationToken cancellationToken)=>Run("inspect_working_copy",new(){["workingCopyHandle"]=workingCopyHandle},cancellationToken);

    [McpServerTool(Name="bind_working_document",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Passively bind an owned saved working copy to this connection's candidate document after native context/file rechecks. No activation, geometry command or later edit authorization.")]
    public Task<CallToolResult> BindWorkingDocument([Description("Fresh candidate document handle from this connection.")]string candidateDocumentHandle,[Description("Owned working-copy lease from this connection.")]string workingCopyHandle,CancellationToken cancellationToken)=>Run("bind_working_document",new(){["candidateDocumentHandle"]=candidateDocumentHandle,["workingCopyHandle"]=workingCopyHandle},cancellationToken);

    [McpServerTool(Name="validate_working_document_binding",ReadOnly=true,Destructive=false,OpenWorld=false),Description("Passively recheck owned binding and its live native context/files, without CAD commands. Requires this connection's dependencies; unavailable in read-only server.")]
    public Task<CallToolResult> ValidateWorkingDocumentBinding([Description("Owned binding handle issued in this connection.")]string workingDocumentBindingHandle,CancellationToken cancellationToken)=>Run("validate_working_document_binding",new(){["workingDocumentBindingHandle"]=workingDocumentBindingHandle},cancellationToken);

    [McpServerTool(Name="prepare_owned_operation",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Couple this connection's local plan and owned binding. Native worker pins proof bytes and checks one retained COM pair twice; writes a disabled packet and prepared-only journal. No geometry/save/context command.")]
    public Task<CallToolResult> PrepareOwnedOperation([Description("Passive operation-plan handle from this connection.")]string operationPlanHandle,[Description("Passive owned binding handle from this connection.")]string workingDocumentBindingHandle,CancellationToken cancellationToken)=>Run("prepare_owned_operation",new(){["operationPlanHandle"]=operationPlanHandle,["workingDocumentBindingHandle"]=workingDocumentBindingHandle},cancellationToken);

    [McpServerTool(Name="validate_owned_operation",ReadOnly=true,Destructive=false,OpenWorld=false),Description("Passively recheck disabled owned packet, all proof dependencies, working/checkpoint bytes and retained native context. No edit authorization, journal append or CAD command; absent from read-only server.")]
    public Task<CallToolResult> ValidateOwnedOperation([Description("Disabled owned-operation handle in this connection.")]string ownedOperationHandle,CancellationToken cancellationToken)=>Run("validate_owned_operation",new(){["ownedOperationHandle"]=ownedOperationHandle},cancellationToken);

    private async Task<CallToolResult> Run(string name,JsonObject args,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed(name);var timer=Stopwatch.StartNew();var acquired=false;var submitted=false;string? request=null;
        try
        {
            if(!await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken))return DiagnosticTools.Result(new(){["state"]="queue_timeout_before_submission"},true);
            acquired=true;
            if(cancellationToken.IsCancellationRequested)return DiagnosticTools.Result(new(){["state"]="cancelled_before_submission"},true);
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return DiagnosticTools.Result(new(){["state"]="maintenance_before_submission"},true);
            if(name is "prepare_operation_plan" or "inspect_operation_plan")
            {
                var local=name=="prepare_operation_plan"?session.Plans.Prepare(args):session.Plans.Inspect(Token(args,"operationPlanHandle"));
                return DiagnosticTools.Result(local);
            }
            var parameters=Parameters(name,args);submitted=true;
            var data=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]=name,["parameters"]=parameters},cancellationToken,session.Options.Timeout-timer.Elapsed);
            request=data["bridgeRequestId"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||data["state"]?.GetValue<string>()!=Success[name])
            {
                Clear(name);
                if(cancellationToken.IsCancellationRequested&&data["state"]?.GetValue<string>() is not ("cancelled_before_submission" or "queue_timeout_before_submission" or "maintenance_before_submission"))
                    data=new(){["state"]="cancelled_outcome_unknown",["bridgeRequestId"]=request,["cadMutationAttempted"]=false,["contextChangeAttempted"]=false,["localArtifactsMayExist"]=IsPreparing(name)};
                return DiagnosticTools.Result(data,true);
            }
            switch(name)
            {
                case "prepare_entity_set":data=session.Candidates.IssueSet(data);break;
                case "prepare_working_copy":data=session.Copies.Issue(data);break;
                case "inspect_working_copy":
                    if(parameters.Any(p=>!JsonNode.DeepEquals(data[p.Key],p.Value)))throw new ReferenceRejected("working_copy_native_receipt_identity_differs");break;
                case "bind_working_document":data=session.Bindings.Issue(data,parameters,Token(args,"candidateDocumentHandle"),Token(args,"workingCopyHandle"));break;
                case "validate_working_document_binding":
                    if(!JsonNode.DeepEquals(session.Bindings.ValidationParameters(Token(args,"workingDocumentBindingHandle")),parameters))throw new ReferenceRejected("document_binding_source_identity_differs");
                    session.Bindings.CheckedValidation(data,parameters);break;
                case "prepare_owned_operation":data=session.Owned.Issue(data,parameters,Token(args,"operationPlanHandle"),Token(args,"workingDocumentBindingHandle"));break;
                case "validate_owned_operation":
                    if(!JsonNode.DeepEquals(session.Owned.ValidationParameters(Token(args,"ownedOperationHandle")),parameters))throw new ReferenceRejected("owned_operation_dependency_lost_during_validation");
                    session.Owned.CheckedValidation(data,parameters);break;
            }
            return DiagnosticTools.Result(data);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return DiagnosticTools.Result(new(){["state"]="cancelled_before_submission"},true);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            if(acquired)Clear(name);var (state,fallback)=Failure(name);
            var code=error is ReferenceRejected rejected?rejected.Code:fallback;
            var data=new JsonObject{["state"]=state,["error"]=new JsonObject{["code"]=code,["message"]=error.Message},["cadMutationAttempted"]=false};
            if(name.Contains("binding",StringComparison.Ordinal)||name=="bind_working_document")data["contextChangeAttempted"]=false;
            if(name.Contains("owned_operation",StringComparison.Ordinal))
            {data.Remove("cadMutationAttempted");data["contextChangeAttempted"]=false;data["geometryMutationAttempted"]=false;data["executionEnabled"]=false;data["localArtifactsMayExist"]=submitted;if(request is not null)data["bridgeRequestId"]=request;}
            if(error is ReferenceRejected artifact&&artifact.OperationId is not null){data["operationId"]=artifact.OperationId;data["planSha256"]=artifact.PlanSha256;data["localArtifactsMayExist"]=true;}
            return DiagnosticTools.Result(data,true);
        }
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private JsonObject Parameters(string name,JsonObject args)=>name switch
    {
        "prepare_entity_set"=>session.Candidates.PrepareParameters(Token(args,"candidateDocumentHandle"),args["candidateEntityHandles"]!.AsArray().Select(x=>x!.GetValue<string>()).ToArray()),
        "validate_entity_set"=>session.Candidates.SetParameters(Token(args,"entitySetHandle")),
        "prepare_working_copy"=>args.DeepClone().AsObject(),
        "inspect_working_copy"=>session.Copies.InspectParameters(Token(args,"workingCopyHandle")),
        "bind_working_document"=>session.Bindings.PrepareParameters(Token(args,"candidateDocumentHandle"),Token(args,"workingCopyHandle")),
        "validate_working_document_binding"=>session.Bindings.ValidationParameters(Token(args,"workingDocumentBindingHandle")),
        "prepare_owned_operation"=>session.Owned.PrepareParameters(Token(args,"operationPlanHandle"),Token(args,"workingDocumentBindingHandle")),
        "validate_owned_operation"=>session.Owned.ValidationParameters(Token(args,"ownedOperationHandle")),
        _=>throw new InvalidOperationException("Unknown fixed prepared mapping.")
    };
    private void Clear(string name)
    {
        if(name is "prepare_entity_set" or "validate_entity_set")session.Candidates.Clear();
        else if(name is "prepare_operation_plan" or "inspect_operation_plan")session.Plans.Clear();
        else if(name is "prepare_working_copy" or "inspect_working_copy")session.Copies.Clear();
        else if(name is "bind_working_document" or "validate_working_document_binding")session.Bindings.Clear();
        else session.Owned.Clear();
    }
    private static (string State,string Code) Failure(string name)=>name switch
    {
        "prepare_entity_set" or "validate_entity_set"=>("entity_set_rejected","entity_set_receipt_or_data_unavailable"),
        "prepare_operation_plan" or "inspect_operation_plan"=>("operation_plan_rejected","operation_plan_artifact_unavailable"),
        "prepare_working_copy" or "inspect_working_copy"=>("working_copy_rejected","working_copy_data_unavailable"),
        "bind_working_document" or "validate_working_document_binding"=>("working_document_binding_rejected","document_binding_data_unavailable"),
        _=>("owned_operation_rejected","owned_operation_proof_unavailable")
    };
    private static bool IsPreparing(string name)=>name.StartsWith("prepare_",StringComparison.Ordinal)||name=="bind_working_document";
    private static string Token(JsonObject args,string field)=>args[field]!.GetValue<string>();
    private static JsonArray Array(string[] values)=>new(values.Select(v=>JsonValue.Create(v)).ToArray());
    private static readonly Dictionary<string,string> Success=new()
    {
        ["prepare_entity_set"]="entity_set_prepared_observed",["validate_entity_set"]="entity_set_validated_observed",
        ["prepare_working_copy"]="working_copy_prepared_disk",["inspect_working_copy"]="working_copy_inspected_disk",
        ["bind_working_document"]="working_document_bound_observed",["validate_working_document_binding"]="working_document_binding_validated_observed",
        ["prepare_owned_operation"]="owned_operation_prepared_observed",["validate_owned_operation"]="owned_operation_validated_observed"
    };
    public static McpServerTool[] Create(BridgeSession session)
    {
        var methods=new PreparedTools(session);using var stream=typeof(PreparedTools).Assembly.GetManifestResourceStream("prepared-tools.json")!;
        var schemas=JsonNode.Parse(stream)!.AsArray().Select(t=>t!.AsObject()).ToDictionary(t=>t["name"]!.GetValue<string>());
        var result=new List<McpServerTool>();
        foreach(var method in typeof(PreparedTools).GetMethods(BindingFlags.Instance|BindingFlags.Public).Where(m=>m.GetCustomAttribute<McpServerToolAttribute>() is not null))
        {
            var name=method.GetCustomAttribute<McpServerToolAttribute>()!.Name!;if(!session.PreparedEnabled(name))continue;
            var schema=schemas[name]["inputSchema"]!.DeepClone().AsObject();
            // SDK parameter descriptions are additive; constraints stay frozen from legacy.
            foreach(var parameter in method.GetParameters())
                if(schema["properties"]?[parameter.Name!] is JsonObject property&&parameter.GetCustomAttribute<DescriptionAttribute>() is { } description)property["description"]=description.Description;
            Action<JsonObject>? validate=name=="prepare_operation_plan"?OperationPlanStore.ValidateArguments:name=="prepare_working_copy"?WorkingCopyStore.ValidateSource:null;
            result.Add(new StrictTool(McpServerTool.Create(method,methods),session,schema,validate));
        }
        return result.ToArray();
    }
}
