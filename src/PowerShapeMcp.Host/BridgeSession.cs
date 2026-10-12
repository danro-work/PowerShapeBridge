using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// One instance per STDIO process/connection. Never reuse across future HTTP clients.
public sealed class BridgeSession(HostOptions options)
{
    public HostOptions Options { get; } = options;
    public OperationJournal Journal { get; } = new(options.BridgeRoot);
    public JsonObject Status()
    {
        var config = Options.ReadConfiguration();
        var result = new JsonObject
        {
            ["adapterVersion"] = HostOptions.Version, ["bridgeRoot"] = Options.BridgeRoot,
            ["configurationPresent"] = true, ["panelLiveness"] = "not_verified",
            ["geometryMutations"] = "disabled", ["transport"] = "stdio",
            ["serverMode"] = Options.ReadOnly ? "read_only" : "general_core",
            ["configurationProfile"] = "general-v1", ["entitySets"] = "disabled", ["operationPlans"] = "disabled",
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
}
