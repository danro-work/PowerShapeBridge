using System.Text.Json;
using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class SurfaceSetTranslationTests
{
    private static Fixture Configured(bool enabled=true)
    {
        var f=new Fixture();foreach(var k in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedOperationPlansExperimental","ownedTranslationsExperimental","surfaceSamplingExperimental","ownedSurfaceTranslationsExperimental"})f.Config[k]=true;f.Config["ownedSurfaceSetTranslationsExperimental"]=enabled;f.SaveConfig();return f;
    }
    [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
    public void ProfileIsGatedAndFullOnly(bool readOnly,bool enabled)
    {
        var f=Configured(enabled);using var s=new BridgeSession(f.Options(readOnly));var tools=ToolCatalog.Create(s).Where(t=>t.ProtocolTool.Name.Contains("surface_set_translation",StringComparison.Ordinal)).ToArray();Assert.Equal(enabled&&!readOnly?2:0,tools.Length);
        if(tools.Length>0){var execute=tools.Single(t=>t.ProtocolTool.Name.StartsWith("execute_",StringComparison.Ordinal));Assert.True(execute.ProtocolTool.Annotations!.DestructiveHint);Assert.False(execute.ProtocolTool.Annotations.ReadOnlyHint);Assert.Equal(4,execute.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("surfaceGrid").GetProperty("properties").GetProperty("tDivisions").GetProperty("maximum").GetInt32());Assert.False(execute.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean());}
        Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
    }
    [Theory][InlineData("true")][InlineData(1)][InlineData(null)]
    public void FlagDoesNotCoerce(object? value){var f=Configured();f.Config["ownedSurfaceSetTranslationsExperimental"]=JsonSerializer.SerializeToNode(value);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
    [Theory][InlineData("ownedSurfaceTranslationsExperimental")][InlineData("ownedTranslationsExperimental")][InlineData("surfaceSamplingExperimental")]
    public void EveryDependencyIsRequired(string dependency){var f=Configured();f.Config[dependency]=false;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());}
    [Theory][InlineData("[[21,8]]")][InlineData("[[21,8],[21,8]]")][InlineData("[[21,8],[40,5]]")]
    public void SingletonDuplicateAndMixedSetsRefused(string keys)=>Assert.Throws<ReferenceRejected>(()=>SurfaceSetTranslationProof.Keys(new(){["targetEntityKeys"]=JsonNode.Parse(keys)}));
    [Fact]public async Task UnknownOwnedHandleStopsBeforeQueue()
    {
        var f=Configured();using var s=new BridgeSession(f.Options());var grid=JsonSerializer.SerializeToElement(new{tStart=1,tEnd=2,tDivisions=2,uStart=1,uEnd=2,uDivisions=3});var result=await new ExecutionTools(s).ExecuteOwnedSurfaceSetTranslation(new string('a',32),grid,default);var data=JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!;Assert.True(result.IsError);Assert.Equal("owned_surface_set_translation_rejected_before_change",data["state"]!.GetValue<string>());Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
    }
    [Fact]public async Task DirectReadOnlyCallCannotBypassCatalog()
    {
        var f=Configured();using var s=new BridgeSession(f.Options(true));var grid=JsonSerializer.SerializeToElement(new{tStart=1,tEnd=2,tDivisions=2,uStart=1,uEnd=2,uDivisions=3});await Assert.ThrowsAsync<ModelContextProtocol.McpProtocolException>(()=>new ExecutionTools(s).ExecuteOwnedSurfaceSetTranslation(new string('a',32),grid,default));Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
    }
}
