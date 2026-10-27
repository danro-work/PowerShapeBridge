using ModelContextProtocol;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class SurfaceSetStopDispositionTests
{
 static Fixture Config(bool enabled=true){var f=new Fixture();foreach(var k in new[]{"surfaceSetStopInspectionExperimental","ownedSurfaceSetTranslationsExperimental","ownedSurfaceTranslationsExperimental","surfaceSamplingExperimental","ownedTranslationsExperimental","ownedOperationPlansExperimental","operationPlansExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})f.Config[k]=true;f.Config["surfaceSetStopDispositionExperimental"]=enabled;f.SaveConfig();return f;}
 [Theory][InlineData(false,true,3)][InlineData(false,false,0)][InlineData(true,true,0)]
 public void FullOnlySeparateGate(bool readOnly,bool enabled,int expected){var f=Config(enabled);using var s=new BridgeSession(f.Options(readOnly));Assert.Equal(expected,ToolCatalog.Create(s).Count(t=>t.ProtocolTool.Name.EndsWith("surface_set_stop_disposition",StringComparison.Ordinal)));}
 [Theory][InlineData("surfaceSetStopInspectionExperimental")][InlineData("surfaceSamplingExperimental")][InlineData("workingCopiesExperimental")]
 public void DependenciesRequired(string name){var f=Config();f.Config[name]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 [Fact]public void StrictBoolean(){var f=Config();f.Config["surfaceSetStopDispositionExperimental"]="true";f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 [Fact]public async Task UnknownHandleNeverQueues(){var f=Config();using var s=new BridgeSession(f.Options());var r=await new SurfaceSetStopDispositionTools(s).Execute(new('a',32),CancellationToken.None);Assert.True(r.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Fact]public async Task ReadOnlyDirectCallRefused(){var f=Config();using var s=new BridgeSession(f.Options(true));await Assert.ThrowsAsync<McpProtocolException>(()=>new SurfaceSetStopDispositionTools(s).Execute(new('a',32),CancellationToken.None));Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Fact]public void ExactObserverShaSchema(){var schema=SurfaceSetStopDispositionTools.PrepareSchema();Assert.Equal("^[0-9a-f]{64}$",schema["properties"]!["observationReceiptSha256"]!["pattern"]!.GetValue<string>());Assert.False(schema["additionalProperties"]!.GetValue<bool>());}
 [Fact]public void UnqualifiedOrUnverifiedCannotIssueCopy(){var store=new WorkingCopyStore();Assert.Throws<ReferenceRejected>(()=>store.IssueDisposition(new(){["state"]="surface_set_stop_disposition_samples_match_unqualified"}));}
}
