using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class CurveLayoutToolsTests {
 [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
 public void SeparatePassiveGateAndStrictSchema(bool readOnly,bool enabled){var f=new Fixture();f.Config["entitySetsExperimental"]=true;f.Config["curveLayoutExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(s).Where(t=>t.ProtocolTool.Name=="inspect_curve_sample_layout").ToArray();Assert.Equal(enabled?1:0,tools.Length);if(enabled){Assert.True(tools[0].ProtocolTool.Annotations!.ReadOnlyHint);Assert.False(tools[0].ProtocolTool.Annotations!.DestructiveHint);Assert.False(tools[0].ProtocolTool.InputSchema.GetProperty("properties").GetProperty("range").GetProperty("additionalProperties").GetBoolean());}Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Theory][InlineData("coerced")][InlineData("dependencies")]
 public void RejectBadFlag(string fault){var f=new Fixture();f.Config["curveLayoutExperimental"]=fault=="coerced"?JsonValue.Create("true"):JsonValue.Create(true);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 [Fact]public void RangeRejectsGuessingAndLayoutRejectsStrings(){Assert.Throws<ReferenceRejected>(()=>CurveLayoutTools.Range(new(){["startIndex"]=0,["endIndex"]=32}));Assert.Throws<ReferenceRejected>(()=>CurveLayoutTools.Range(new(){["startIndex"]=true,["endIndex"]=3}));Assert.Throws<ReferenceRejected>(()=>CurveLayoutTools.Layout(new(){["status"]=0}));}
}
