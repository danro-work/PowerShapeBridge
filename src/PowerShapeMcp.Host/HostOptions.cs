using System.Text.Json;

namespace PowerShapeMcp;

public sealed record HostOptions(string BridgeRoot, TimeSpan Timeout, bool ReadOnly)
{
    public const string Version = "0.50.0-dev";
    public static HostOptions Parse(string[] args)
    {
        string? root = null;
        var timeout = 30;
        var readOnly = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!seen.Add(args[i])) throw new ArgumentException("Duplicate option.");
            switch (args[i])
            {
                case "--bridge-root" when i + 1 < args.Length: root = args[++i]; break;
                case "--timeout" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out timeout) || timeout is < 1 or > 120)
                        throw new ArgumentException("Timeout must be an integer from 1 to 120 seconds.");
                    break;
                case "--read-only": readOnly = true; break;
                default: throw new ArgumentException("Unknown or incomplete option.");
            }
        }
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("--bridge-root is required.");
        var options = new HostOptions(Path.GetFullPath(root), TimeSpan.FromSeconds(timeout), readOnly);
        options.ReadConfiguration();
        return options;
    }

    public JsonElement ReadConfiguration()
    {
        var config = FileArtifacts.ReadJson(BridgeRoot, "bridge.config.json", 65536);
        if (config.GetProperty("coreProfile").GetString() != "general-v1" ||
            config.GetProperty("expectedVersion").GetString() != "23.2.2")
            throw new InvalidDataException("Only general-v1 / PowerShape 23.2.2 is supported.");
        foreach (var field in new[] { "translationTestPanel", "checkpointTestPanel", "parameterizedTranslationPanel", "diskVerificationPanel", "mcpMutationsPanel", "recipeOperationsPanel", "captureViewport" })
            if (config.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.False))
                throw new InvalidDataException("General core forbids fixture flags and captures.");
        foreach (var field in new[] { "expectedProject", "sourceStep", "protectedProject", "qualificationOperationId", "qualificationReceiptSha256", "qualificationReceiptRelease", "testProjectSha256", "protectedProjectSha256" })
            if (config.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null &&
                !(value.ValueKind == JsonValueKind.String && value.GetString() == ""))
                throw new InvalidDataException("General core forbids fixture paths and receipts.");
        foreach (var (field, upper) in new[] { ("timeoutSeconds", 120), ("operationTimeoutSeconds", 110), ("maxEntities", 100000) })
            if (!config.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Number ||
                !value.TryGetInt32(out var limit) || limit < 1 || limit > upper)
                throw new InvalidDataException("Invalid core limit: " + field);
        var dependencies = new Dictionary<string, string[]>
        {
            ["levelReadingExperimental"] = [],
            ["ownedLevelMovesExperimental"] = ["levelReadingExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"],
            ["ownedWorkplanesExperimental"] = ["entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"],
            ["ownedWorkplaneActivationExperimental"] = ["entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"],
            ["recoveryInspectionExperimental"] = [],
            ["recoveryResolutionExperimental"] = ["recoveryInspectionExperimental"],
            ["checkpointRecoveryExperimental"] = ["workingCopiesExperimental", "recoveryInspectionExperimental"],
            ["rotationPlansExperimental"] = ["entitySetsExperimental", "operationPlansExperimental"],
            ["uniformScalePlansExperimental"] = ["entitySetsExperimental", "operationPlansExperimental"],
            ["ownedUniformScalePlansExperimental"] = ["uniformScalePlansExperimental","documentBindingsExperimental","workingCopiesExperimental"],
            ["ownedSolidScalesExperimental"] = ["ownedUniformScalePlansExperimental","uniformScalePlansExperimental","operationPlansExperimental"],
            ["ownedDgkTargetsExperimental"] = ["entitySetsExperimental","documentBindingsExperimental","workingCopiesExperimental"],
            ["ownedSolidRotationsExperimental"] = ["ownedRotationPlansExperimental","operationPlansExperimental"],
            ["ownedSolidQuarterTurnsExperimental"] = ["ownedRotationPlansExperimental","operationPlansExperimental"],
            ["ownedRotationPlansExperimental"] = ["rotationPlansExperimental", "documentBindingsExperimental", "workingCopiesExperimental"],
            ["entitySetsExperimental"] = [],
            ["operationPlansExperimental"] = ["entitySetsExperimental"],
            ["workingCopiesExperimental"] = [],
            ["documentBindingsExperimental"] = ["workingCopiesExperimental", "entitySetsExperimental"],
            ["ownedOpenAdoptionExperimental"] = ["workingCopyOpenExperimental", "workingCopiesExperimental", "entitySetsExperimental", "documentBindingsExperimental"],
            ["workingCopyOpenExperimental"] = ["workingCopiesExperimental", "entitySetsExperimental", "documentBindingsExperimental"],
            ["ownedOperationPlansExperimental"] = ["operationPlansExperimental", "workingCopiesExperimental", "entitySetsExperimental", "documentBindingsExperimental"],
            ["ownedTranslationsExperimental"] = ["ownedOperationPlansExperimental"],
            ["ownedSaveTargetsExperimental"] = ["documentBindingsExperimental", "workingCopiesExperimental", "entitySetsExperimental"],
            ["ownedSavesExperimental"] = ["ownedSaveTargetsExperimental"],
            ["ownedSavedReopenExperimental"] = ["ownedSavesExperimental", "workingCopyOpenExperimental"],
            ["ownedSavedResultRestoreExperimental"] = ["ownedSavedReopenExperimental","ownedSavesExperimental","ownedSaveTargetsExperimental","workingCopyOpenExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"]
        };
        foreach (var (field, required) in dependencies)
        {
            if (!config.TryGetProperty(field, out var value)) continue;
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Experimental flag must be boolean.");
            if (value.ValueKind == JsonValueKind.True && required.Any(dep => !config.TryGetProperty(dep, out var setting) || setting.ValueKind != JsonValueKind.True))
                throw new InvalidDataException("Experimental feature dependencies are missing.");
        }
        return config;
    }
}
