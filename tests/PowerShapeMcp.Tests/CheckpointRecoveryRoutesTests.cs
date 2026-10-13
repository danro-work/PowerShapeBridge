using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class CheckpointRecoveryRoutesTests
{
    private sealed class Connection:IAsyncDisposable
    {
        internal readonly CheckpointRecoveryFixture Data;
        internal readonly TransportCancellationProof.Recorder CancellationRecorder=new();
        internal McpClient Client=null!;private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(40));
        private Connection(bool readOnly,bool enabled)
        {
            Data=new(readOnly,enabled);Data.Files.Write("PSBridge.Worker.exe","Opaque marker; never executed.");
            var input=new Pipe();var output=new Pipe();server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new(){ToolCollection=[..CancellationRecorder.Wrap(ToolCatalog.Create(Data.Session))]});run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        internal static async Task<Connection> Create(bool readOnly=false,bool enabled=true){var result=new Connection(readOnly,enabled);result.Client=await McpClient.CreateAsync(result.transport,cancellationToken:result.stop.Token);return result;}
        internal async Task<(string,JsonObject)> Claim(string recipe)
        {
            var path=await Data.Files.ClaimRequest();var envelope=JsonNode.Parse(await Data.Files.ReadClaimedRequest(path))!;Assert.Equal(recipe,envelope["arguments"]!["recipeId"]!.GetValue<string>());return(Path.GetFileNameWithoutExtension(path),envelope["arguments"]!["parameters"]!.AsObject());
        }
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Data.Session.Dispose();stop.Dispose();}
    }
    private static JsonObject Read(CallToolResult result)=>JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();
    [Theory]
    [InlineData(false,true,2)]
    [InlineData(true,true,0)]
    [InlineData(false,false,0)]
    public async Task CatalogGatesFilesystemWritesAndRejectsArbitraryPathsBeforeQueue(bool readonlyMode,bool enabled,int tools)
    {
        await using var connection=await Connection.Create(readonlyMode,enabled);var catalog=await connection.Client.ListToolsAsync();
        Assert.Equal(tools,catalog.Count(tool=>tool.Name is "prepare_checkpoint_recovery" or "create_checkpoint_working_copy"));
        foreach(var name in new[]{"prepare_checkpoint_recovery","create_checkpoint_working_copy"})
            await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync(name,new Dictionary<string,object?>{["path"]="C:/NeverCopy.psmodel"}).AsTask());
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Files.Root,"requests")));
    }
    [Fact]
    public async Task OfficialSdkMaterializesOnceAndIssuesOnlyOwnedSavedByteHandle()
    {
        await using var connection=await Connection.Create();var handle=connection.Data.Issue();var args=new Dictionary<string,object?>{["checkpointRecoveryHandle"]=handle};
        var call=connection.Client.CallToolAsync("create_checkpoint_working_copy",args).AsTask();var (id,parameters)=await connection.Claim("create_checkpoint_working_copy");
        Assert.Equal(2,parameters.Count);Assert.Equal(connection.Data.Intent,parameters["recoveryId"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>connection.Data.Session.CheckpointRecoveries.Consume(handle));connection.Data.Materialize(id);var reply=await call;
        Assert.False(reply.IsError,Read(reply).ToJsonString());var data=Read(reply);Assert.NotNull(data["workingCopyHandle"]);Assert.Null(data["workingDocumentBindingHandle"]);Assert.False(data["originalOutcomeResolved"]!.GetValue<bool>());
        var before=TransportCancellationProof.Snapshot(connection.Data.Files.Root);Assert.True((await connection.Client.CallToolAsync("create_checkpoint_working_copy",args)).IsError);
        TransportCancellationProof.Unchanged(connection.Data.Files.Root,before);Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Files.Root,"requests")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutAndLostOriginalReplyPreserveConsumedIntentAndArtifacts(bool returned)
    {
        await using var connection=await Connection.Create();var handle=connection.Data.Issue();var args=new Dictionary<string,object?>{["checkpointRecoveryHandle"]=handle};
        var call=connection.Client.CallToolAsync("create_checkpoint_working_copy",args).AsTask();var (id,_)=await connection.Claim("create_checkpoint_working_copy");
        if(returned)connection.Data.Materialize(id,publish:false);var before=TransportCancellationProof.Snapshot(connection.Data.Files.Root);
        var reply=await call;Assert.True(reply.IsError);Assert.Equal("timeout_outcome_unknown",Read(reply)["state"]!.GetValue<string>());Assert.Null(Read(reply)["workingCopyHandle"]);
        Assert.True((await connection.Client.CallToolAsync("create_checkpoint_working_copy",args)).IsError);TransportCancellationProof.Unchanged(connection.Data.Files.Root,before);
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Files.Root,"requests")));Assert.True(File.Exists(FileArtifacts.FixedPath(connection.Data.Files.Root,"requests",id+".json.processing")));
    }
    [Fact]
    public async Task CancellationBeforeSubmissionPreservesUnconsumedIntent()
    {
        await using var connection=await Connection.Create();var handle=connection.Data.Issue();var result=await new CheckpointRecoveryTools(connection.Data.Session).CreateCopy(handle,new CancellationToken(true));
        Assert.Equal("cancelled_before_submission",Read(result)["state"]!.GetValue<string>());Assert.NotNull(connection.Data.Session.CheckpointRecoveries.Consume(handle));Assert.False(Directory.Exists(Path.Combine(connection.Data.Files.Root,"requests")));
    }
    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task CancellationAfterClaimPreservesUnknownArtifactsAndNeverRevivesIntent(bool materialized,bool notify)
    {
        await using var connection=await Connection.Create();var handle=connection.Data.Issue();var args=new Dictionary<string,object?>{["checkpointRecoveryHandle"]=handle};
        using var cancellation=new CancellationTokenSource();
        var call=connection.Client.CallToolAsync("create_checkpoint_working_copy",args,cancellationToken:cancellation.Token).AsTask();
        var (id,_)=await connection.Claim("create_checkpoint_working_copy");
        if(materialized)connection.Data.Materialize(id,publish:false);
        var before=TransportCancellationProof.Snapshot(connection.Data.Files.Root);
        await TransportCancellationProof.CancelClaimed(call,cancellation,connection.Data.Session,connection.Data.Files.Root,id,connection.Client,connection.CancellationRecorder,notify);
        Assert.Throws<ReferenceRejected>(()=>connection.Data.Session.CheckpointRecoveries.Consume(handle));
        var replay=await connection.Client.CallToolAsync("create_checkpoint_working_copy",args);
        Assert.True(replay.IsError);Assert.Null(Read(replay)["workingCopyHandle"]);
        TransportCancellationProof.Unchanged(connection.Data.Files.Root,before);
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Files.Root,"requests")));
        Assert.Equal(materialized,File.Exists(FileArtifacts.FixedPath(connection.Data.Files.Root,"checkpointcopyplans",id+".result.json")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostPrepareReplyCannotGrantIntentOrAutomaticallySubmitAgain(bool prepared)
    {
        await using var connection=await Connection.Create();
        var call=connection.Client.CallToolAsync("prepare_checkpoint_recovery",new Dictionary<string,object?>{["operationId"]=connection.Data.Origin,["operationPlanSha256"]=connection.Data.Parameters["operationPlanSha256"]!.GetValue<string>()}).AsTask();
        var (id,_)=await connection.Claim("prepare_checkpoint_recovery");
        if(prepared)
        {
            var plan=connection.Data.Plan.DeepClone().AsObject();plan["recoveryId"]=id;
            var hash=ArtifactProof.Freeze(connection.Data.Files.Root,"checkpointrecoveries/"+id+".json",plan);
            var proof=connection.Data.Prepared.DeepClone().AsObject();proof["bridgeRequestId"]=id;proof["recoveryId"]=id;proof["recoverySha256"]=hash;proof["recovery"]=plan;
            connection.Data.Files.Write("responses/"+id+".unpublished",proof.ToJsonString());
        }
        var before=TransportCancellationProof.Snapshot(connection.Data.Files.Root);var reply=await call;
        Assert.True(reply.IsError);Assert.Equal("timeout_outcome_unknown",Read(reply)["state"]!.GetValue<string>());
        Assert.Null(Read(reply)["checkpointRecoveryHandle"]);Assert.False(Read(reply)["retryAllowed"]!.GetValue<bool>());
        TransportCancellationProof.Unchanged(connection.Data.Files.Root,before);
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Files.Root,"requests")));
        Assert.False(File.Exists(FileArtifacts.FixedPath(connection.Data.Files.Root,"responses",id+".json")));
    }
}
