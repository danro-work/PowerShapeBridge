using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class RotationPlanTests
{
    internal static JsonObject Parameters()=>JsonNode.Parse("""{"angleDegrees":90,"origin":[10,20,30],"direction":[0,0,2],"coordinateFrame":"world","lengthUnits":"document_raw"}""")!.AsObject();
    [Fact]
    public void PreparationFreezesExplicitAxisAndNeverCreatesNativeQueueOrExecutionAuthority()
    {
        var data=new SafetyFixture();var store=new RotationPlanStore(data.Root,data.Candidates,()=>data.Time,()=>data.Utc);
        var result=store.Prepare(data.Sets[0],Parameters());var plan=result["plan"]!.AsObject();
        Assert.False(plan["executionEnabled"]!.GetValue<bool>());Assert.Equal("long (integer degrees)",plan["nativeAngleType"]!.GetValue<string>());
        Assert.Equal(new[]{10d,20d,30d,0d,0d,1d},plan["nativeAxis"]!.AsArray().Select(value=>value!.GetValue<double>()));
        Assert.Equal(data.SourceSha,plan["entitySet"] is JsonObject set?OperationPlanStore.ReadSet(data.Root,set,data.Utc).Plan["inspectionSha256"]!.GetValue<string>():null);
        Assert.Equal(plan.ToJsonString(),store.Inspect(result["rotationPlanHandle"]!.GetValue<string>()).ToJsonString());
        Assert.False(Directory.Exists(Path.Combine(data.Root,"requests")));
        Assert.True(File.Exists(FileArtifacts.FixedPath(data.Root,"rotationplans",result["operationId"]!.GetValue<string>()+".json")));
    }
    [Theory]
    [InlineData("fractional")]
    [InlineData("noop")]
    [InlineData("zero_axis")]
    [InlineData("short_origin")]
    [InlineData("large_origin")]
    [InlineData("boolean_angle")]
    [InlineData("wrong_frame_type")]
    [InlineData("local_frame")]
    [InlineData("extra_command")]
    public void InvalidParametersAreRejectedBeforeAnyPlanWrite(string fault)
    {
        var data=new SafetyFixture();var args=Parameters();
        switch(fault)
        {
            case "fractional":args["angleDegrees"]=0.5;break;
            case "noop":args["angleDegrees"]=360;break;
            case "zero_axis":args["direction"]=new JsonArray(0,0,0);break;
            case "short_origin":args["origin"]=new JsonArray(0,0);break;
            case "large_origin":args["origin"]![0]=1000001;break;
            case "boolean_angle":args["angleDegrees"]=true;break;
            case "wrong_frame_type":args["coordinateFrame"]=true;break;
            case "local_frame":args["coordinateFrame"]="active_workplane";break;
            case "extra_command":args["command"]="NeverExecute";break;
        }
        var store=new RotationPlanStore(data.Root,data.Candidates,()=>data.Time,()=>data.Utc);
        Assert.Throws<ReferenceRejected>(()=>store.Prepare(data.Sets[0],args));
        Assert.False(Directory.Exists(Path.Combine(data.Root,"rotationplans")));
        Assert.False(Directory.Exists(Path.Combine(data.Root,"requests")));
    }
    [Fact]
    public void VerySmallAxisNormalizesWithoutUnderflow()
    {
        var args=Parameters();args["direction"]=new JsonArray(0d,1e-300,0d);
        var axis=RotationDefinition.Read(args).NativeAxis();Assert.Equal(new[]{10d,20d,30d,0d,1d,0d},axis.Select(value=>value!.GetValue<double>()));
    }
    [Theory]
    [InlineData("foreign")]
    [InlineData("expired")]
    [InlineData("changed_source")]
    [InlineData("changed_plan")]
    [InlineData("revoke")]
    public void ForeignExpiredChangedAndRevokedPlansCannotBeReused(string fault)
    {
        var data=new SafetyFixture();var store=new RotationPlanStore(data.Root,data.Candidates,()=>data.Time,()=>data.Utc);
        var result=store.Prepare(data.Sets[0],Parameters());var handle=result["rotationPlanHandle"]!.GetValue<string>();
        switch(fault)
        {
            case "foreign":store=new(data.Root,data.Candidates,()=>data.Time,()=>data.Utc);break;
            case "expired":data.Time+=301;data.Utc=data.Utc.AddSeconds(301);break;
            case "changed_source":data.Files.Write("responses/"+data.SourceId+".json","{}");break;
            case "changed_plan":data.Files.Write("rotationplans/"+result["operationId"]!.GetValue<string>()+".json","{}");break;
            case "revoke":store.Revoke(handle);break;
        }
        Assert.Throws<ReferenceRejected>(()=>store.Inspect(handle));
    }
    [Fact]
    public void MixedTargetSetIsFrozenWithoutReducingItToOneSolid()
    {
        var data=new SafetyFixture();var keys=new JsonArray(new JsonArray(40,5),new JsonArray(21,8),new JsonArray(59,10));
        var set=data.MakeSet(keys);var store=new RotationPlanStore(data.Root,data.Candidates,()=>data.Time,()=>data.Utc);
        var result=store.Prepare(set,Parameters());Assert.Equal(keys.ToJsonString(),result["plan"]!["entityKeys"]!.ToJsonString());
        Assert.False(Directory.Exists(Path.Combine(data.Root,"requests")));
    }
    [Fact]
    public void PlanLimitRejectsBeforeCreatingUnboundedArtifacts()
    {
        var data=new SafetyFixture();var store=new RotationPlanStore(data.Root,data.Candidates,()=>data.Time,()=>data.Utc);
        for(var index=0;index<128;index++)store.Prepare(data.Sets[0],Parameters());
        var before=TransportCancellationProof.Snapshot(data.Root);
        Assert.Equal("rotation_plan_limit",Assert.Throws<ReferenceRejected>(()=>store.Prepare(data.Sets[0],Parameters())).Code);
        TransportCancellationProof.Unchanged(data.Root,before);
    }
}
