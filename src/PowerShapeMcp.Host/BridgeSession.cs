using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace PowerShapeMcp;

// One instance per STDIO process/connection. Never reuse across future HTTP clients.
public sealed class BridgeSession : IDisposable
{
    public HostOptions Options { get; }
    public OperationJournal Journal { get; }
    public BridgeIpc Ipc { get; }
    public ReferenceStore References { get; }
    public DocumentHandleStore Documents { get; }
    public CandidateStore Candidates { get; }
    public WorkingCopyStore Copies { get; }
    public OperationPlanStore Plans { get; }
    public DocumentBindingStore Bindings { get; }
    public OwnedOperationStore Owned { get; }
    public OwnedSaveTargetStore SaveTargets { get; }
    public OwnedSavedResultStore SavedResults { get; }
    public OwnedReopenStore Reopens { get; }
    public CheckpointRecoveryStore CheckpointRecoveries { get; }
    public RotationPlanStore RotationPlans { get; }
    public OwnedRotationStore OwnedRotations { get; }
    internal UniformScalePlanStore ScalePlans { get; }
    internal OwnedUniformScaleStore OwnedScales { get; }
    public SemaphoreSlim ReferenceGate { get; }=new(1,1);
    public bool CandidatesEnabled { get; }
    public bool RecoveryInspectionEnabled { get; }
    public bool RecoveryResolutionEnabled { get; }
    public OutcomeResolutionJournal Resolutions { get; }
    private readonly HashSet<string> prepared=[];
    private readonly HashSet<string> execution=[];
    private readonly object admissionGate=new();
    private readonly HashSet<RequestId> pending=[];

