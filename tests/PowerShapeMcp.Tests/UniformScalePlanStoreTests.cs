using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class UniformScalePlanStoreTests
{
    private static JsonObject Parameters()=>JsonNode.Parse("""{"factor":2,"centre":[10,20,30],"coordinateFrame":"world","lengthUnits":"document_raw","mode":"scale_in_place"}""")!.AsObject();
    private static UniformScalePlanStore Store(SafetyFixture data)=>new(data.Root,data.Candidates,()=>data.Time,()=>data.Utc);
    [Fact]
    public void LocalPlanPinsExplicitParametersSourcesAndPreparedOnlyJournalWithoutQueue()
    {
        var data=new SafetyFixture();var store=Store(data);var result=store.Prepare(data.Sets[0],Parameters());var plan=result["plan"]!.AsObject();
        Assert.False(plan["executionEnabled"]!.GetValue<bool>());Assert.False(plan["nativeProviderQualified"]!.GetValue<bool>());
        Assert.Equal("uniform_scale_entities",plan["operation"]!.GetValue<string>());
        Assert.Equal(2,plan["parameters"]!["factor"]!.GetValue<double>());
        Assert.Equal(new[]{10d,20d,30d},plan["parameters"]!["centre"]!.AsArray().Select(v=>v!.GetValue<double>()));
        var handle=result["scalePlanHandle"]!.GetValue<string>();Assert.Equal(plan.ToJsonString(),store.Inspect(handle).ToJsonString());
        Assert.Equal(result["planSha256"]!.GetValue<string>(),store.ReferenceParameters(handle)["scalePlanSha256"]!.GetValue<string>());
        var journal=ArtifactProof.Journal(data.Root,result["operationId"]!.GetValue<string>());
        Assert.Equal("prepared",journal["state"]!.GetValue<string>());Assert.Equal(1,journal["eventCount"]!.GetValue<int>());
        Assert.False(Directory.Exists(Path.Combine(data.Root,"requests")));
    }
    [Theory]
    [InlineData("boolean")][InlineData("string")][InlineData("zero")][InlineData("negative")][InlineData("noop")]
    [InlineData("null_factor")][InlineData("centre_short")][InlineData("centre_null")][InlineData("centre_bool")]
    [InlineData("local_frame")][InlineData("units")][InlineData("copy_mode")][InlineData("axis_lock")]
    public void InvalidInputsCannotWriteAPlanOrRequest(string fault)
    {
        var data=new SafetyFixture();var parameters=Parameters();
        switch(fault)
        {
            case "boolean":parameters["factor"]=true;break;
            case "string":parameters["factor"]="2";break;
            case "zero":parameters["factor"]=0;break;
            case "negative":parameters["factor"]=-2;break;
            case "noop":parameters["factor"]=1;break;
            case "null_factor":parameters["factor"]=null;break;
            case "centre_short":parameters["centre"]=new JsonArray(1,2);break;
            case "centre_null":parameters["centre"]=null;break;
            case "centre_bool":parameters["centre"]![0]=true;break;
            case "local_frame":parameters["coordinateFrame"]="active_workplane";break;
            case "units":parameters["lengthUnits"]="mm";break;
            case "copy_mode":parameters["mode"]="keep_original";break;
            case "axis_lock":parameters["lockX"]=true;break;
        }
        Assert.Throws<ReferenceRejected>(()=>Store(data).Prepare(data.Sets[0],parameters));
        Assert.False(Directory.Exists(Path.Combine(data.Root,"scaleplans")));Assert.False(Directory.Exists(Path.Combine(data.Root,"requests")));
    }
    [Theory][InlineData("foreign")][InlineData("expired")][InlineData("changed_source")]
    [InlineData("changed_plan")][InlineData("revoke")][InlineData("clear")][InlineData("writer_lock")][InlineData("journal_tamper")]
    public void InvalidatedPlansCannotBeUsed(string fault)
    {
        var data=new SafetyFixture();var store=Store(data);var result=store.Prepare(data.Sets[0],Parameters());
        var handle=result["scalePlanHandle"]!.GetValue<string>();var id=result["operationId"]!.GetValue<string>();
        switch(fault)
        {
            case "foreign":store=Store(data);break;
            case "expired":data.Time+=301;data.Utc=data.Utc.AddSeconds(301);break;
            case "changed_source":data.Files.Write("responses/"+data.SourceId+".json","{}");break;
            case "changed_plan":data.Files.Write("scaleplans/"+id+".json","{}");break;
            case "revoke":store.Revoke(handle);break;
            case "clear":store.Clear();break;
            case "writer_lock":data.Files.Write("operations/"+id+"/writer.lock","interrupted");break;
            case "journal_tamper":data.Files.Write("operations/"+id+"/000000.json","{}");break;
        }
        Assert.Throws<ReferenceRejected>(()=>store.Inspect(handle));
    }
    [Fact]
    public void CallerMutationCannotChangePinnedParameters()
    {
        var data=new SafetyFixture();var store=Store(data);var parameters=Parameters();
        var result=store.Prepare(data.Sets[0],parameters);var handle=result["scalePlanHandle"]!.GetValue<string>();
        parameters["factor"]=100;parameters["centre"]![0]=999;result["plan"]!["parameters"]!["factor"]=99;
        Assert.Equal(2,store.Inspect(handle)["parameters"]!["factor"]!.GetValue<double>());
        Assert.Equal(10,store.Inspect(handle)["parameters"]!["centre"]![0]!.GetValue<double>());
    }
    [Fact]
    public void MixedSetKeepsAllTargetsWithoutNativeInvocation()
    {
        var data=new SafetyFixture();var keys=new JsonArray(new JsonArray(40,5),new JsonArray(21,8),new JsonArray(59,10));
        var set=data.MakeSet(keys);var result=Store(data).Prepare(set,Parameters());
        Assert.Equal(keys.ToJsonString(),result["plan"]!["entityKeys"]!.ToJsonString());
    }
    [Fact]
    public void ShrinkFactorIsSupportedWithoutRounding()
    {
        var data=new SafetyFixture();var args=Parameters();args["factor"]=.25;
        Assert.Equal(.25,Store(data).Prepare(data.Sets[0],args)["plan"]!["parameters"]!["factor"]!.GetValue<double>());
    }
    [Fact]
    public void PlanLimitRefusesBeforeWritingUnboundedArtifacts()
    {
        var data=new SafetyFixture();var store=Store(data);
        for(int i=0;i<128;i++)store.Prepare(data.Sets[0],Parameters());
        var before=TransportCancellationProof.Snapshot(data.Root);
        Assert.Equal("uniform_scale_plan_limit",Assert.Throws<ReferenceRejected>(()=>store.Prepare(data.Sets[0],Parameters())).Code);
        TransportCancellationProof.Unchanged(data.Root,before);
    }
}
