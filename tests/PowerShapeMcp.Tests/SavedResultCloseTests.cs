using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;
public sealed class SavedResultCloseTests
{
    static Fixture Config(bool enabled=true){var f=new Fixture();foreach(var flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","workingCopyOpenExperimental","ownedSaveTargetsExperimental","ownedSavesExperimental","ownedSavedReopenExperimental","ownedSavedResultRestoreExperimental","levelReadingExperimental","recoveryInspectionExperimental"})f.Config[flag]=true;f.Config["ownedSavedResultCloseExperimental"]=enabled;f.SaveConfig();return f;}
    [Theory][InlineData(false,true,true)][InlineData(false,false,false)][InlineData(true,true,false)]
    public void ResultCloseIsFullOnlyAndDefaultOff(bool readOnly,bool enabled,bool present){var f=Config(enabled);using var s=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(s);foreach(var name in new[]{"execute_owned_saved_result_close","verify_owned_saved_result_close"})Assert.Equal(present,tools.Any(t=>t.ProtocolTool.Name==name));if(readOnly)Assert.Equal(8,tools.Length);}
    [Theory][InlineData("ownedSavedResultRestoreExperimental")][InlineData("ownedSavesExperimental")][InlineData("workingCopiesExperimental")]
    public void MissingDependencyRefusesBeforeTransport(string flag){var f=Config();f.Config[flag]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Theory][InlineData("true")][InlineData(1)][InlineData(null)]
    public void CoercedFlagsAreNotBoolean(object? value){var f=Config();f.Config["ownedSavedResultCloseExperimental"]=JsonValue.Create(value);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
    [Fact]
    public void ExactDispositionAndOriginalReceiptHashSchemas(){var execute=SavedResultCloseTools.Schema(false);var verify=SavedResultCloseTools.Schema(true);Assert.False(execute["additionalProperties"]!.GetValue<bool>());Assert.Equal(4,execute["required"]!.AsArray().Count);Assert.Equal("discard_owned_saved_result",execute["properties"]!["disposition"]!["enum"]![0]!.GetValue<string>());Assert.Equal("^[0-9a-f]{64}$",verify["properties"]!["closeReceiptSha256"]!["pattern"]!.GetValue<string>());Assert.Equal(4,verify["required"]!.AsArray().Count);}
    [Fact]
    public async Task HistoricalOrUnknownHandleCannotSubmitClose(){var f=Config();using var s=new BridgeSession(f.Options());var r=await new SavedResultCloseTools(s).Execute(new string('a',32),new string('b',32),new string('c',32),"discard_owned_saved_result",CancellationToken.None);Assert.True(r.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Fact]
    public async Task WrongDispositionCannotConsumeOrSubmit(){var f=Config();using var s=new BridgeSession(f.Options());var r=await new SavedResultCloseTools(s).Execute(new string('a',32),new string('b',32),new string('c',32),"save_and_close",CancellationToken.None);Assert.True(r.IsError);Assert.Contains("owned_saved_result_close_rejected",r.StructuredContent!.Value.GetRawText());Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
    [Fact]
    public async Task CancelledBeforeSubmissionCannotClose(){var f=Config();using var s=new BridgeSession(f.Options());using var c=new CancellationTokenSource();c.Cancel();var r=await new SavedResultCloseTools(s).Execute(new string('a',32),new string('b',32),new string('c',32),"discard_owned_saved_result",c.Token);Assert.True(r.IsError);Assert.Contains("cancelled_before_submission",r.StructuredContent!.Value.GetRawText());Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
}
