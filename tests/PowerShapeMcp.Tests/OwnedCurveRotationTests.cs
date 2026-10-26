using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class OwnedCurveRotationTests {
 [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
 public void SeparateFullOnlyRotationGate(bool readOnly,bool enabled){var f=new Fixture();foreach(var k in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","rotationPlansExperimental","ownedRotationPlansExperimental","curveLayoutExperimental","curveWitnessesExperimental"})f.Config[k]=true;f.Config["ownedCurveRotationsExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(s).Where(t=>t.ProtocolTool.Name.Contains("curve_rotation",StringComparison.Ordinal)).ToArray();Assert.Equal(enabled&&!readOnly?2:0,tools.Length);if(tools.Length>0){var t=tools.Single(t=>t.ProtocolTool.Name.StartsWith("execute_",StringComparison.Ordinal));Assert.True(t.ProtocolTool.Annotations!.DestructiveHint);Assert.False(t.ProtocolTool.Annotations.ReadOnlyHint);Assert.Equal(1,t.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("curveRange").GetProperty("properties").GetProperty("startIndex").GetProperty("const").GetInt32());Assert.True(t.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("curveRange").TryGetProperty("description",out _));}Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Theory][InlineData("string")][InlineData("missing")]
 public void InvalidFlagRejected(string fault){var f=new Fixture();f.Config["ownedCurveRotationsExperimental"]=fault=="string"?JsonValue.Create("true"):JsonValue.Create(true);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
}
