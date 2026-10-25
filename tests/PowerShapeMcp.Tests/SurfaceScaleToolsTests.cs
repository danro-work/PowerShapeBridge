using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class SurfaceScaleToolsTests {
 [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
 public void SurfaceScaleUsesSeparateDefaultOffFullOnlyGate(bool readOnly,bool enabled){var f=new Fixture();foreach(var k in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","uniformScalePlansExperimental","ownedUniformScalePlansExperimental","surfaceSamplingExperimental"})f.Config[k]=true;f.Config["ownedSurfaceScalesExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(s).Where(t=>t.ProtocolTool.Name.Contains("surface_scale",StringComparison.Ordinal)).ToArray();Assert.Equal(enabled&&!readOnly?3:0,tools.Length);if(tools.Length>0){var execute=tools.Single(t=>t.ProtocolTool.Name.StartsWith("execute_",StringComparison.Ordinal));Assert.True(execute.ProtocolTool.Annotations!.DestructiveHint);Assert.False(execute.ProtocolTool.Annotations.ReadOnlyHint);var prepare=tools.Single(t=>t.ProtocolTool.Name.StartsWith("prepare_",StringComparison.Ordinal));Assert.Equal(4,prepare.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("surfaceGrid").GetProperty("properties").GetProperty("uDivisions").GetProperty("maximum").GetInt32());}Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Theory][InlineData("string")][InlineData("dependencies")]
 public void SurfaceScaleRejectsCoercedOrMissingDependencies(string fault){var f=new Fixture();f.Config["ownedSurfaceScalesExperimental"]=fault=="string"?JsonValue.Create("true"):JsonValue.Create(true);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
}
