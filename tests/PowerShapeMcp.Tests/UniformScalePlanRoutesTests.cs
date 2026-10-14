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

public sealed class UniformScalePlanRoutesTests
{
    private static JsonObject Parameters()=>JsonNode.Parse("""{"factor":2,"centre":[10,20,30],"coordinateFrame":"world","lengthUnits":"document_raw","mode":"scale_in_place"}""")!.AsObject();
    private sealed class Connection:IAsyncDisposable
    {
        internal readonly SafetyFixture Data=new();internal readonly BridgeSession Session;internal readonly string Set;
        internal McpClient Client=null!;private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(45));
        private Connection(bool readonlyMode,bool enabled)
        {
            foreach(var flag in new[]{"entitySetsExperimental","operationPlansExperimental"})Data.Files.Config[flag]=true;
            Data.Files.Config["uniformScalePlansExperimental"]=enabled;Data.Files.SaveConfig();
            Session=new(Data.Files.Options(readonlyMode,5),()=>Data.Time,()=>Data.Utc);Session.Candidates.Issue(Data.Source,Data.SourceSha);
            var native=Data.Candidates.SetParameters(Data.Sets[0]);
            Set=Session.Candidates.IssueSet(new(){["state"]="entity_set_prepared_observed",["entitySetId"]=native["entitySetId"]!.DeepClone(),["entitySetSha256"]=native["entitySetSha256"]!.DeepClone(),["expiresUtc"]=LeaseTime.Format(Data.Utc.AddSeconds(300))})["entitySetHandle"]!.GetValue<string>();
            var input=new Pipe();var output=new Pipe();server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new(){ToolCollection=[..ToolCatalog.Create(Session)]});
            run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        internal static async Task<Connection> Create(bool readonlyMode=false,bool enabled=true){var value=new Connection(readonlyMode,enabled);value.Client=await McpClient.CreateAsync(value.transport,cancellationToken:value.stop.Token);return value;}
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Session.Dispose();stop.Dispose();}
    }
    [Theory][InlineData(false,true,true)][InlineData(true,true,false)][InlineData(false,false,false)]
    public async Task ToolRequiresExplicitFlagAndIsUnavailableInReadonly(bool readonlyMode,bool enabled,bool present)
    {
        await using var connection=await Connection.Create(readonlyMode,enabled);var tools=await connection.Client.ListToolsAsync();
        Assert.Equal(present,tools.Any(t=>t.Name=="prepare_uniform_scale_plan"));
        Assert.DoesNotContain(tools,t=>t.Name is "execute_uniform_scale" or "execute_scale_plan");
        if(!present)await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("prepare_uniform_scale_plan",
            new Dictionary<string,object?>{{"entitySetHandle",connection.Set},{"parameters",Parameters()}}).AsTask());
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"scaleplans")));Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }
    [Fact]
    public async Task OfficialSdkCallCreatesOnlyLocalPlanAndContextChangeRevokesIt()
    {
        await using var connection=await Connection.Create();var tools=await connection.Client.ListToolsAsync();var tool=tools.Single(t=>t.Name=="prepare_uniform_scale_plan");
        Assert.False(tool.JsonSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.False(tool.JsonSchema.GetProperty("properties").GetProperty("parameters").GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("number",tool.JsonSchema.GetProperty("properties").GetProperty("parameters").GetProperty("properties").GetProperty("factor").GetProperty("type").GetString());
        var response=await connection.Client.CallToolAsync("prepare_uniform_scale_plan",new Dictionary<string,object?>{{"entitySetHandle",connection.Set},{"parameters",Parameters()}});
        Assert.False(response.IsError);var result=JsonNode.Parse(response.StructuredContent!.Value.GetRawText())!;var handle=result["scalePlanHandle"]!.GetValue<string>();
        Assert.False(connection.Session.ScalePlans.Inspect(handle)["executionEnabled"]!.GetValue<bool>());
        connection.Session.RevokeCadLeases(false);Assert.Throws<ReferenceRejected>(()=>connection.Session.ScalePlans.Inspect(handle));
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }
    [Theory][InlineData("extra")][InlineData("boolean")][InlineData("negative")][InlineData("noop")][InlineData("short_centre")]
    public async Task StrictWireRejectsInvalidParametersBeforeFilesOrQueue(string fault)
    {
        await using var connection=await Connection.Create();var args=Parameters();
        if(fault=="extra")args["command"]="NeverExecute";
        if(fault=="boolean")args["factor"]=true;
        if(fault=="negative")args["factor"]=-2;
        if(fault=="noop")args["factor"]=1;
        if(fault=="short_centre")args["centre"]=new JsonArray(0,0);
        await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("prepare_uniform_scale_plan",new Dictionary<string,object?>{{"entitySetHandle",connection.Set},{"parameters",args}}).AsTask());
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"scaleplans")));Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }
    [Fact]
    public async Task CancellationBeforePreparationKeepsAllFilesUntouched()
    {
        await using var connection=await Connection.Create();var before=TransportCancellationProof.Snapshot(connection.Data.Root);
        var response=await new UniformScalePlanTools(connection.Session).Prepare(connection.Set,JsonSerializer.SerializeToElement(Parameters()),new CancellationToken(true));
        Assert.True(response.IsError);TransportCancellationProof.Unchanged(connection.Data.Root,before);
    }
    [Theory][InlineData("missing_dependency")][InlineData("non_boolean")]
    public void InvalidConfigurationCannotEnableScale(string fault)
    {
        var data=new SafetyFixture();data.Files.Config["uniformScalePlansExperimental"]=true;
        if(fault=="missing_dependency"){data.Files.Config["operationPlansExperimental"]=false;}
        else {data.Files.Config["uniformScalePlansExperimental"]="true";}
        data.Files.SaveConfig();Assert.Throws<InvalidDataException>(()=>new BridgeSession(data.Files.Options()));
    }
}
