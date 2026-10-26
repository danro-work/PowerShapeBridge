using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class PowerShapeProcessPinTests
{
    private static JsonObject Pin()=>new(){["processId"]=101,["processStartedUtc"]="2026-10-08T08:38:27.4633424Z",["windowsSessionId"]=1,["executablePath"]=@"C:\Program Files\Autodesk\PowerShape 2023\sys\exec64\powershape.exe"};
    [Fact]public void ExplicitCompleteIdentityAccepted(){var f=new Fixture();f.Config["powerShapeProcessPin"]=Pin();f.SaveConfig();f.Options().ReadConfiguration();}
    [Theory][InlineData("bool")][InlineData("missing")][InlineData("extra")][InlineData("pid_string")][InlineData("pid_zero")][InlineData("session_string")][InlineData("time_nozone")][InlineData("path_relative")]
    public void PartialCoercedOrFallbackPinsRejected(string fault)
    {
        var f=new Fixture();var p=Pin();if(fault=="missing")p.Remove("processStartedUtc");if(fault=="extra")p["fallback"]=true;if(fault=="pid_string")p["processId"]="101";if(fault=="pid_zero")p["processId"]=0;if(fault=="session_string")p["windowsSessionId"]="1";if(fault=="time_nozone")p["processStartedUtc"]="2026-10-08T08:38:27";if(fault=="path_relative")p["executablePath"]="powershape.exe";f.Config["powerShapeProcessPin"]=fault=="bool"?JsonValue.Create(true):p;f.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Options().ReadConfiguration());
    }
    [Fact]public void NullKeepsExistingDefault(){var f=new Fixture();f.Config["powerShapeProcessPin"]=null;f.SaveConfig();f.Options().ReadConfiguration();}
}
