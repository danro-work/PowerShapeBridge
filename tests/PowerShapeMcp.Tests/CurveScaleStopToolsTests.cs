using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class CurveScaleStopToolsTests {
 [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
 public void StopInspectionHasSeparateDefaultOffFullOnlyGate(bool readOnly,bool enabled){var f=new Fixture();foreach(var k in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","uniformScalePlansExperimental","ownedUniformScalePlansExperimental","curveLayoutExperimental","curveWitnessesExperimental","ownedCurveScalesExperimental"})f.Config[k]=true;f.Config["curveScaleStopInspectionExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(s).Where(t=>t.ProtocolTool.Name==CurveScaleStopTools.Name).ToArray();Assert.Equal(enabled&&!readOnly?1:0,tools.Length);if(tools.Length>0){Assert.True(tools[0].ProtocolTool.Annotations!.ReadOnlyHint);Assert.False(tools[0].ProtocolTool.Annotations!.DestructiveHint);Assert.False(tools[0].ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean());}Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Theory][InlineData("true")][InlineData("dependency")]
 public void RejectMalformedGateOrMissingDependency(string fault){var f=new Fixture();f.Config["curveScaleStopInspectionExperimental"]=fault=="true"?JsonValue.Create("true"):JsonValue.Create(true);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
}
