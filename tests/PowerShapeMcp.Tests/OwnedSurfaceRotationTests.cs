using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class OwnedSurfaceRotationTests
{
    [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
    public void Z90ToolsAreExplicitlyGatedAndFullOnly(bool readOnly,bool enabled){
        var f=new Fixture();foreach(var k in new[]{"entitySetsExperimental","operationPlansExperimental","rotationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedRotationPlansExperimental","surfaceSamplingExperimental"})f.Config[k]=true;f.Config["ownedSurfaceRotationsExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));var all=ToolCatalog.Create(s);var tools=all.Where(t=>t.ProtocolTool.Name.Contains("surface_rotation",StringComparison.Ordinal)).ToArray();Assert.Equal(enabled&&!readOnly?2:0,tools.Length);if(tools.Length>0){var t=tools.Single(t=>t.ProtocolTool.Name.StartsWith("execute_",StringComparison.Ordinal));Assert.False(t.ProtocolTool.Annotations!.ReadOnlyHint);Assert.True(t.ProtocolTool.Annotations.DestructiveHint);Assert.Equal(4,t.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("surfaceGrid").GetProperty("properties").GetProperty("uDivisions").GetProperty("maximum").GetInt32());}Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
    }
    [Theory][InlineData("string")][InlineData("dependencies")]
    public void Z90FlagRejectsCoercionAndMissingDependencies(string fault){var f=new Fixture();f.Config["ownedSurfaceRotationsExperimental"]=fault=="string"?JsonValue.Create("true"):JsonValue.Create(true);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
}
