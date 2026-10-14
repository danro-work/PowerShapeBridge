using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedDgkConfigurationTests
{
    [Theory][InlineData("number")][InlineData("string")][InlineData("missing_set")][InlineData("missing_copy")][InlineData("missing_binding")]
    public void FlagAndDependencyCoercionAreRejected(string fault)
    {
        var data=new Fixture();foreach(var flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedDgkTargetsExperimental"})data.Config[flag]=true;
        if(fault=="number")data.Config["ownedDgkTargetsExperimental"]=1;
        if(fault=="string")data.Config["ownedDgkTargetsExperimental"]="true";
        if(fault=="missing_set")data.Config["entitySetsExperimental"]=false;
        if(fault=="missing_copy")data.Config["workingCopiesExperimental"]=false;
        if(fault=="missing_binding")data.Config["documentBindingsExperimental"]=false;
        data.SaveConfig();Assert.Throws<InvalidDataException>(()=>data.Options().ReadConfiguration());
    }
}
