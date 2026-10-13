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
        internal readonly TransportCancellationProof.Recorder CancellationRecorder=new();
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(30));private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;
        private Connection()
        {
            Fixture.Data.Target.Data.Files.Write("PSBridge.Worker.exe","Opaque marker; never executed.");var input=new Pipe();var output=new Pipe();
            server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..CancellationRecorder.Wrap(ToolCatalog.Create(Fixture.Session))]});run=server.RunAsync(stop.Token);
            transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        internal static async Task<Connection> Create(){var result=new Connection();result.Client=await McpClient.CreateAsync(result.transport,cancellationToken:result.stop.Token);return result;}
        internal async Task<(string Id,JsonObject Parameters)> Claim(string recipe)
        {
            var path=await Fixture.Data.Target.Data.Files.ClaimRequest();var envelope=JsonNode.Parse(await Fixture.Data.Target.Data.Files.ReadClaimedRequest(path))!;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimedTimeoutOrLostReplyRetainsOneRequestAndCannotReplay(bool effectReturned)
    {
        await using var connection=await Connection.Create();
        var args=connection.OpenArgs().ToDictionary(p=>p.Key,p=>(object?)p.Value!.GetValue<string>());
        var call=connection.Client.CallToolAsync("execute_owned_saved_reopen",args).AsTask();
        var (id,parameters)=await connection.Claim("execute_owned_saved_reopen");
        Assert.Throws<ReferenceRejected>(()=>connection.Fixture.Session.Reopens.Parameters(connection.Fixture.Handle));
        if(effectReturned)connection.Fixture.Execution(id,parameters,publish:false);
        var root=connection.Fixture.Root;
        var protectedPaths=Directory.GetFiles(root,"*",SearchOption.AllDirectories)
            .Where(path=>!path.EndsWith(".test-tmp",StringComparison.Ordinal)).ToArray();
        static string Sha(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        var before=protectedPaths.ToDictionary(path=>path,Sha);
        var reply=await call;Assert.True(reply.IsError);
        var result=JsonNode.Parse(reply.StructuredContent!.Value.GetRawText())!;
        Assert.Equal("timeout_outcome_unknown",result["state"]!.GetValue<string>());
        Assert.False(result["retryAllowed"]!.GetValue<bool>());
        Assert.True(result["contextChangeMayHaveOccurred"]!.GetValue<bool>());
        Assert.Null(result["workingDocumentBindingHandle"]);
        Assert.True(File.Exists(FileArtifacts.FixedPath(root,"requests",id+".json.processing")));
        Assert.False(File.Exists(FileArtifacts.FixedPath(root,"responses",id+".json")));
        if(effectReturned)
        {
            Assert.Equal(connection.Fixture.OriginalSha.ToUpperInvariant(),Sha(FileArtifacts.FixedPath(root,"responses",id+".unpublished")));
            Assert.True(File.Exists(FileArtifacts.FixedPath(root,"workingcopies",connection.Fixture.Prepared["reopen"]!["copyId"]!.GetValue<string>(),"open-once.json")));
        }
        var replay=await connection.Client.CallToolAsync("execute_owned_saved_reopen",args);
        Assert.True(replay.IsError);Assert.Single(Directory.GetFiles(Path.Combine(root,"requests")));
        foreach(var pair in before)Assert.Equal(pair.Value,Sha(pair.Key));
        Assert.Equal(protectedPaths.Order(),Directory.GetFiles(root,"*",SearchOption.AllDirectories).Order());
        TransportCancellationProof.Record(root,id,"execute_owned_saved_reopen",effectReturned?"returned_reply_lost":"claimed_timeout",result["state"]!.GetValue<string>(),before);
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task OfficialSdkCancellationPreservesSavedReopenAndCannotReplay(bool effectReturned,bool notify)
    {
        await using var connection=await Connection.Create();
        var args=connection.OpenArgs().ToDictionary(p=>p.Key,p=>(object?)p.Value!.GetValue<string>());
        using var cancellation=new CancellationTokenSource();
        var call=connection.Client.CallToolAsync("execute_owned_saved_reopen",args,cancellationToken:cancellation.Token).AsTask();
        var (id,parameters)=await connection.Claim("execute_owned_saved_reopen");
        if(effectReturned)connection.Fixture.Execution(id,parameters,publish:false);
        await TransportCancellationProof.CancelClaimed(call,cancellation,connection.Fixture.Session,connection.Fixture.Root,id,connection.Client,connection.CancellationRecorder,notify);
        Assert.Throws<ReferenceRejected>(()=>connection.Fixture.Session.Reopens.Parameters(connection.Fixture.Handle));
        if(effectReturned)Assert.True(File.Exists(FileArtifacts.FixedPath(connection.Fixture.Root,"workingcopies",connection.Fixture.Prepared["reopen"]!["copyId"]!.GetValue<string>(),"open-once.json")));
        var before=TransportCancellationProof.Snapshot(connection.Fixture.Root);
        Assert.True((await connection.Client.CallToolAsync("execute_owned_saved_reopen",args)).IsError);
        TransportCancellationProof.Unchanged(connection.Fixture.Root,before);
        TransportCancellationProof.Record(connection.Fixture.Root,id,"execute_owned_saved_reopen",(notify?"cancel_notified_":"cancel_local_")+(effectReturned?"returned":"pending"),"client_wait_cancelled",before);
    }

    [Fact]
    public async Task OfficialSdkLateSavedReopenReceiptDoesNotGrantBindingOrReplay()
    {
        await using var connection=await Connection.Create();var args=connection.OpenArgs().ToDictionary(p=>p.Key,p=>(object?)p.Value!.GetValue<string>());
        var call=connection.Client.CallToolAsync("execute_owned_saved_reopen",args).AsTask();var (id,parameters)=await connection.Claim("execute_owned_saved_reopen");
        connection.Fixture.Execution(id,parameters,publish:false);var root=connection.Fixture.Root;
        var before=TransportCancellationProof.Snapshot(root);var response=await call;var data=JsonNode.Parse(response.StructuredContent!.Value.GetRawText())!;
        Assert.True(response.IsError);Assert.Equal("timeout_outcome_unknown",data["state"]!.GetValue<string>());Assert.Null(data["workingDocumentBindingHandle"]);
        Assert.False(data["retryAllowed"]!.GetValue<bool>());TransportCancellationProof.Unchanged(root,before);LostReplyFixture.OneAuthoritativeReturn(root,id);
        LostReplyFixture.PublishOriginal(root,id);Assert.Equal(connection.Fixture.OriginalSha,FileArtifacts.Sha256(File.ReadAllBytes(FileArtifacts.FixedPath(root,"responses",id+".json"))));
        before=TransportCancellationProof.Snapshot(root);Assert.True((await connection.Client.CallToolAsync("execute_owned_saved_reopen",args)).IsError);
        TransportCancellationProof.Unchanged(root,before);Assert.Single(Directory.GetFiles(Path.Combine(root,"requests")));
        Assert.Equal("timeout_outcome_unknown",data["state"]!.GetValue<string>());Assert.Null(data["workingDocumentBindingHandle"]);
        TransportCancellationProof.Record(root,id,"execute_owned_saved_reopen","late_receipt",data["state"]!.GetValue<string>(),before);
    }
}
