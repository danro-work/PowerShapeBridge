using ModelContextProtocol;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class SurfaceSetNumericalTests
{
 static Fixture Config(bool enabled=true){var f=new Fixture();foreach(var k in new[]{"ownedSurfaceSetTranslationsExperimental","ownedSurfaceTranslationsExperimental","surfaceSamplingExperimental","ownedTranslationsExperimental","ownedOperationPlansExperimental","operationPlansExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})f.Config[k]=true;f.Config["ownedSurfaceSetNumericalExperimental"]=enabled;f.SaveConfig();return f;}
 [Theory][InlineData(false,true,2)][InlineData(false,false,0)][InlineData(true,true,0)]public void FullOnlyNewGate(bool readOnly,bool enabled,int expected){var f=Config(enabled);using var s=new BridgeSession(f.Options(readOnly));Assert.Equal(expected,ToolCatalog.Create(s).Count(t=>t.ProtocolTool.Name.EndsWith("_surface_set_translation_numerical",StringComparison.Ordinal)));}
 [Theory][InlineData("ownedSurfaceSetTranslationsExperimental")][InlineData("ownedSurfaceTranslationsExperimental")][InlineData("surfaceSamplingExperimental")]public void RequiresStrictDependencies(string dep){var f=Config();f.Config[dep]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 [Fact]public void BooleanOnly(){var f=Config();f.Config["ownedSurfaceSetNumericalExperimental"]=1;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 [Fact]public async Task UnknownIntentNoQueue(){var f=Config();using var s=new BridgeSession(f.Options());var r=await new ExecutionTools(s).ExecuteOwnedSurfaceSetNumerical(new('a',32),System.Text.Json.JsonSerializer.SerializeToElement(new{tStart=1,tEnd=2,tDivisions=2,uStart=1,uEnd=2,uDivisions=3}),CancellationToken.None);Assert.True(r.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Fact]public async Task ReadOnlyDirectRefusal(){var f=Config();using var s=new BridgeSession(f.Options(true));await Assert.ThrowsAsync<McpProtocolException>(()=>new ExecutionTools(s).VerifyOwnedSurfaceSetNumerical(new('a',32),new('b',64),new('c',32),new('d',32),CancellationToken.None));Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
}
