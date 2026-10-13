using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedSaveExecutionRoutesTests
{
    private sealed class Connection:IAsyncDisposable
    {
        internal OwnedSaveTargetTests.TargetFixture Data { get; }
        internal SavedResultFixture Saved { get; }
        internal McpClient Client { get; private set; }=null!;
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(30));
        private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;
        private Connection(bool readOnly,bool saves)
        {
            Data=new(readOnly,saves:saves);Saved=new(Data);Data.Data.Files.Write("PSBridge.Worker.exe","Opaque fake marker; never executed.");
            var input=new Pipe();var output=new Pipe();server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(Data.Session)]});
            run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        internal static async Task<Connection> Create(bool readOnly=false,bool saves=true){var result=new Connection(readOnly,saves);result.Client=await McpClient.CreateAsync(result.transport,cancellationToken:result.stop.Token);return result;}
        internal async Task<(string Id,JsonObject Parameters)> Claim(string recipe)
        {
            var path=await Data.Data.Files.ClaimRequest();var envelope=JsonNode.Parse(await Data.Data.Files.ReadClaimedRequest(path))!;
            Assert.Equal("recipe.prepare",envelope["operation"]!.GetValue<string>());Assert.Equal(recipe,envelope["arguments"]!["recipeId"]!.GetValue<string>());
            return(Path.GetFileNameWithoutExtension(path),envelope["arguments"]!["parameters"]!.AsObject());
        }
        internal async Task<string> Execute()
        {
            var handle=Data.Issue();var call=Client.CallToolAsync("execute_owned_save_as",new Dictionary<string,object?>{["ownedSaveAsHandle"]=handle}).AsTask();var (id,parameters)=await Claim("execute_owned_save_as");
            Assert.Equal(Data.Id,parameters["targetId"]!.GetValue<string>());Assert.Equal(2,parameters.Count);Saved.Execute(id);var response=await call;Assert.False(response.IsError,Read(response).ToJsonString());return handle;
        }
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Data.Session.Dispose();stop.Dispose();}
    }
    private static JsonObject Read(CallToolResult result)=>JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();
    [Theory]
    [InlineData(false,true,16)]
    [InlineData(false,false,14)]
    [InlineData(true,true,6)]
    public async Task OfficialCatalogSchemaAndReadonlyGateBeforeQueue(bool readOnly,bool saves,int count)
    {
        await using var connection=await Connection.Create(readOnly,saves);Assert.Equal(count,(await connection.Client.ListToolsAsync()).Count);
        foreach(var name in new[]{"execute_owned_save_as","verify_owned_save_as"})
        {
            if(readOnly||!saves)Assert.Throws<McpProtocolException>(()=>connection.Data.Session.AssertToolAllowed(name));
            await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync(name,new Dictionary<string,object?>{["ownedSaveAsHandle"]=true,["path"]="C:/Source.psmodel"}).AsTask());
        }
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Data.Root,"requests")));
    }
    [Fact]
    public async Task ExactlyOneFixedSaveRevokesCadLeasesAndCannotReplayConsumedHandle()
    {
        await using var connection=await Connection.Create();var handle=await connection.Execute();
        Assert.Throws<ReferenceRejected>(()=>connection.Data.Targets.ValidationParameters(handle));
        Assert.Throws<ReferenceRejected>(()=>connection.Data.Bindings.ValidationParameters(connection.Data.Binding));
        var replay=await connection.Client.CallToolAsync("execute_owned_save_as",new Dictionary<string,object?>{["ownedSaveAsHandle"]=handle});Assert.True(replay.IsError);
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Data.Root,"requests")));
        Assert.True(File.Exists(FileArtifacts.FixedPath(connection.Data.Data.Root,"workingcopies",connection.Data.Plan["copyId"]!.GetValue<string>(),"mutation-fence.json")));
    }
    [Fact]
    public async Task VerifyUsesFreshPrivateProofsAndIssuesOnlySavedByteHandle()
    {
        await using var connection=await Connection.Create();await connection.Execute();var input=connection.Saved.FreshReads();
        var call=connection.Client.CallToolAsync("verify_owned_save_as",input.ToDictionary(p=>p.Key,p=>(object?)p.Value!.GetValue<string>())).AsTask();var (id,parameters)=await connection.Claim("verify_owned_save_as");
        Assert.Equal(9,parameters.Count);Assert.Equal(connection.Saved.SaveId,parameters["saveRequestId"]!.GetValue<string>());
        connection.Saved.Verify(id,parameters);var response=await call;Assert.False(response.IsError);var data=Read(response);var handle=data["savedResultHandle"]!.GetValue<string>();
        Assert.Equal(id,connection.Data.Session.SavedResults.Parameters(handle)["verificationRequestId"]!.GetValue<string>());
        Assert.False(data["cadSaveAttempted"]!.GetValue<bool>());Assert.Null(data["workingDocumentBindingHandle"]);
        Assert.Throws<ReferenceRejected>(()=>connection.Data.Session.Documents.Consume(connection.Saved.Choice));
        Assert.Equal(2,Directory.GetFiles(Path.Combine(connection.Data.Data.Root,"requests")).Length);
    }
    [Theory]
    [InlineData("wrong_return")]
    [InlineData("changed_bytes")]
    [InlineData("wrong_plan")]
    public async Task ForgedOrChangedNativeSuccessRemainsUnknownAndNeverRepeatsSave(string fault)
    {
        await using var connection=await Connection.Create();var handle=connection.Data.Issue();var call=connection.Client.CallToolAsync("execute_owned_save_as",new Dictionary<string,object?>{["ownedSaveAsHandle"]=handle}).AsTask();var (id,parameters)=await connection.Claim("execute_owned_save_as");
        var response=connection.Saved.Execute(id,false);
        if(fault=="wrong_return")response["saveCommandReturned"]=false;
        if(fault=="wrong_plan")response["planSha256"]=new string('f',64);
        if(fault=="changed_bytes")File.AppendAllText(connection.Data.Plan["resultPath"]!.GetValue<string>()," changed");
        connection.Saved.PublishExecution(response);var result=Read(await call);
        Assert.Equal("owned_save_as_outcome_unknown",result["state"]!.GetValue<string>());Assert.True(result["cadSaveMayHaveOccurred"]!.GetValue<bool>());Assert.False(result["retryAllowed"]!.GetValue<bool>());
        Assert.Throws<ReferenceRejected>(()=>connection.Data.Targets.ValidationParameters(handle));Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Data.Root,"requests")));
    }
    [Fact]
    public async Task CancellationAfterClaimPreservesProcessingAndConsumedIntent()
    {
        await using var connection=await Connection.Create();var handle=connection.Data.Issue();using var cancel=new CancellationTokenSource();
        var call=new OwnedSaveTools(connection.Data.Session).ExecuteOwnedSaveAs(handle,cancel.Token);var (id,parameters)=await connection.Claim("execute_owned_save_as");cancel.Cancel();var data=Read(await call);
        Assert.Equal("cancelled_outcome_unknown",data["state"]!.GetValue<string>());Assert.True(data["cadSaveMayHaveOccurred"]!.GetValue<bool>());
        Assert.Throws<ReferenceRejected>(()=>connection.Data.Targets.ValidationParameters(handle));Assert.True(File.Exists(FileArtifacts.FixedPath(connection.Data.Data.Root,"requests",id+".json.processing")));
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Data.Root,"requests")));
    }
    [Fact]
    public async Task CancellationBeforeSubmissionAndMaintenancePreserveUnconsumedTarget()
    {
        await using var connection=await Connection.Create();var handle=connection.Data.Issue();using var cancel=new CancellationTokenSource();cancel.Cancel();
        var data=Read(await new OwnedSaveTools(connection.Data.Session).ExecuteOwnedSaveAs(handle,cancel.Token));Assert.Equal("cancelled_before_submission",data["state"]!.GetValue<string>());
        Assert.Equal(connection.Data.Id,connection.Data.Targets.ValidationParameters(handle)["targetId"]!.GetValue<string>());
        connection.Data.Data.Files.Write("maintenance.json","{}");var result=Read(await new OwnedSaveTools(connection.Data.Session).ExecuteOwnedSaveAs(handle,CancellationToken.None));Assert.Equal("maintenance_before_submission",result["state"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Data.Root,"requests")));
    }
}
