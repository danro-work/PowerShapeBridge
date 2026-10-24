using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;
public sealed class SavedFinalizationTests
{
    static Fixture Config(bool enabled=true){var f=new Fixture();foreach(var flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","workingCopyOpenExperimental","ownedSaveTargetsExperimental","ownedSavesExperimental","ownedSavedReopenExperimental","ownedSavedResultRestoreExperimental"})f.Config[flag]=true;f.Config["ownedSavedFinalizationExperimental"]=enabled;f.SaveConfig();return f;}
    [Theory][InlineData(false,true,true)][InlineData(false,false,false)][InlineData(true,true,false)]
    public void FinalizationFullOnlyWithSafeDefault(bool readOnly,bool enabled,bool present){var f=Config(enabled);using var s=new BridgeSession(f.Options(readOnly));foreach(var name in new[]{"execute_owned_saved_finalization","verify_owned_saved_finalization"})Assert.Equal(present,ToolCatalog.Create(s).Any(t=>t.ProtocolTool.Name==name));}
    [Theory][InlineData("ownedSavedReopenExperimental")][InlineData("ownedSavedResultRestoreExperimental")][InlineData("workingCopiesExperimental")]
    public void MissingSealOrCurrentSourceDependencyRefuses(string flag){var f=Config();f.Config[flag]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
    [Theory][InlineData("true")][InlineData(1)][InlineData(null)]
    public void JsonBooleanRequired(object? value){var f=Config();f.Config["ownedSavedFinalizationExperimental"]=JsonValue.Create(value);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
    [Fact]
    public void SeedReceiptAndDispositionAreExactSchema(){var s=SavedFinalizationTools.Schema(false);Assert.Equal(6,s["required"]!.AsArray().Count);Assert.False(s["additionalProperties"]!.GetValue<bool>());Assert.Equal("^[0-9a-f]{64}$",s["properties"]!["seedPreparationReceiptSha256"]!["pattern"]!.GetValue<string>());Assert.Equal("finalize_owned_saved_result_with_sealed_bytes",s["properties"]!["disposition"]!["enum"]![0]!.GetValue<string>());Assert.Equal(4,SavedFinalizationTools.Schema(true)["required"]!.AsArray().Count);}
    [Fact]
    public async Task UnknownLeaseCannotReachNative(){var f=Config();using var s=new BridgeSession(f.Options());var r=await new SavedFinalizationTools(s).Execute(new string('a',32),new string('b',32),new string('c',32),"finalize_owned_saved_result_with_sealed_bytes",new string('d',32),new string('e',64),CancellationToken.None);Assert.True(r.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Fact]
    public async Task WrongDispositionCannotSubmit(){var f=Config();using var s=new BridgeSession(f.Options());var r=await new SavedFinalizationTools(s).Execute(new string('a',32),new string('b',32),new string('c',32),"discard_owned_saved_result",new string('d',32),new string('e',64),CancellationToken.None);Assert.True(r.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Fact]
    public async Task CancelledBeforeSubmissionCannotClose(){var f=Config();using var s=new BridgeSession(f.Options());using var c=new CancellationTokenSource();c.Cancel();var r=await new SavedFinalizationTools(s).Execute(new string('a',32),new string('b',32),new string('c',32),"finalize_owned_saved_result_with_sealed_bytes",new string('d',32),new string('e',64),c.Token);Assert.True(r.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
}
