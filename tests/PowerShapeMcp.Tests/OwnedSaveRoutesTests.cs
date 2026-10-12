using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedSaveRoutesTests
{
    private sealed class Connection:IAsyncDisposable
    {
        public OwnedSaveTargetTests.TargetFixture Data { get; }
        public McpClient Client { get; private set; }=null!;
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(30));
        private readonly McpServer server;
        private readonly Task run;
        private readonly StreamClientTransport transport;
        private Connection(bool readOnly,bool enabled)
        {
            Data=new(readOnly,enabled);Data.Data.Files.Write("PSBridge.Worker.exe","Opaque fake marker; never executed.");
            var input=new Pipe();var output=new Pipe();server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(Data.Session)]});
            run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        public static async Task<Connection> Create(bool readOnly=false,bool enabled=true)
        {var result=new Connection(readOnly,enabled);result.Client=await McpClient.CreateAsync(result.transport,cancellationToken:result.stop.Token);return result;}
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Data.Session.Dispose();stop.Dispose();}
        public async Task<string> Claim()
        {
            var request=await Data.Data.Files.ClaimRequest();var envelope=JsonNode.Parse(File.ReadAllText(request+".processing"))!;
            Assert.Equal("recipe.prepare",envelope["operation"]!.GetValue<string>());Assert.Equal("prepare_owned_save_as",envelope["arguments"]!["recipeId"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(Data.Parameters,envelope["arguments"]!["parameters"]));return Path.GetFileNameWithoutExtension(request);
        }
        public void Reply(string id,bool forged=false)
        {
            var plan=Data.Plan.DeepClone().AsObject();plan["targetId"]=id;plan["resultPath"]=FileArtifacts.FixedPath(Data.Data.Root,"savedresults",id,"Result_"+id+".psmodel");
            if(forged)plan["executionEnabled"]=true;
            var sha=ArtifactProof.Freeze(Data.Data.Root,"savedresults/"+id+"/target.json",plan);
            var reply=Data.Receipt.DeepClone().AsObject();reply["targetId"]=id;reply["targetSha256"]=sha;reply["target"]=plan;reply["bridgeRequestId"]=id;
            Data.Data.Files.Write("responses/"+id+".json",reply.ToJsonString());
        }
    }
    private static JsonObject Read(CallToolResult result)=>JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();

    [Theory]
    [InlineData(false,true,14)]
    [InlineData(false,false,13)]
    [InlineData(true,true,6)]
    public async Task ExactFlagAndReadonlyCatalogGateRejectBeforeQueue(bool readOnly,bool enabled,int count)
    {
        await using var connection=await Connection.Create(readOnly,enabled);var tools=await connection.Client.ListToolsAsync();Assert.Equal(count,tools.Count);
        foreach(var args in new[]{new Dictionary<string,object?>(),new(){["workingDocumentBindingHandle"]=true},new(){["workingDocumentBindingHandle"]=connection.Data.Binding,["path"]="C:/Source.psmodel"}})
            await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("prepare_owned_save_as",args).AsTask());
        if(readOnly||!enabled)Assert.Throws<McpProtocolException>(()=>connection.Data.Session.AssertToolAllowed("prepare_owned_save_as"));
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Data.Root,"requests")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareUsesFixedBindingProofAndNeverCallsSave(bool forged)
    {
        await using var connection=await Connection.Create();var call=connection.Client.CallToolAsync("prepare_owned_save_as",new Dictionary<string,object?>{["workingDocumentBindingHandle"]=connection.Data.Binding}).AsTask();
        var id=await connection.Claim();connection.Reply(id,forged);var reply=await call;Assert.Equal(forged,reply.IsError);var value=Read(reply);
        Assert.False(value["cadSaveAttempted"]!.GetValue<bool>());Assert.False(value["contextChangeAttempted"]!.GetValue<bool>());
        if(!forged)
        {
            Assert.Equal("owned_save_target_prepared_files",value["state"]!.GetValue<string>());
            var handle=value["ownedSaveAsHandle"]!.GetValue<string>();Assert.Equal(id,connection.Data.Targets.ValidationParameters(handle)["targetId"]!.GetValue<string>());
            Assert.False(File.Exists(value["target"]!["resultPath"]!.GetValue<string>()));
        }
        else Assert.Equal("owned_save_target_rejected",value["state"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Data.Root,"requests")));
    }

    [Fact]
    public async Task BindingLostDuringNativePrepareCannotReturnTargetHandle()
    {
        await using var connection=await Connection.Create();var call=connection.Client.CallToolAsync("prepare_owned_save_as",new Dictionary<string,object?>{["workingDocumentBindingHandle"]=connection.Data.Binding}).AsTask();
        var id=await connection.Claim();connection.Data.Bindings.Clear();connection.Reply(id);
        var received=await call;Assert.True(received.IsError);Assert.Null(Read(received)["ownedSaveAsHandle"]);
    }

    [Fact]
    public async Task CancellationAfterClaimLeavesArtifactsUnknownWithoutSaveOrReplay()
    {
        await using var connection=await Connection.Create();using var cancellation=new CancellationTokenSource();
        var call=new OwnedSaveTools(connection.Data.Session).PrepareOwnedSaveAs(connection.Data.Binding,cancellation.Token);
        var id=await connection.Claim();cancellation.Cancel();var data=Read(await call);
        Assert.Equal("cancelled_outcome_unknown",data["state"]!.GetValue<string>());Assert.True(data["localArtifactsMayExist"]!.GetValue<bool>());
        Assert.False(data["cadSaveAttempted"]!.GetValue<bool>());Assert.False(data["retryAllowed"]!.GetValue<bool>());
        Assert.True(File.Exists(Path.Combine(connection.Data.Data.Root,"requests",id+".json.processing")));Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Data.Root,"requests")));
    }
}
