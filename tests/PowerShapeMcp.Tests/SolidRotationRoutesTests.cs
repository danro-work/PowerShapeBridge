using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class SolidRotationRoutesTests
{
    static readonly string[] Names=["prepare_owned_solid_rotation","execute_owned_solid_rotation","verify_owned_solid_rotation"];
    static JsonObject Parameters(int angle=45)=>JsonNode.Parse($$"""{"angleDegrees":{{angle}},"origin":[2,3,4],"direction":[3,4,0],"coordinateFrame":"world","lengthUnits":"document_raw"}""")!.AsObject();
    static JsonObject Value(CallToolResult result)=>JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();
    static async Task<string> Prepared(OwnedRotationRoutesTests.Connection c,bool forgeScope=false)
    {
        var pending=c.Client.CallToolAsync(Names[0],c.Args()).AsTask();var path=await c.Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);var envelope=JsonNode.Parse(await c.Data.Files.ReadClaimedRequest(path))!;
        Assert.Equal(Names[0],envelope["arguments"]!["recipeId"]!.GetValue<string>());var reply=c.Reply(id,envelope["arguments"]!["parameters"]!.AsObject());if(forgeScope)reply["qualificationScope"]=SolidQuarterTurnProof.ObliqueScope;
        c.Data.Files.Write("responses/"+id+".json",reply.ToJsonString());var result=await pending;
        if(forgeScope){Assert.True(result.IsError);Assert.Null(Value(result)["ownedRotationHandle"]);return "";}
        Assert.False(result.IsError,Value(result).ToJsonString());return Value(result)["ownedRotationHandle"]!.GetValue<string>();
    }
    [Theory]
    [InlineData(false,true,true)][InlineData(true,true,false)][InlineData(false,false,false)]
    public async Task SeparateFeatureAndReadOnlyPolicyGateEveryRoute(bool readOnly,bool enabled,bool present)
    {
        await using var c=await OwnedRotationRoutesTests.Connection.Create(readOnly,enabled,true,Parameters(),true);var tools=await c.Client.ListToolsAsync();
        foreach(var name in Names){Assert.Equal(present,tools.Any(t=>t.Name==name));await Assert.ThrowsAsync<McpProtocolException>(()=>c.Client.CallToolAsync(name,new Dictionary<string,object?>{["command"]="NeverRun"}).AsTask());}
        Assert.Equal(present?"experimental_one_owned_solid_world_integer_rotation_mm":"disabled",c.Session.Status()["solidRotations"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(c.Data.Root,"requests")));
    }
    [Theory]
    [InlineData(45)][InlineData(-45)][InlineData(180)][InlineData(-180)][InlineData(359)][InlineData(-359)]
    public async Task IntegerPreparationPinsASeparateScopeAndOneUseLease(int angle)
    {
        await using var c=await OwnedRotationRoutesTests.Connection.Create(quarterTurn:true,quarterTurnParameters:Parameters(angle),generalRotation:true);var handle=await Prepared(c);
        var proof=c.Session.SolidRotations.Consume(handle);var receipt=SolidQuarterTurnProof.Raw(c.Data.Root,proof["ownedRotationId"]!.GetValue<string>()).Value;
        Assert.Equal(SolidQuarterTurnProof.RotationScope,receipt["qualificationScope"]!.GetValue<string>());Assert.Throws<ReferenceRejected>(()=>c.Session.SolidRotations.Consume(handle));Assert.Throws<ReferenceRejected>(()=>c.Session.SolidQuarterTurns.Consume(handle));
        Assert.Single(Directory.GetFiles(Path.Combine(c.Data.Root,"requests")));
    }
    [Theory]
    [InlineData(0)][InlineData(360)][InlineData(-360)][InlineData(1000)]
    public void NewScopeRejectsNoopOrOutOfRangeAngles(int angle)=>Assert.Throws<ReferenceRejected>(()=>SolidQuarterTurnProof.ScopeFor(Parameters(angle),true));
    [Fact]
    public async Task NewPreparationCannotMasqueradeAsQuarterTurn()
    {await using var c=await OwnedRotationRoutesTests.Connection.Create(quarterTurn:true,quarterTurnParameters:Parameters(),generalRotation:true);await Prepared(c,true);}
    [Fact]
    public async Task TimeoutConsumesGeneralIntentAndCannotReplay()
    {
        await using var c=await OwnedRotationRoutesTests.Connection.Create(quarterTurn:true,quarterTurnParameters:Parameters(),generalRotation:true);var handle=await Prepared(c);
        var pending=c.Client.CallToolAsync(Names[1],new Dictionary<string,object?>{["ownedRotationHandle"]=handle}).AsTask();var path=await c.Data.Files.ClaimRequest();var request=JsonNode.Parse(await c.Data.Files.ReadClaimedRequest(path))!;Assert.Equal(Names[1],request["arguments"]!["recipeId"]!.GetValue<string>());
        var result=await pending;Assert.True(result.IsError);Assert.False(Value(result)["retryAllowed"]!.GetValue<bool>());
        var replay=await c.Client.CallToolAsync(Names[1],new Dictionary<string,object?>{["ownedRotationHandle"]=handle});Assert.Equal("solid_rotation_rejected_before_submission",Value(replay)["state"]!.GetValue<string>());Assert.Equal(2,Directory.GetFiles(Path.Combine(c.Data.Root,"requests")).Length);
    }
    [Fact]
    public async Task QuarterTurnLeaseCannotAuthorizeGeneralExecution()
    {
        await using var c=await OwnedRotationRoutesTests.Connection.Create(quarterTurn:true);
        var pending=c.Client.CallToolAsync("prepare_owned_solid_quarter_turn",c.Args()).AsTask();var path=await c.Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);var request=JsonNode.Parse(await c.Data.Files.ReadClaimedRequest(path))!;
        c.Data.Files.Write("responses/"+id+".json",c.Reply(id,request["arguments"]!["parameters"]!.AsObject()).ToJsonString());var prepared=await pending;Assert.False(prepared.IsError);
        var handle=Value(prepared)["ownedRotationHandle"]!.GetValue<string>();Assert.Throws<ReferenceRejected>(()=>c.Session.SolidRotations.Consume(handle));Assert.NotNull(c.Session.SolidQuarterTurns.Consume(handle));Assert.Single(Directory.GetFiles(Path.Combine(c.Data.Root,"requests")));
    }
}
