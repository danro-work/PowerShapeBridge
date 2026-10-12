using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedSavedReopenRoutesTests
{
    private sealed class Connection:IAsyncDisposable
    {
        internal readonly SavedReopenFixture Fixture=new();
        internal McpClient Client { get; private set; }=null!;
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(30));private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;
        private Connection()
        {
            Fixture.Data.Target.Data.Files.Write("PSBridge.Worker.exe","Opaque marker; never executed.");var input=new Pipe();var output=new Pipe();
            server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(Fixture.Session)]});run=server.RunAsync(stop.Token);
            transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        internal static async Task<Connection> Create(){var result=new Connection();result.Client=await McpClient.CreateAsync(result.transport,cancellationToken:result.stop.Token);return result;}
        internal async Task<(string Id,JsonObject Parameters)> Claim(string recipe)
        {
            var path=await Fixture.Data.Target.Data.Files.ClaimRequest();var envelope=JsonNode.Parse(File.ReadAllText(path+".processing"))!;
            Assert.Equal(recipe,envelope["arguments"]!["recipeId"]!.GetValue<string>());Assert.Equal("recipe.prepare",envelope["operation"]!.GetValue<string>());
            return(Path.GetFileNameWithoutExtension(path),envelope["arguments"]!["parameters"]!.AsObject());
        }
        internal JsonObject OpenArgs()
        {
            var fresh=Fixture.Data.Saved.FreshReads();return new(){["ownedReopenHandle"]=Fixture.Handle,["candidateDocumentHandle"]=fresh["candidateDocumentHandle"]!.DeepClone(),["activeDocumentHandle"]=fresh["activeDocumentHandle"]!.DeepClone()};
        }
        internal async Task Execute()
        {
            var call=Client.CallToolAsync("execute_owned_saved_reopen",OpenArgs().ToDictionary(p=>p.Key,p=>(object?)p.Value!.GetValue<string>())).AsTask();var (id,parameters)=await Claim("execute_owned_saved_reopen");Assert.Equal(9,parameters.Count);
            Fixture.Execution(id,parameters);var reply=await call;Assert.False(reply.IsError,reply.StructuredContent!.Value.GetRawText());
        }
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Fixture.Session.Dispose();stop.Dispose();}
    }
    [Fact]
    public async Task OfficialSdkRoutesExecuteOnceThenIssueFreshBindingWithExactSchemas()
    {
        await using var connection=await Connection.Create();var tools=await connection.Client.ListToolsAsync();
        foreach(var pair in new[]{("execute_owned_saved_reopen",new[]{"ownedReopenHandle","candidateDocumentHandle","activeDocumentHandle"}),("verify_owned_saved_reopen",new[]{"reopenRequestId","reopenReceiptSha256","candidateDocumentHandle","activeDocumentHandle"})})
        {
            var schema=tools.Single(t=>t.Name==pair.Item1).JsonSchema;Assert.Equal(pair.Item2,schema.GetProperty("required").EnumerateArray().Select(n=>n.GetString()).ToArray());Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
            await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync(pair.Item1,new Dictionary<string,object?>{["path"]="C:/Source.psmodel"}).AsTask());
        }
        await connection.Execute();Assert.Throws<ReferenceRejected>(()=>connection.Fixture.Session.Reopens.Parameters(connection.Fixture.Handle));
        var args=connection.Fixture.FreshReads();var call=connection.Client.CallToolAsync("verify_owned_saved_reopen",args.ToDictionary(p=>p.Key,p=>(object?)p.Value!.GetValue<string>())).AsTask();var (id,parameters)=await connection.Claim("verify_owned_saved_reopen");Assert.Equal(9,parameters.Count);
        connection.Fixture.Verification(id,parameters);var reply=await call;Assert.False(reply.IsError,reply.StructuredContent!.Value.GetRawText());
        var result=JsonNode.Parse(reply.StructuredContent.Value.GetRawText())!;var binding=result["workingDocumentBindingHandle"]!.GetValue<string>();
        Assert.NotNull(connection.Fixture.Session.Bindings.ValidationParameters(binding));Assert.Equal(500,result["model"]!["id"]!.GetValue<int>());
        var replay=await connection.Client.CallToolAsync("verify_owned_saved_reopen",args.ToDictionary(p=>p.Key,p=>(object?)p.Value!.GetValue<string>()));Assert.True(replay.IsError);
        Assert.Equal(2,Directory.GetFiles(Path.Combine(connection.Fixture.Root,"requests")).Length);
    }
    [Fact]
    public async Task LostCandidateDuringNativeVerificationNeverIssuesBinding()
    {
        await using var connection=await Connection.Create();await connection.Execute();var args=connection.Fixture.FreshReads();var call=connection.Client.CallToolAsync("verify_owned_saved_reopen",args.ToDictionary(p=>p.Key,p=>(object?)p.Value!.GetValue<string>())).AsTask();
        var (id,parameters)=await connection.Claim("verify_owned_saved_reopen");connection.Fixture.Session.Candidates.Clear();connection.Fixture.Verification(id,parameters);
        var reply=await call;Assert.True(reply.IsError);var result=JsonNode.Parse(reply.StructuredContent!.Value.GetRawText())!;Assert.Null(result["workingDocumentBindingHandle"]);Assert.True(result["verificationMayBePartial"]!.GetValue<bool>());
        Assert.Equal(2,Directory.GetFiles(Path.Combine(connection.Fixture.Root,"requests")).Length);
    }
}
