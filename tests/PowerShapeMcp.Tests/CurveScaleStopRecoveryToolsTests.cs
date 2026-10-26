using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class CurveScaleStopRecoveryToolsTests {
 [Theory]
 [InlineData("2026-10-26T10:22:36.0406570Z","2026-10-26T10:22:36.040657+00:00",true)]
 [InlineData("2026-10-26T10:22:36.0406571Z","2026-10-26T10:22:36.040657+00:00",false)]
 [InlineData("2026-10-26T10:22:36.0406580Z","2026-10-26T10:22:36.040657+00:00",false)]
 public void NativeExpiryEncodingDoesNotExtendLease(string native,string input,bool equal)=>Assert.Equal(equal,CurveScaleStopRecoveryProof.SameExpiry(native,input));
 [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
 public void RecoveryHasSeparateDefaultOffFullOnlyGate(bool readOnly,bool enabled){var f=new Fixture();foreach(var k in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","uniformScalePlansExperimental","ownedUniformScalePlansExperimental","curveLayoutExperimental","curveWitnessesExperimental","ownedCurveScalesExperimental","curveScaleStopInspectionExperimental","ownedSelectionsExperimental"})f.Config[k]=true;f.Config["curveScaleStopRecoveryExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(s).Where(t=>t.ProtocolTool.Name.Contains("curve_scale_stop_recovery",StringComparison.Ordinal)).ToArray();Assert.Equal(enabled&&!readOnly?3:0,tools.Length);if(tools.Length>0){var execute=tools.Single(t=>t.ProtocolTool.Name.StartsWith("execute_",StringComparison.Ordinal));Assert.True(execute.ProtocolTool.Annotations!.DestructiveHint);Assert.False(execute.ProtocolTool.Annotations!.ReadOnlyHint);}Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Theory][InlineData("true")][InlineData("dependency")]
 public void RejectMalformedGateOrMissingDependency(string fault){var f=new Fixture();f.Config["curveScaleStopRecoveryExperimental"]=fault=="true"?JsonValue.Create("true"):JsonValue.Create(true);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
}
