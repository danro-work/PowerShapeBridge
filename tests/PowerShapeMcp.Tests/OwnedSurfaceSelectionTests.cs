using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class OwnedSurfaceSelectionTests
{
    static JsonObject Grid()=>new(){["tStart"]=1,["tEnd"]=2,["tDivisions"]=2,["uStart"]=1,["uEnd"]=2,["uDivisions"]=3};
    [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
    public void SurfaceProfileIsSeparateAndFullOnly(bool readOnly,bool enabled)
    {
        var f=new Fixture();foreach(var k in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedSelectionsExperimental","surfaceSamplingExperimental"})f.Config[k]=true;f.Config["ownedSurfaceSelectionsExperimental"]=enabled;f.SaveConfig();using var s=new BridgeSession(f.Options(readOnly));Assert.Equal(!readOnly&&enabled,s.SurfaceSelectionsEnabled);var tools=OwnedSelectionTools.Create(s);
        if(readOnly){Assert.Empty(tools);return;}var prepare=tools.Single(t=>t.ProtocolTool.Name=="prepare_owned_selection");Assert.Equal(enabled?4:2,prepare.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("parameters").GetProperty("oneOf").GetArrayLength());
    }
    [Theory][InlineData("extra")][InlineData("range")][InlineData("bool")][InlineData("missing_target")]
    public void NewSurfaceParametersRemainExactAndBounded(string fault)
    {
        var p=new JsonObject{["mode"]="select_surface_single",["candidateEntityHandle"]=new string('a',32),["grid"]=Grid()};if(fault=="extra")p["command"]="SAVE";if(fault=="range")p["grid"]!["tDivisions"]=5;if(fault=="bool")p["grid"]!["tDivisions"]=true;if(fault=="missing_target")p.Remove("candidateEntityHandle");Assert.Throws<ReferenceRejected>(()=>OwnedSelectionStore.Parameters(p));
    }
    [Theory][InlineData("string")][InlineData("dependencies")]
    public void SurfaceFlagDoesNotEnableAnUnownedOrUngatedProvider(string fault)
    {
        var f=new Fixture();f.Config["ownedSurfaceSelectionsExperimental"]=fault=="string"?JsonValue.Create("true"):JsonValue.Create(true);f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());
    }
}
