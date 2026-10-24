using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class ReturnedRotationProofTests
{
    [Fact]public void FrameworkAndPythonAxisSpellingsRequireTheSameNativeDouble()
    {
        var framework=JsonNode.Parse("[2,-3,4,0.2672612419124244,0.53452248382484879,0.80178372573727319]");
        var python=JsonNode.Parse("[2,-3,4,0.2672612419124244,0.5345224838248488,0.8017837257372732]");
        Assert.True(SolidQuarterTurnProof.SameNativeAxis(framework,python));
        python![3]=0.2672612419124245;
        Assert.False(SolidQuarterTurnProof.SameNativeAxis(framework,python));
        Assert.False(SolidQuarterTurnProof.SameNativeAxis(framework,JsonNode.Parse("[2,-3,4,\"0.2672612419124244\",0.5345224838248488,0.8017837257372732]")));
    }
    static (Fixture F,string Id,string Sha,JsonObject Plan,JsonObject Returned) CommandFixture(){var f=new Fixture();var id=new string('a',32);var sha=new string('b',64);var target=new JsonObject{["entityKey"]=new JsonArray(40,5),["compositeId"]=800000005,["beforeMeasurementSha256"]=new string('c',64)};var plan=new JsonObject{["targets"]=new JsonArray(target),["angleDegrees"]=-30,["nativeAxis"]=new JsonArray(2,-3,4,1,2,3)};var start=new JsonObject{["operationId"]=id,["planSha256"]=sha,["index"]=0,["entityKey"]=target["entityKey"]!.DeepClone(),["compositeId"]=800000005,["angleDegrees"]=-30,["nativeAxis"]=plan["nativeAxis"]!.DeepClone(),["beforeObservationSha256"]=new string('d',64)};f.Write("rotationcommands/"+id+"/000000.started.json",start.ToJsonString());var returned=new JsonObject{["operationId"]=id,["planSha256"]=sha,["index"]=0,["startedSha256"]=FileArtifacts.Sha256(File.ReadAllBytes(Path.Combine(f.Root,"rotationcommands",id,"000000.started.json"))),["returnCode"]=1,["timestampUtc"]=LeaseTime.Format(DateTimeOffset.UtcNow)};f.Write("rotationcommands/"+id+"/000000.returned.json",returned.ToJsonString());return(f,id,sha,plan,returned);}
    [Fact]public void OneFrozenPositiveReturnIsExplicitEvidenceOnly(){var x=CommandFixture();var c=ReturnedRotationProof.Command(x.F.Root,x.Id,x.Plan,x.Sha);Assert.Equal(1,c["returnCode"]!.GetValue<int>());Assert.False(File.Exists(Path.Combine(x.F.Root,"rotationcommands",x.Id,"summary.json")));}
    [Theory][InlineData(0)][InlineData(-1)][InlineData("1")][InlineData(null)]
    public void MissingOrNonpositiveNativeSuccessCannotBeInferred(object? code){var x=CommandFixture();x.Returned["returnCode"]=JsonValue.Create(code);x.F.Write("rotationcommands/"+x.Id+"/000000.returned.json",x.Returned.ToJsonString());Assert.Throws<ReferenceRejected>(()=>ReturnedRotationProof.Command(x.F.Root,x.Id,x.Plan,x.Sha));}
    [Theory][InlineData("operationId")][InlineData("planSha256")][InlineData("startedSha256")]
    public void MismatchedReturnCannotResolveUnknown(string key){var x=CommandFixture();x.Returned[key]=new string('e',key=="operationId"?32:64);x.F.Write("rotationcommands/"+x.Id+"/000000.returned.json",x.Returned.ToJsonString());Assert.Throws<ReferenceRejected>(()=>ReturnedRotationProof.Command(x.F.Root,x.Id,x.Plan,x.Sha));}
    [Fact]public void AdditionalCommandSummaryIsOutsidePartialProfile(){var x=CommandFixture();x.F.Write("rotationcommands/"+x.Id+"/summary.json","{}");Assert.Throws<ReferenceRejected>(()=>ReturnedRotationProof.Command(x.F.Root,x.Id,x.Plan,x.Sha));}
    [Fact]public void PartialProfileNeverAppliesToQuarterTurnFacade(){var x=CommandFixture();Assert.Throws<ReferenceRejected>(()=>ReturnedRotationProof.Execution(x.F.Root,x.Id,new string('f',64),false));}
}
