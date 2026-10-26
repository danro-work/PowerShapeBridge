using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class CurveWitnessToolsTests {
 [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
 public void PassiveSeparateGateAndSchema(bool readOnly,bool enabled){var f=new Fixture();f.Config["entitySetsExperimental"]=true;f.Config["curveLayoutExperimental"]=true;f.Config["curveWitnessesExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(s).Where(t=>t.ProtocolTool.Name=="inspect_curve_translation_witnesses").ToArray();Assert.Equal(enabled?1:0,tools.Length);if(enabled){var t=tools.Single();Assert.True(t.ProtocolTool.Annotations!.ReadOnlyHint);Assert.False(t.ProtocolTool.Annotations.DestructiveHint);Assert.Equal(1,t.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("range").GetProperty("properties").GetProperty("startIndex").GetProperty("const").GetInt32());}Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Theory][InlineData("coerced")][InlineData("dependency")]
 public void InvalidFlagRefused(string fault){var f=new Fixture();f.Config["curveWitnessesExperimental"]=fault=="coerced"?JsonValue.Create("true"):JsonValue.Create(true);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
}
