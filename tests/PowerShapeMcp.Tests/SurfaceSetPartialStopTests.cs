using ModelContextProtocol;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class SurfaceSetPartialStopTests
{
 static Fixture Config(bool enabled=true){var f=new Fixture();foreach(var k in new[]{"ownedSurfaceSetTranslationsExperimental","ownedSurfaceTranslationsExperimental","surfaceSamplingExperimental","ownedTranslationsExperimental","ownedOperationPlansExperimental","operationPlansExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})f.Config[k]=true;f.Config["surfaceSetStopInspectionExperimental"]=enabled;f.SaveConfig();return f;}
 [Theory][InlineData(false,true,true)][InlineData(false,false,false)][InlineData(true,true,false)]
 public void EnabledFullOnlyCatalog(bool readOnly,bool enabled,bool expected){var f=Config(enabled);using var s=new BridgeSession(f.Options(readOnly));Assert.Equal(expected,ToolCatalog.Create(s).Any(t=>t.ProtocolTool.Name==SurfaceSetPartialStopTools.Name));}
 [Theory][InlineData("ownedSurfaceSetTranslationsExperimental")][InlineData("surfaceSamplingExperimental")][InlineData("ownedTranslationsExperimental")]
 public void MissingDependenciesRejected(string name){var f=Config();f.Config[name]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 [Fact]public void CoercedFlagRejected(){var f=Config();f.Config["surfaceSetStopInspectionExperimental"]="true";f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
 [Fact]public async Task UnknownOriginalRejectedBeforeQueue(){var f=Config();using var s=new BridgeSession(f.Options());var reply=await new SurfaceSetPartialStopTools(s).Inspect(new('a',32),new('b',64),new('c',32),new('d',32),CancellationToken.None);Assert.True(reply.IsError);Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
 [Fact]public async Task ReadOnlyDirectCallRejectedBeforeQueue(){var f=Config();using var s=new BridgeSession(f.Options(true));await Assert.ThrowsAsync<McpProtocolException>(()=>new SurfaceSetPartialStopTools(s).Inspect(new('a',32),new('b',64),new('c',32),new('d',32),CancellationToken.None));Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));}
}
