using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class OwnedCurveTranslationTests {
 [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
 public void FullOnlySeparateGate(bool readOnly,bool enabled){var f=new Fixture();foreach(var k in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedOperationPlansExperimental","ownedTranslationsExperimental","curveLayoutExperimental"})f.Config[k]=true;f.Config["ownedCurveTranslationsExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(s).Where(t=>t.ProtocolTool.Name.Contains("curve_translation",StringComparison.Ordinal)).ToArray();Assert.Equal(enabled&&!readOnly?2:0,tools.Length);if(tools.Length>0){var t=tools.Single(t=>t.ProtocolTool.Name.StartsWith("execute_",StringComparison.Ordinal));Assert.True(t.ProtocolTool.Annotations!.DestructiveHint);Assert.False(t.ProtocolTool.Annotations.ReadOnlyHint);Assert.Equal(1,t.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("curveRange").GetProperty("properties").GetProperty("startIndex").GetProperty("const").GetInt32());}Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Theory][InlineData("true")][InlineData(true)]public void BadFlagOrMissingDependencies(object value){var f=new Fixture();f.Config["ownedCurveTranslationsExperimental"]=JsonSerializerNode(value);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 static JsonNode JsonSerializerNode(object v)=>System.Text.Json.JsonSerializer.SerializeToNode(v)!;
 [Theory][InlineData(0,4)][InlineData(1,1)][InlineData(1,33)]public void IncompleteRangeRefused(int start,int end)=>Assert.Throws<ReferenceRejected>(()=>CurveTranslationProof.Range(new(){["startIndex"]=start,["endIndex"]=end}));
}
