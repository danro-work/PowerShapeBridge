using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class PreparedSafetyTests
{
    [Fact]
    public void LocalPlanFreezesSourceRawUnitsAndOnlyPreparedJournal()
    {
        var fixture=new SafetyFixture();var prepared=fixture.Plans.Prepare(fixture.Args());var id=prepared["operationId"]!.GetValue<string>();
        var inspected=fixture.Plans.Inspect(prepared["operationPlanHandle"]!.GetValue<string>());
        Assert.False(prepared["plan"]!["executionEnabled"]!.GetValue<bool>());Assert.False(inspected["journal"]!["retryAllowed"]!.GetValue<bool>());
        Assert.Equal("prepared",inspected["journal"]!["recordedState"]!.GetValue<string>());Assert.Equal(1,inspected["journal"]!["eventCount"]!.GetValue<int>());
        Assert.Equal("Python JSON sorted keys; distinct from native C# digest",prepared["plan"]!["observationDigestEncoding"]!.GetValue<string>());
        Assert.Equal(FileArtifacts.Sha256(File.ReadAllBytes(Path.Combine(fixture.Root,"operationplans",id+".json"))),prepared["planSha256"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
    }
    [Theory]
    [InlineData("bool")]
    [InlineData("zero")]
    [InlineData("large")]
    [InlineData("nan")]
    [InlineData("command")]
    public void InvalidTranslationParametersNeverCreateFiles(string fault)
    {
        var fixture=new SafetyFixture();var args=fixture.Args();var parameters=args["parameters"]!.AsObject();
        switch(fault)
        {
            case "bool":parameters["vector"]=new JsonArray(true,0,0);break;
            case "zero":parameters["vector"]=new JsonArray(0,0,0);break;
            case "large":parameters["vector"]=new JsonArray(1000001,0,0);break;
            case "nan":parameters["vector"]=JsonNode.Parse("[1e999,0,0]");break;
            case "command":parameters["command"]="DELETE";break;
        }
        Assert.Throws<ReferenceRejected>(()=>fixture.Plans.Prepare(args));Assert.False(Directory.Exists(Path.Combine(fixture.Root,"operationplans")));
    }
    [Fact]
    public void IntersectionRolesAndOverlapAreCheckedBeforePreparation()
    {
        var fixture=new SafetyFixture();var good=fixture.Plans.Prepare(fixture.Args("intersect_entity_sets"));Assert.False(good["plan"]!["executionEnabled"]!.GetValue<bool>());
        Assert.Equal("intersection_set_type_unsupported",Assert.Throws<ReferenceRejected>(()=>fixture.Plans.Prepare(fixture.Args("intersect_entity_sets",[fixture.Sets[2],fixture.Sets[1]]))).Code);
        var duplicate=fixture.MakeSet(new JsonArray(new JsonArray(21,8)));
        Assert.Equal("intersection_sets_overlap",Assert.Throws<ReferenceRejected>(()=>fixture.Plans.Prepare(fixture.Args("intersect_entity_sets",[fixture.Sets[1],duplicate]))).Code);
    }
    [Theory]
    [InlineData("receipt")]
    [InlineData("set")]
    [InlineData("plan")]
    public void ChangedProofBytesAreRejectedWithoutCad(string artifact)
    {
        var fixture=new SafetyFixture();var prepared=fixture.Plans.Prepare(fixture.Args());var handle=prepared["operationPlanHandle"]!.GetValue<string>();
        var path=artifact=="receipt"?Path.Combine(fixture.Root,"responses",fixture.SourceId+".json"):artifact=="set"?Path.Combine(fixture.Root,"entitysets",fixture.Candidates.SetParameters(fixture.Sets[0])["entitySetId"]!.GetValue<string>()+".json"):
            Path.Combine(fixture.Root,"operationplans",prepared["operationId"]!.GetValue<string>()+".json");
        File.AppendAllText(path," ");
        Assert.Equal("operation_artifact_changed",Assert.Throws<ReferenceRejected>(()=>fixture.Plans.Inspect(handle)).Code);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
    }
    [Theory]
    [InlineData("expiry")]
    [InlineData("context")]
    [InlineData("sets")]
    public void PlanHandleCannotOutliveItsDependencies(string change)
    {
        var fixture=new SafetyFixture();var prepared=fixture.Plans.Prepare(fixture.Args());var handle=prepared["operationPlanHandle"]!.GetValue<string>();
        if(change=="expiry")fixture.Time=400;
        else if(change=="sets")fixture.Candidates.Clear();
        else{var source=fixture.Source.DeepClone().AsObject();source["model"]!["id"]=99;fixture.Candidates.Observe(source);}
        Assert.Throws<ReferenceRejected>(()=>fixture.Plans.Inspect(handle));
    }
    [Fact]
    public void BindingAndPacketRemainPassiveAndVerificationDoesNotAppend()
    {
        var fixture=new SafetyFixture();var packet=fixture.Packet();var args=fixture.Owned.ValidationParameters(packet.Handle);
        var id=packet.Receipt["ownedOperationId"]!.GetValue<string>();var path=Path.Combine(fixture.Root,"operations",id,"000000.json");var before=File.ReadAllBytes(path);
        fixture.Owned.CheckedValidation(packet.Receipt,args);
        Assert.Equal(before,File.ReadAllBytes(path));Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.json"));
        Assert.False(packet.Receipt["ownedOperation"]!["executionEnabled"]!.GetValue<bool>());Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
    }
    [Theory]
    [InlineData("copies")]
    [InlineData("candidates")]
    [InlineData("plans")]
    [InlineData("bindings")]
    [InlineData("expiry")]
    public void PacketCannotSurviveDependencyLoss(string lost)
    {
        var fixture=new SafetyFixture();var packet=fixture.Packet();
        switch(lost){case "copies":fixture.Copies.Clear();break;case "candidates":fixture.Candidates.Clear();break;case "plans":fixture.Plans.Clear();break;case "bindings":fixture.Bindings.Clear();break;case "expiry":fixture.Time=400;break;}
        Assert.Throws<ReferenceRejected>(()=>fixture.Owned.ValidationParameters(packet.Handle));
    }
    [Theory]
    [InlineData("schema_bool")]
    [InlineData("enabled")]
    [InlineData("extra")]
    [InlineData("save_flag")]
    [InlineData("writer")]
    public void MalformedNativePacketNeverBecomesExecutable(string fault)
    {
        var fixture=new SafetyFixture();var packet=fixture.Packet();var receipt=packet.Receipt.DeepClone().AsObject();var value=receipt["ownedOperation"]!.AsObject();var id=receipt["ownedOperationId"]!.GetValue<string>();
        if(fault=="schema_bool")value["schemaVersion"]=true;
        if(fault=="enabled")value["executionEnabled"]=true;
        if(fault=="extra")value["command"]="DELETE";
        if(fault=="save_flag")receipt["cadSaveAttempted"]=true;
        if(fault=="writer")fixture.Files.Write("operations/"+id+"/writer.lock","interrupted");
        if(fault is "schema_bool" or "enabled" or "extra")
        {
            var path=Path.Combine(fixture.Root,"ownedoperations",id+".json");File.WriteAllText(path,value.ToJsonString());receipt["ownedOperationSha256"]=FileArtifacts.Sha256(File.ReadAllBytes(path));
        }
        Assert.Throws<ReferenceRejected>(()=>fixture.Owned.Receipt(receipt));
    }
    [Fact]
    public void RepeatedPreparedJournalLeavesAnExplicitInterruptedMarker()
    {
        var fixture=new Fixture();var id=new string('a',32);var sha=new string('b',64);var timestamp=DateTimeOffset.UtcNow;
        PreparedJournal.Create(fixture.Root,id,sha,timestamp,new(){["executionEnabled"]=false});
        Assert.Throws<ReferenceRejected>(()=>PreparedJournal.Create(fixture.Root,id,sha,timestamp,new(){["executionEnabled"]=false}));
        Assert.True(File.Exists(Path.Combine(fixture.Root,"operations",id,"writer.lock")));
        Assert.Equal("outcome_unknown",ArtifactProof.Journal(fixture.Root,id)["state"]!.GetValue<string>());
    }
}
