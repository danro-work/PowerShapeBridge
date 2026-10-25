using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class OwnedSurfaceTranslationTests
{
    [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
    public void NewRecipesAreExplicitlyGatedAndFullOnly(bool readOnly,bool enabled)
    {
        var f=new Fixture();foreach(var k in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedOperationPlansExperimental","ownedTranslationsExperimental","surfaceSamplingExperimental"})f.Config[k]=true;f.Config["ownedSurfaceTranslationsExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));
        var tools=ToolCatalog.Create(s);var selected=tools.Where(t=>t.ProtocolTool.Name.Contains("surface_translation",StringComparison.Ordinal)).ToArray();Assert.Equal(enabled&&!readOnly?2:0,selected.Length);
        if(selected.Length>0){var execute=selected.Single(t=>t.ProtocolTool.Name=="execute_owned_surface_translation");Assert.Equal(4,execute.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("surfaceGrid").GetProperty("properties").GetProperty("tDivisions").GetProperty("maximum").GetInt32());Assert.False(execute.ProtocolTool.Annotations!.ReadOnlyHint);Assert.True(execute.ProtocolTool.Annotations.DestructiveHint);}
        Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
    }
    [Theory][InlineData("string")][InlineData("dependencies")]
    public void NewFlagRejectsCoercionAndMissingDependencies(string fault)
    {var f=new Fixture();f.Config["ownedSurfaceTranslationsExperimental"]=fault=="string"?JsonValue.Create("true"):JsonValue.Create(true);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
}
