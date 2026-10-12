using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public sealed class OperationJournal(string root)
{
    private static readonly Dictionary<string, string[]> Transitions = new()
    {
        [""] = ["prepared"],
        ["prepared"] = ["execution_claimed", "rejected_before_change"],
        ["execution_claimed"] = ["command_started", "rejected_before_change", "outcome_unknown"],
        ["command_started"] = ["command_returned", "outcome_unknown"],
        ["command_returned"] = ["succeeded_verified", "executed_unverified", "outcome_unknown", "recovery_required"],
        ["outcome_unknown"] = ["succeeded_verified", "recovery_required"],
        ["executed_unverified"] = ["succeeded_verified", "recovery_required"],
        ["recovery_required"] = [], ["succeeded_verified"] = [], ["rejected_before_change"] = []
    };
    private static readonly HashSet<string> Fields = ["schemaVersion", "operationId", "planSha256", "sequence", "previousSha256", "state", "timestampUtc", "details"];

    public JsonObject Read(string id)
    {
        if (!FileArtifacts.Token(id)) throw new ArgumentException("Invalid operation identity.");
        var folder = FileArtifacts.FixedPath(root, "operations", id);
        if (!Directory.Exists(folder))
        {
            var raw = FileArtifacts.ReadBytes(root, Path.Combine("operations", id + ".json"), 1000000);
            var receipt = FileArtifacts.Parse(raw);
            if (receipt.GetProperty("operationId").GetString() != id || receipt.GetProperty("state").ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Receipt identity/state differs.");
            return Result(id, FileArtifacts.Sha256(raw), JsonNode.Parse(receipt.GetRawText())!, "Recorded journal only; current model/export files and live PowerShape context not checked.");
        }
        try
        {
            var paths = Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal).ToArray();
            if (paths.Length is < 1 or > 64) throw new InvalidDataException("operation_journal_missing_or_limit");
            string? previous = null, plan = null;
            var state = "";
            var events = new JsonArray();
            for (var i = 0; i < paths.Length; i++)
            {
                if (Path.GetFileName(paths[i]) != $"{i:D6}.json") throw new InvalidDataException("operation_journal_sequence_invalid");
                var raw = FileArtifacts.ReadBytes(root, Path.GetRelativePath(root, paths[i]), 65536);
                var item = FileArtifacts.Parse(raw);
                if (!Fields.SetEquals(item.EnumerateObject().Select(p => p.Name))) throw new InvalidDataException("operation_journal_event_invalid");
                var sha = item.GetProperty("planSha256").GetString();
                if (item.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number || !item.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1 ||
                    item.GetProperty("sequence").ValueKind != JsonValueKind.Number || !item.GetProperty("sequence").TryGetInt32(out var sequence) || sequence != i ||
                    item.GetProperty("operationId").GetString() != id || item.GetProperty("previousSha256").GetString() != previous ||
                    !FileArtifacts.Token(sha, 64))
                    throw new InvalidDataException("operation_journal_chain_invalid");
                if (plan is not null && plan != sha) throw new InvalidDataException("operation_journal_plan_changed");
                var next = item.GetProperty("state").GetString();
                var details = item.GetProperty("details");
                if (next is null || !Transitions[state].Contains(next) || details.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("operation_journal_transition_invalid");
                // Compact UTF8 length is evaluated separately from the authoritative raw-byte hash.
                if (System.Text.Encoding.UTF8.GetByteCount(PythonJson.Dump(details)) > 8192)
                    throw new InvalidDataException("operation_journal_details_limit");
                if (next is "succeeded_verified" or "recovery_required" or "rejected_before_change") ValidateEvidence(details);
                var timestamp = item.GetProperty("timestampUtc").GetString();
                if (timestamp is null || !(timestamp.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(timestamp, @"[+-]\d\d:\d\d$")) ||
                    !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new InvalidDataException("operation_time_invalid");
                previous = FileArtifacts.Sha256(raw); plan = sha; state = next;
                events.Add(JsonNode.Parse(item.GetRawText()));
            }
            var locked = File.Exists(FileArtifacts.FixedPath(root, "operations", id, "writer.lock"));
            var journal = new JsonObject
            {
                ["operationId"] = id, ["planSha256"] = plan, ["recordedState"] = state,
                ["state"] = locked || state is "execution_claimed" or "command_started" or "command_returned" ? "outcome_unknown" : state,
                ["eventCount"] = paths.Length, ["journalSha256"] = previous, ["retryAllowed"] = false,
                ["writerLockPresent"] = locked, ["events"] = events
            };
            return Result(id, previous!, journal, "Recorded journal only; no live CAD verification or retry. An interrupted writer/claim/command is reported as outcome_unknown.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new JsonObject { ["state"] = "operation_journal_unavailable", ["operationId"] = id, ["outcome"] = "unknown", ["retryAllowed"] = false,
                ["error"] = new JsonObject { ["code"] = ex is InvalidDataException ? ex.Message : "operation_journal_unreadable", ["message"] = ex.Message },
                ["qualification"] = "No CAD verification or command retry. Inspect original journal and independent evidence." };
        }
    }

    private static JsonObject Result(string id, string sha, JsonNode receipt, string qualification) => new()
    { ["state"] = "operation_receipt_read", ["operationId"] = id, ["receiptSha256"] = sha, ["receipt"] = receipt, ["qualification"] = qualification };

    private static void ValidateEvidence(JsonElement details)
    {
        if (!details.TryGetProperty("evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Array || evidence.GetArrayLength() is < 1 or > 16)
            throw new InvalidDataException("operation_independent_evidence_required");
        foreach (var item in evidence.EnumerateArray())
            if (item.ValueKind != JsonValueKind.Object || !new HashSet<string> { "requestId", "sha256" }.SetEquals(item.EnumerateObject().Select(p => p.Name)) ||
                !FileArtifacts.Token(item.GetProperty("requestId").GetString()) || !FileArtifacts.Token(item.GetProperty("sha256").GetString(), 64))
                throw new InvalidDataException("operation_independent_evidence_required");
    }
}
