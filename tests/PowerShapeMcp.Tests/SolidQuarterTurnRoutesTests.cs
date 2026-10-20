using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class SolidQuarterTurnRoutesTests
{
    static readonly string[] Names=["prepare_owned_solid_quarter_turn","execute_owned_solid_quarter_turn","verify_owned_solid_quarter_turn"];
    static JsonObject Value(CallToolResult reply)=>JsonNode.Parse(reply.StructuredContent!.Value.GetRawText())!.AsObject();
    static async Task<string> Prepare(OwnedRotationRoutesTests.Connection c,bool tamper=false)
    {
        var pending=c.Client.CallToolAsync(Names[0],c.Args()).AsTask();var path=await c.Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);var request=JsonNode.Parse(await c.Data.Files.ReadClaimedRequest(path))!;
        Assert.Equal(Names[0],request["arguments"]!["recipeId"]!.GetValue<string>());var args=request["arguments"]!["parameters"]!.AsObject();Assert.Equal(4,args.Count);
        var native=c.Reply(id,args);if(tamper)File.AppendAllText(Path.Combine(c.Data.Root,"solidrotationdescriptors",id+".json")," ");
        c.Data.Files.Write("responses/"+id+".json",native.ToJsonString());var reply=await pending;
        if(tamper){Assert.True(reply.IsError);Assert.Null(Value(reply)["ownedRotationHandle"]);return "";}
        Assert.False(reply.IsError,Value(reply).ToJsonString());return Value(reply)["ownedRotationHandle"]!.GetValue<string>();
    }
    [Theory]
    [InlineData(false,true,true)]
    [InlineData(true,true,false)]
    [InlineData(false,false,false)]
    public async Task CatalogAndDirectCallsEnforceServerReadOnlyAndFeatureFlag(bool readOnly,bool enabled,bool present)
    {
        await using var c=await OwnedRotationRoutesTests.Connection.Create(readOnly,enabled,true);var tools=await c.Client.ListToolsAsync();
        Assert.Equal(present?"experimental_one_owned_solid_world_z_plus90_mm":"disabled",c.Session.Status()["solidQuarterTurns"]!.GetValue<string>());
        foreach(var name in Names){Assert.Equal(present,tools.Any(t=>t.Name==name));await Assert.ThrowsAsync<McpProtocolException>(()=>c.Client.CallToolAsync(name,new Dictionary<string,object?>{["command"]="NeverRun"}).AsTask());}
        Assert.False(Directory.Exists(Path.Combine(c.Data.Root,"requests")));
    }
    [Fact]
    public async Task PreparedHandlePinsDescriptorAndOriginalReceiptWithoutExecuting()
    {
        await using var c=await OwnedRotationRoutesTests.Connection.Create(quarterTurn:true);var handle=await Prepare(c);Assert.NotNull(c.Session.OwnedRotations.ValidationParameters(handle));Assert.Single(Directory.GetFiles(Path.Combine(c.Data.Root,"requests")));
    }
    [Fact]
    public async Task ChangedDescriptorCannotIssuePreparedHandle()
    {await using var c=await OwnedRotationRoutesTests.Connection.Create(quarterTurn:true);await Prepare(c,true);Assert.Single(Directory.GetFiles(Path.Combine(c.Data.Root,"requests")));}
    [Fact]
    public async Task TimeoutConsumesIntentAndCannotQueueSecondRotation()
    {
        await using var c=await OwnedRotationRoutesTests.Connection.Create(quarterTurn:true);var handle=await Prepare(c);
        var pending=c.Client.CallToolAsync(Names[1],new Dictionary<string,object?>{["ownedRotationHandle"]=handle}).AsTask();var path=await c.Data.Files.ClaimRequest();var request=JsonNode.Parse(await c.Data.Files.ReadClaimedRequest(path))!;
        Assert.Equal(Names[1],request["arguments"]!["recipeId"]!.GetValue<string>());var args=request["arguments"]!["parameters"]!.AsObject();Assert.Equal(4,args.Count);Assert.Equal(64,args["preparationReceiptSha256"]!.GetValue<string>().Length);Assert.Equal(64,args["solidWitnessBundleSha256"]!.GetValue<string>().Length);
        var reply=await pending;Assert.True(reply.IsError);Assert.False(Value(reply)["retryAllowed"]!.GetValue<bool>());
        Assert.Throws<ReferenceRejected>(()=>c.Session.OwnedRotations.ValidationParameters(handle));
        var replay=await c.Client.CallToolAsync(Names[1],new Dictionary<string,object?>{["ownedRotationHandle"]=handle});Assert.True(replay.IsError);Assert.Equal("solid_quarter_turn_rejected_before_submission",Value(replay)["state"]!.GetValue<string>());Assert.Equal(2,Directory.GetFiles(Path.Combine(c.Data.Root,"requests")).Length);
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("foreign")]
    [InlineData("descriptor")]
    [InlineData("receipt")]
    [InlineData("coherent_receipt_and_bundle")]
    public async Task InvalidatedPreparationNeverQueuesAnExecution(string fault)
    {
        await using var c=await OwnedRotationRoutesTests.Connection.Create(quarterTurn:true);var handle=await Prepare(c);var args=c.Session.OwnedRotations.ValidationParameters(handle);var id=args["ownedRotationId"]!.GetValue<string>();
        if(fault=="expired")c.Data.Time+=301;
        if(fault=="foreign")handle=Guid.NewGuid().ToString("N");
        if(fault=="descriptor")File.AppendAllText(Path.Combine(c.Data.Root,"solidrotationdescriptors",id+".json")," ");
        if(fault=="coherent_receipt_and_bundle"){var bundlePath=Path.Combine(c.Data.Root,"solidrotationdescriptors",id+".json");var nativePath=Path.Combine(c.Data.Root,"responses",id+".json");var bundle=JsonNode.Parse(File.ReadAllText(bundlePath))!.AsObject();bundle["descriptor"]=new JsonObject{["changed"]=true};File.WriteAllText(bundlePath,bundle.ToJsonString());var receipt=JsonNode.Parse(File.ReadAllText(nativePath))!.AsObject();receipt["solidWitnessBundleSha256"]=FileArtifacts.Sha256(File.ReadAllBytes(bundlePath));File.WriteAllText(nativePath,receipt.ToJsonString());}
        if(fault=="receipt")File.AppendAllText(Path.Combine(c.Data.Root,"responses",id+".json"),"x");
        var result=await c.Client.CallToolAsync(Names[1],new Dictionary<string,object?>{["ownedRotationHandle"]=handle});Assert.True(result.IsError);Assert.Single(Directory.GetFiles(Path.Combine(c.Data.Root,"requests")));
    }
    [Fact]
    public async Task MissingOriginalVerificationProofDoesNotConsumeFreshCadChoicesOrQueue()
    {
        await using var c=await OwnedRotationRoutesTests.Connection.Create(quarterTurn:true);
        var result=await c.Client.CallToolAsync(Names[2],new Dictionary<string,object?>{["executionRequestId"]=Guid.NewGuid().ToString("N"),["executionReceiptSha256"]=new string('a',64),["candidateDocumentHandle"]=Guid.NewGuid().ToString("N"),["activeDocumentHandle"]=Guid.NewGuid().ToString("N")});
        Assert.True(result.IsError);Assert.False(Directory.Exists(Path.Combine(c.Data.Root,"requests")));
    }
}