    public BridgeSession(HostOptions options,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
    {
        Options=options;Journal=new(options.BridgeRoot);Ipc=new(options);
        References=new(clock);Documents=new(clock,utcClock);Candidates=new(clock,utcClock);
        Copies=new(clock,utcClock);Plans=new(options.BridgeRoot,Candidates,clock,utcClock);
        Bindings=new(options.BridgeRoot,Candidates,Copies,clock,utcClock);Owned=new(options.BridgeRoot,Plans,Bindings,clock,utcClock);
        SaveTargets=new(options.BridgeRoot,Bindings,clock,utcClock);
        SavedResults=new(options.BridgeRoot,SaveTargets,Candidates,clock,utcClock);
        Reopens=new(options.BridgeRoot,SavedResults,clock,utcClock);
        CheckpointRecoveries=new(options.BridgeRoot,clock,utcClock);
        RotationPlans=new(options.BridgeRoot,Candidates,clock,utcClock);
        OwnedRotations=new(options.BridgeRoot,RotationPlans,Bindings,clock,utcClock);
        ScalePlans=new(options.BridgeRoot,Candidates,clock,utcClock);
        OwnedScales=new(options.BridgeRoot,ScalePlans,Bindings,clock,utcClock);
        var config=Options.ReadConfiguration();
        CandidatesEnabled=config.TryGetProperty("entitySetsExperimental",out var flag)&&flag.ValueKind==System.Text.Json.JsonValueKind.True;
        RecoveryInspectionEnabled=config.TryGetProperty("recoveryInspectionExperimental",out var recoveryFlag)&&recoveryFlag.ValueKind==System.Text.Json.JsonValueKind.True;
        RecoveryResolutionEnabled=!options.ReadOnly&&config.TryGetProperty("recoveryResolutionExperimental",out var resolutionFlag)&&resolutionFlag.ValueKind==System.Text.Json.JsonValueKind.True;
        Resolutions=new(options.BridgeRoot,utcClock);
        if(!options.ReadOnly)
        {
            execution.Add("activate_document");
            foreach(var (flagName,names) in new[]{
                ("rotationPlansExperimental",new[]{"prepare_rotation_plan"}),
                ("uniformScalePlansExperimental",new[]{"prepare_uniform_scale_plan"}),
                ("ownedUniformScalePlansExperimental",new[]{"prepare_owned_uniform_scale"}),
                ("ownedRotationPlansExperimental",new[]{"prepare_owned_rotation"}),
                ("entitySetsExperimental",new[]{"prepare_entity_set","validate_entity_set"}),
                ("operationPlansExperimental",new[]{"prepare_operation_plan","inspect_operation_plan"}),
                ("workingCopiesExperimental",new[]{"prepare_working_copy","inspect_working_copy"}),
                ("documentBindingsExperimental",new[]{"bind_working_document","validate_working_document_binding"}),
                ("ownedOperationPlansExperimental",new[]{"prepare_owned_operation","validate_owned_operation"}),
                ("ownedSaveTargetsExperimental",new[]{"prepare_owned_save_as"}),
                ("ownedSavedReopenExperimental",new[]{"prepare_owned_saved_reopen"})})
                if(config.TryGetProperty(flagName,out var enabled)&&enabled.ValueKind==System.Text.Json.JsonValueKind.True)prepared.UnionWith(names);
            foreach(var (flagName,names) in new[]{("workingCopyOpenExperimental",new[]{"open_working_copy","verify_working_copy_open"}),("ownedTranslationsExperimental",new[]{"execute_owned_translation","verify_owned_translation"}),("ownedSavesExperimental",new[]{"execute_owned_save_as","verify_owned_save_as"}),("ownedSavedReopenExperimental",new[]{"execute_owned_saved_reopen","verify_owned_saved_reopen"})})
                if(config.TryGetProperty(flagName,out var enabled)&&enabled.ValueKind==System.Text.Json.JsonValueKind.True)execution.UnionWith(names);
        }
        if(!options.ReadOnly&&config.TryGetProperty("checkpointRecoveryExperimental",out var recoveryCopy)&&recoveryCopy.ValueKind==System.Text.Json.JsonValueKind.True)
            prepared.UnionWith(new[]{"prepare_checkpoint_recovery","create_checkpoint_working_copy"});
    }
    public void AssertToolAllowed(string name)
    {
        if(name is "bridge_status" or "operation_result" or "list_documents" or "inspect_active_model" or "resolve_entity_references" ||
            name=="inspect_entity_candidates"&&CandidatesEnabled||name=="inspect_operation_recovery"&&RecoveryInspectionEnabled||prepared.Contains(name)||execution.Contains(name))return;
        throw new McpProtocolException("Tool is unavailable in this server/mode.",McpErrorCode.InvalidParams);
    }
    public bool PreparedEnabled(string name)=>prepared.Contains(name);
    public bool ExecutionEnabled(string name)=>execution.Contains(name);
    public JsonObject RecordResolution(string verifier,JsonObject receipt,JsonObject parameters,CancellationToken cancellationToken)
    {
        if(!RecoveryResolutionEnabled)return receipt;
        var result=receipt.DeepClone().AsObject();result["outcomeResolution"]=Resolutions.Record(verifier,receipt,parameters,cancellationToken);return result;
    }
    public void RevokeCadLeases(bool consumeOwned)
    {
        Documents.Clear();References.Clear();Candidates.Clear();Bindings.Clear();
        RotationPlans.Clear();
        OwnedRotations.Clear();
        ScalePlans.Clear();
        OwnedScales.Clear();
        SaveTargets.Clear();
        Reopens.Clear();
        if(consumeOwned){Plans.Clear();Owned.Clear();}
    }
    public IDisposable AcquireCall(RequestId id)
    {
        lock(admissionGate)
        {
            if(pending.Count>=8||!pending.Add(id))throw new McpProtocolException("Duplicate request id or too many pending calls.",McpErrorCode.InvalidRequest);
            return new Admission(this,id);
        }
    }
    private sealed class Admission(BridgeSession session,RequestId id):IDisposable
    {public void Dispose(){lock(session.admissionGate)session.pending.Remove(id);}}
    public JsonObject Status()
    {
        var config = Options.ReadConfiguration();
        var result = new JsonObject
        {
            ["adapterVersion"] = HostOptions.Version, ["bridgeRoot"] = Options.BridgeRoot,
            ["configurationPresent"] = true, ["panelLiveness"] = "not_verified",
            ["geometryMutations"] = ExecutionEnabled("execute_owned_translation")?"experimental_owned_solid_translation_only":"disabled", ["transport"] = "stdio",
            ["serverMode"] = Options.ReadOnly ? "read_only" : "general_core",
            ["configurationProfile"] = "general-v1", ["entitySets"] = CandidatesEnabled?"experimental_observations_only":"disabled", ["operationPlans"] = PreparedEnabled("prepare_operation_plan")?"experimental_local_only_execution_disabled":"disabled",
            ["workingCopies"] = PreparedEnabled("prepare_working_copy")?"experimental_saved_disk_bytes_only":"disabled", ["documentBindings"] = PreparedEnabled("bind_working_document")?"experimental_passive_owned_active_document":"disabled", ["workingCopyOpen"] = ExecutionEnabled("open_working_copy")?"experimental_owned_new_window_only":"disabled",
            ["ownedOperationPlans"] = PreparedEnabled("prepare_owned_operation")?"experimental_coupled_guard_execution_disabled":"disabled", ["ownedTranslations"] = ExecutionEnabled("execute_owned_translation")?"experimental_one_owned_solid_independent_verification":"disabled",
            ["ownedSaveTargets"] = PreparedEnabled("prepare_owned_save_as")?"experimental_new_fixed_file_target":"disabled", ["ownedSaves"] = ExecutionEnabled("execute_owned_save_as")?"experimental_one_save_separate_verification":"disabled",
            ["ownedSavedReopen"] = ExecutionEnabled("execute_owned_saved_reopen")?"experimental_one_guarded_open_separate_verification_new_binding":"disabled",
            ["recoveryInspection"] = RecoveryInspectionEnabled?"experimental_recorded_artifacts_only_no_execution_authority":"disabled",
            ["maintenance"] = File.Exists(FileArtifacts.FixedPath(Options.BridgeRoot, "maintenance.json")),
            ["referenceScope"] = "ephemeral_read_observations; not accepted by mutation tools",
            ["documentChoice"] = ExecutionEnabled("activate_document")?"enabled":"disabled", ["expectedProject"] = null, ["captureViewport"] = false,
            ["workerTimeoutSeconds"] = config.GetProperty("operationTimeoutSeconds").GetInt32()
        };
        if(config.TryGetProperty("recoveryResolutionExperimental",out _))result["recoveryResolution"]=RecoveryResolutionEnabled?"experimental_immutable_independent_verification_decisions":"disabled";
        if(config.TryGetProperty("checkpointRecoveryExperimental",out _))result["checkpointRecovery"]=PreparedEnabled("prepare_checkpoint_recovery")?"experimental_new_owned_saved_byte_copy_no_open":"disabled";
        if(config.TryGetProperty("uniformScalePlansExperimental",out _))result["uniformScalePlans"]=PreparedEnabled("prepare_uniform_scale_plan")?"experimental_local_plan_only_execution_disabled":"disabled";
        if(config.TryGetProperty("ownedUniformScalePlansExperimental",out _))result["ownedUniformScalePlans"]=PreparedEnabled("prepare_owned_uniform_scale")?"experimental_passive_owned_guard_execution_disabled":"disabled";
        foreach (var (file, key) in new[] { ("deployment-current.json", "installedDeployment"), ("panel-status.json", "lastPanelReport"), ("worker-stage.json", "lastWorkerStage") })
        {
            if (!File.Exists(FileArtifacts.FixedPath(Options.BridgeRoot, "reports", file))) continue;
            try { result[key] = JsonNode.Parse(FileArtifacts.ReadAdvisoryReport(Options.BridgeRoot,file).GetRawText()); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { result[key] = null; }
        }
        return result;
    }
    public void Dispose(){OwnedScales.Clear();ScalePlans.Clear();OwnedRotations.Clear();RotationPlans.Clear();CheckpointRecoveries.Clear();Reopens.Clear();SavedResults.Clear();Ipc.Dispose();ReferenceGate.Dispose();}
}
