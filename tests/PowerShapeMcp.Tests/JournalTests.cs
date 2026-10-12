using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class JournalTests
{
    private static string Add(Fixture fixture, string id, int sequence, string? previous, string state, JsonObject? details = null)
    {
        var value = new JsonObject
        {
            ["schemaVersion"] = 1, ["operationId"] = id, ["planSha256"] = new string('b', 64),
            ["sequence"] = sequence, ["previousSha256"] = previous, ["state"] = state,
            ["timestampUtc"] = "2026-10-12T10:00:00Z", ["details"] = details ?? new JsonObject()
        };
        var text = value.ToJsonString(new() { WriteIndented = true }) + "\r\n";
        fixture.Write($"operations/{id}/{sequence:D6}.json", text);
        return FileArtifacts.Sha256(System.Text.Encoding.UTF8.GetBytes(text));
    }

    [Theory]
    [InlineData(0, "prepared")]
    [InlineData(1, "outcome_unknown")]
    [InlineData(2, "outcome_unknown")]
    [InlineData(3, "outcome_unknown")]
    [InlineData(4, "executed_unverified")]
    [InlineData(5, "succeeded_verified")]
    public void JournalDoesNotInferVerifiedSuccessFromReturn(int last, string expected)
    {
        var fixture = new Fixture(); var id = new string('a',32); string? previous = null;
        var states = new[] { "prepared", "execution_claimed", "command_started", "command_returned", "executed_unverified", "succeeded_verified" };
        for (var i = 0; i <= last; i++)
            previous = Add(fixture, id, i, previous, states[i], i == 5 ? new JsonObject { ["evidence"] = new JsonArray(new JsonObject { ["requestId"] = new string('c',32), ["sha256"] = new string('d',64) }) } : null);
        var result = new OperationJournal(fixture.Root).Read(id);
        Assert.Equal(previous, result["receiptSha256"]!.GetValue<string>());
        Assert.Equal(expected, result["receipt"]!["state"]!.GetValue<string>());
        Assert.False(result["receipt"]!["retryAllowed"]!.GetValue<bool>());
        fixture.Write($"operations/{id}/writer.lock", "interrupted");
        var locked = new OperationJournal(fixture.Root).Read(id);
        Assert.Equal("outcome_unknown", locked["receipt"]!["state"]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(fixture.Root,"operations",id,"writer.lock")));
    }

    [Theory]
    [InlineData("bad_hash")]
    [InlineData("missing_sequence")]
    [InlineData("invalid_transition")]
    [InlineData("missing_evidence")]
    [InlineData("partial_json")]
    [InlineData("bool_sequence")]
    [InlineData("extra_field")]
    public void CorruptJournalsStayUnknownWithoutRepair(string fault)
    {
        var fixture = new Fixture(); var id = new string('a',32);
        var sha = Add(fixture, id, 0, null, "prepared");
        var file = $"operations/{id}/000001.json";
        Add(fixture,id,1,fault == "bad_hash" ? new string('c',64) : sha, fault == "invalid_transition" ? "command_started" : fault == "missing_evidence" ? "rejected_before_change" : "execution_claimed");
        if (fault == "missing_sequence") File.Move(Path.Combine(fixture.Root,file), Path.Combine(fixture.Root,$"operations/{id}/000002.json"));
        if (fault == "partial_json") fixture.Write(file,"{\"state\":");
        if (fault is "bool_sequence" or "extra_field")
        {
            var value = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Root,file)))!.AsObject();
            if (fault == "bool_sequence") value["sequence"] = true; else value["command"] = "DELETE";
            fixture.Write(file,value.ToJsonString());
        }
        var before = Directory.GetFiles(Path.Combine(fixture.Root,"operations",id)).ToDictionary(path => Path.GetFileName(path)!,File.ReadAllText);
        var result = new OperationJournal(fixture.Root).Read(id);
        Assert.Equal("operation_journal_unavailable", result["state"]!.GetValue<string>());
        Assert.Equal("unknown", result["outcome"]!.GetValue<string>());
        foreach (var (name, text) in before) Assert.Equal(text, File.ReadAllText(Path.Combine(fixture.Root,"operations",id,name!)));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
    }
}
