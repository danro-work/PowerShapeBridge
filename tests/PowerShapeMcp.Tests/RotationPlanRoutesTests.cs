using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class RotationPlanRoutesTests
{
    private sealed class Connection:IAsyncDisposable
    {
        internal readonly SafetyFixture Data=new();internal readonly BridgeSession Session;internal readonly string Set;
        internal McpClient Client=null!;private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(30));
        private Connection(bool readonlyMode,bool enabled)
        {
            foreach(var flag in new[]{"entitySetsExperimental","operationPlansExperimental"})Data.Files.Config[flag]=true;
            Data.Files.Config["rotationPlansExperimental"]=enabled;Data.Files.SaveConfig();
            Session=new(Data.Files.Options(readonlyMode,5),()=>Data.Time,()=>Data.Utc);Session.Candidates.Issue(Data.Source,Data.SourceSha);
            var native=Data.Candidates.SetParameters(Data.Sets[0]);
            Set=Session.Candidates.IssueSet(new(){["state"]="entity_set_prepared_observed",["entitySetId"]=native["entitySetId"]!.DeepClone(),["entitySetSha256"]=native["entitySetSha256"]!.DeepClone(),["expiresUtc"]=LeaseTime.Format(Data.Utc.AddSeconds(300))})["entitySetHandle"]!.GetValue<string>();
            var input=new Pipe();var output=new Pipe();server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new(){ToolCollection=[..ToolCatalog.Create(Session)]});run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        internal static async Task<Connection> Create(bool readonlyMode=false,bool enabled=true){var value=new Connection(readonlyMode,enabled);value.Client=await McpClient.CreateAsync(value.transport,cancellationToken:value.stop.Token);return value;}
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Session.Dispose();stop.Dispose();}
    }
    [Theory]
    [InlineData(false,true,true)]
    [InlineData(true,true,false)]
    [InlineData(false,false,false)]
    public async Task CatalogRequiresExplicitFlagAndKeepsReadonlySeparate(bool readonlyMode,bool enabled,bool present)
    {
        await using var connection=await Connection.Create(readonlyMode,enabled);var tools=await connection.Client.ListToolsAsync();
        Assert.Equal(present,tools.Any(tool=>tool.Name=="prepare_rotation_plan"));
        Assert.DoesNotContain(tools,tool=>tool.Name is "execute_rotation" or "execute_rotation_plan");
        if(!present)await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("prepare_rotation_plan",new Dictionary<string,object?>{["entitySetHandle"]=connection.Set,["parameters"]=RotationPlanTests.Parameters()}).AsTask());
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"rotationplans")));Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }
    [Fact]
    public async Task OfficialSdkRouteWritesOnlyPassivePlanAndRevokesItOnCadContextChange()
    {
        await using var connection=await Connection.Create();var tools=await connection.Client.ListToolsAsync();var tool=tools.Single(tool=>tool.Name=="prepare_rotation_plan");
        Assert.False(tool.JsonSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("integer",tool.JsonSchema.GetProperty("properties").GetProperty("parameters").GetProperty("properties").GetProperty("angleDegrees").GetProperty("type").GetString());
        var response=await connection.Client.CallToolAsync("prepare_rotation_plan",new Dictionary<string,object?>{["entitySetHandle"]=connection.Set,["parameters"]=RotationPlanTests.Parameters()});
        Assert.False(response.IsError);var result=JsonNode.Parse(response.StructuredContent!.Value.GetRawText())!;
        var handle=result["rotationPlanHandle"]!.GetValue<string>();Assert.False(connection.Session.RotationPlans.Inspect(handle)["executionEnabled"]!.GetValue<bool>());
        connection.Session.RevokeCadLeases(false);Assert.Throws<ReferenceRejected>(()=>connection.Session.RotationPlans.Inspect(handle));
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }
    [Theory]
    [InlineData("fractional")]
    [InlineData("extra")]
    public async Task StrictWireSchemaRejectsFractionalAnglesAndCommandsBeforeAnyWrite(string fault)
    {
        await using var connection=await Connection.Create();var args=RotationPlanTests.Parameters();if(fault=="fractional")args["angleDegrees"]=0.5;else args["command"]="NeverExecute";
        await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("prepare_rotation_plan",new Dictionary<string,object?>{["entitySetHandle"]=connection.Set,["parameters"]=args}).AsTask());
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"rotationplans")));Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }
    [Fact]
    public async Task CancellationBeforePreparationKeepsFilesUntouched()
    {
        await using var connection=await Connection.Create();var snapshot=TransportCancellationProof.Snapshot(connection.Data.Root);
        var response=await new RotationPlanTools(connection.Session).Prepare(connection.Set,JsonSerializer.SerializeToElement(RotationPlanTests.Parameters()),new CancellationToken(true));
        Assert.True(response.IsError);TransportCancellationProof.Unchanged(connection.Data.Root,snapshot);
    }
}
