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
    public SemaphoreSlim ReferenceGate { get; }=new(1,1);
    public bool CandidatesEnabled { get; }
    private readonly object admissionGate=new();
    private readonly HashSet<RequestId> pending=[];

    public BridgeSession(HostOptions options,Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
    {
        Options=options;Journal=new(options.BridgeRoot);Ipc=new(options);
        References=new(clock);Documents=new(clock,utcClock);Candidates=new(clock,utcClock);
        var config=Options.ReadConfiguration();
        CandidatesEnabled=config.TryGetProperty("entitySetsExperimental",out var flag)&&flag.ValueKind==System.Text.Json.JsonValueKind.True;
    }
    public void AssertToolAllowed(string name)
    {
        if(name is "bridge_status" or "operation_result" or "list_documents" or "inspect_active_model" or "resolve_entity_references" ||
            name=="inspect_entity_candidates"&&CandidatesEnabled)return;
        throw new McpProtocolException("Tool is unavailable in this server/mode.",McpErrorCode.InvalidParams);
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
            ["geometryMutations"] = "disabled", ["transport"] = "stdio",
            ["serverMode"] = Options.ReadOnly ? "read_only" : "general_core",
            ["configurationProfile"] = "general-v1", ["entitySets"] = CandidatesEnabled?"experimental_observations_only":"disabled", ["operationPlans"] = "disabled",
            ["workingCopies"] = "disabled", ["documentBindings"] = "disabled", ["workingCopyOpen"] = "disabled",
            ["ownedOperationPlans"] = "disabled", ["ownedTranslations"] = "disabled",
            ["maintenance"] = File.Exists(FileArtifacts.FixedPath(Options.BridgeRoot, "maintenance.json")),
            ["referenceScope"] = "ephemeral_read_observations; not accepted by mutation tools",
            ["documentChoice"] = "disabled", ["expectedProject"] = null, ["captureViewport"] = false,
            ["workerTimeoutSeconds"] = config.GetProperty("operationTimeoutSeconds").GetInt32()
        };
        foreach (var (file, key) in new[] { ("deployment-current.json", "installedDeployment"), ("panel-status.json", "lastPanelReport"), ("worker-stage.json", "lastWorkerStage") })
        {
            if (!File.Exists(FileArtifacts.FixedPath(Options.BridgeRoot, "reports", file))) continue;
            try { result[key] = JsonNode.Parse(FileArtifacts.ReadJson(Options.BridgeRoot, Path.Combine("reports", file), 1000000).GetRawText()); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { result[key] = null; }
        }
        return result;
    }
    public void Dispose(){Ipc.Dispose();ReferenceGate.Dispose();}
}
