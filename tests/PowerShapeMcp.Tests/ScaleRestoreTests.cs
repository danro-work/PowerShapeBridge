using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class ScaleRestoreTests
{
    internal static Fixture Config(bool enabled=true)
    {
        var f=new Fixture();foreach(var flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","workingCopyOpenExperimental","ownedSaveTargetsExperimental","ownedSavesExperimental","ownedSavedReopenExperimental","operationPlansExperimental","uniformScalePlansExperimental","ownedUniformScalePlansExperimental","ownedSolidScalesExperimental","levelReadingExperimental","recoveryInspectionExperimental"})f.Config[flag]=true;
        f.Config["ownedSolidScaleRestoreExperimental"]=enabled;f.SaveConfig();return f;
    }
    [Theory][InlineData(false,true,true)][InlineData(false,false,false)][InlineData(true,true,false)]
    public void CurrentAccessIsFullOnlyAndExplicitlyGated(bool readOnly,bool enabled,bool present)
    {
        var f=Config(enabled);using var s=new BridgeSession(f.Options(readOnly));var catalog=ToolCatalog.Create(s);Assert.Equal(present,catalog.Any(t=>t.ProtocolTool.Name=="restore_owned_solid_scale"));if(readOnly)Assert.Equal(8,catalog.Length);
        if(present){var tool=catalog.Single(t=>t.ProtocolTool.Name=="restore_owned_solid_scale").ProtocolTool;Assert.False(tool.InputSchema.GetProperty("additionalProperties").GetBoolean());Assert.Equal(4,tool.InputSchema.GetProperty("required").GetArrayLength());Assert.False(tool.Annotations!.ReadOnlyHint);Assert.False(tool.Annotations.DestructiveHint);}
    }
    [Theory][InlineData("ownedSolidScalesExperimental")][InlineData("ownedSavedReopenExperimental")][InlineData("workingCopiesExperimental")][InlineData("documentBindingsExperimental")]
    public void MissingPrerequisiteRefusesBeforeTransport(string field){var f=Config();f.Config[field]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Fact]
    public void OriginalVerifierDigestUsesSixtyFourHexCharacters()
    {
        var schema=ScaleRestoreTools.Schema();Assert.Equal("^[0-9a-f]{64}$",schema["properties"]!["scaleVerificationReceiptSha256"]!["pattern"]!.GetValue<string>());Assert.Equal("^[0-9a-f]{32}$",schema["properties"]!["scaleVerificationRequestId"]!["pattern"]!.GetValue<string>());
    }
    [Theory][InlineData("true")][InlineData(1)]
    public void FlagCoercionIsRejected(object value){var f=Config();f.Config["ownedSolidScaleRestoreExperimental"]=JsonValue.Create(value);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
    [Fact]
    public async Task MissingOrTamperedSuccessfulHistoryCannotIssueOrSubmit()
    {
        var f=Config();using var s=new BridgeSession(f.Options());var t=new ScaleRestoreTools(s);var id=new string('a',32);var sha=new string('b',64);
        var missing=await t.Restore(id,sha,new string('c',32),new string('d',32),CancellationToken.None);Assert.True(missing.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
        f.Write("responses/"+id+".json","{\"state\":\"owned_solid_scale_verified_observed\"}");var wrong=await t.Restore(id,sha,new string('c',32),new string('d',32),CancellationToken.None);Assert.True(wrong.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
        Assert.Throws<ReferenceRejected>(()=>s.Copies.InspectParameters(id));Assert.Throws<ReferenceRejected>(()=>s.Bindings.ValidationParameters(id));
    }
    [Fact]
    public async Task CancellationAndMaintenanceBeforeSubmissionProduceNoAuthority()
    {
        var f=Config();using var s=new BridgeSession(f.Options());var t=new ScaleRestoreTools(s);using var cancel=new CancellationTokenSource();cancel.Cancel();var id=new string('a',32);var sha=new string('b',64);
        var cancelled=await t.Restore(id,sha,id,id,cancel.Token);Assert.True(cancelled.IsError);Assert.Contains("cancelled_before_submission",cancelled.StructuredContent!.Value.GetRawText());
        f.Write("maintenance.json","{}");var maintenance=await t.Restore(id,sha,id,id,CancellationToken.None);Assert.True(maintenance.IsError);Assert.Contains("maintenance_before_submission",maintenance.StructuredContent!.Value.GetRawText());Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
    }
    [Fact]
    public void ExpiredOrForeignScopeCannotIssueCurrentCopyLease()
    {
        var copies=new WorkingCopyStore(utcClock:()=>DateTimeOffset.Parse("2026-10-24T06:00:00Z"));var receipt=new JsonObject{["state"]="owned_solid_scale_restored_observed",["restorationScope"]=ScaleRestoreProof.Scope,["originalJournalModified"]=false,["geometrySaved"]=false,["noCommandReplay"]=true,["timestampUtc"]="2026-10-24T05:00:00Z",["expiresUtc"]="2026-10-24T05:05:00Z"};Assert.Throws<ReferenceRejected>(()=>copies.IssueRestoredScale(receipt));receipt["restorationScope"]="other_scope";Assert.Throws<ReferenceRejected>(()=>copies.IssueRestoredScale(receipt));
    }
}
