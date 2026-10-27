using ModelContextProtocol;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class SavedFinalizationOrderedTests
{
 static Fixture Config(bool enabled=true){var f=new Fixture();foreach(var k in new[]{"ownedSavedFinalizationExperimental","ownedSavedReopenExperimental","ownedSavedResultRestoreExperimental","ownedSavesExperimental","ownedSaveTargetsExperimental","workingCopyOpenExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})f.Config[k]=true;f.Config["ownedSavedFinalizationOrderedExperimental"]=enabled;f.SaveConfig();return f;}
 [Theory][InlineData(false,true,2)][InlineData(false,false,0)][InlineData(true,true,0)]public void SeparateFullOnlyGate(bool readOnly,bool enabled,int count){var f=Config(enabled);using var s=new BridgeSession(f.Options(readOnly));Assert.Equal(count,ToolCatalog.Create(s).Count(t=>t.ProtocolTool.Name.EndsWith("_owned_saved_finalization_ordered",StringComparison.Ordinal)));}
 [Theory][InlineData("ownedSavedFinalizationExperimental")][InlineData("ownedSavedResultRestoreExperimental")][InlineData("workingCopiesExperimental")]public void DependenciesRequired(string key){var f=Config();f.Config[key]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 [Fact]public void StrictBool(){var f=Config();f.Config["ownedSavedFinalizationOrderedExperimental"]="true";f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 [Fact]public async Task UnknownSourceNoQueue(){var f=Config();using var s=new BridgeSession(f.Options());var r=await new SavedFinalizationTools(s).ExecuteOrdered(new('a',32),new('b',32),new('c',32),"finalize_owned_saved_result_with_sealed_bytes_ordered_windows",new('d',32),new('e',64),CancellationToken.None);Assert.True(r.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Fact]public async Task ReadOnlyDirectRefusal(){var f=Config();using var s=new BridgeSession(f.Options(true));await Assert.ThrowsAsync<McpProtocolException>(()=>new SavedFinalizationTools(s).VerifyOrdered(new('a',32),new('b',64),new('c',32),new('d',32),CancellationToken.None));Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Fact]public void DispositionsRemainSeparate(){Assert.Equal("finalize_owned_saved_result_with_sealed_bytes",SavedFinalizationTools.Schema(false)["properties"]!["disposition"]!["enum"]![0]!.GetValue<string>());Assert.Equal("finalize_owned_saved_result_with_sealed_bytes_ordered_windows",SavedFinalizationTools.Schema(false,true)["properties"]!["disposition"]!["enum"]![0]!.GetValue<string>());}
}
