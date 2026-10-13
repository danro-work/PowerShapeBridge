using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class ExecutionRoutesTests
{
    private sealed class Connection:IAsyncDisposable
    {
        public SafetyFixture Data { get; }=new();
        public BridgeSession Session { get; }
        public McpClient Client { get; private set; }=null!;
        public string Document { get; }
        public string Copy { get; }
        public string Owned { get; }
        internal readonly TransportCancellationProof.Recorder CancellationRecorder=new();
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(30));
        private readonly McpServer server;
        private readonly Task run;
        private readonly StreamClientTransport transport;
        private Connection(bool readOnly,bool open,bool execute,int timeout=1)
        {
            foreach(var flag in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedOperationPlansExperimental"})Data.Files.Config[flag]=true;
            Data.Files.Config["workingCopyOpenExperimental"]=open;Data.Files.Config["ownedTranslationsExperimental"]=execute;Data.Files.SaveConfig();
            Data.Files.Write("PSBridge.Worker.exe","Fake marker; never executed.");Session=new(Data.Files.Options(readOnly,timeout),()=>Data.Time,()=>Data.Utc);
            Document=Session.Candidates.Issue(Data.Source,Data.SourceSha)["candidateDocumentHandle"]!.GetValue<string>();
            var copyId=Data.BindingReceipt["binding"]!["copyId"]!.GetValue<string>();
            Copy=Session.Copies.Issue(new(){["state"]="working_copy_prepared_disk",["copyId"]=copyId,["bridgeRequestId"]=copyId,["ownershipSha256"]=new string('d',64),["timestampUtc"]=LeaseTime.Format(Data.Utc)})["workingCopyHandle"]!.GetValue<string>();
            var binding=Session.Bindings.Issue(Data.BindingReceipt,Session.Bindings.PrepareParameters(Document,Copy),Document,Copy)["workingDocumentBindingHandle"]!.GetValue<string>();
            var setParams=Data.Candidates.SetParameters(Data.Sets[0]);
            var set=Session.Candidates.IssueSet(new(){["state"]="entity_set_prepared_observed",["entitySetId"]=setParams["entitySetId"]!.DeepClone(),["entitySetSha256"]=setParams["entitySetSha256"]!.DeepClone(),["expiresUtc"]=LeaseTime.Format(Data.Utc.AddSeconds(300))})["entitySetHandle"]!.GetValue<string>();
            var plan=Session.Plans.Prepare(Data.Args(handles:[set]));var planHandle=plan["operationPlanHandle"]!.GetValue<string>();
            var parameters=Session.Owned.PrepareParameters(planHandle,binding);var packet=parameters.DeepClone().AsObject();var id=Guid.NewGuid().ToString("N");
            packet["schemaVersion"]=1;packet["ownedOperationId"]=id;packet["preparedUtc"]=LeaseTime.Format(Data.Utc);packet["expiresUtc"]=Data.BindingReceipt["binding"]!["expiresUtc"]!.DeepClone();packet["executionEnabled"]=false;
            packet["nativeObservedPropertiesSha256"]=new string('e',64);packet["windowContextSha256"]=new string('f',64);packet["workingSha256"]=new string('a',64);packet["copyId"]=copyId;packet["ownershipSha256"]=new string('d',64);
            var sha=ArtifactProof.Freeze(Data.Root,"ownedoperations/"+id+".json",packet);PreparedJournal.Create(Data.Root,id,sha,Data.Utc,new(){["executionEnabled"]=false});
            Owned=Session.Owned.Issue(new(){["state"]="owned_operation_prepared_observed",["ownedOperationId"]=id,["ownedOperationSha256"]=sha,["ownedOperation"]=packet,["expiresUtc"]=packet["expiresUtc"]!.DeepClone(),["bridgeRequestId"]=id,["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false},parameters,planHandle,binding)["ownedOperationHandle"]!.GetValue<string>();
            var input=new Pipe();var output=new Pipe();server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..CancellationRecorder.Wrap(ToolCatalog.Create(Session))]});
            run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        public static async Task<Connection> Create(bool readOnly=false,bool open=true,bool execute=true,int timeout=1)
        {var result=new Connection(readOnly,open,execute,timeout);result.Client=await McpClient.CreateAsync(result.transport,cancellationToken:result.stop.Token);return result;}
        public string Choice()
        {
            var issued=Session.Documents.Issue(new(){["state"]="documents_listed_verified",["bridgeRequestId"]=new string('a',32),["timestampUtc"]=LeaseTime.Format(Data.Utc),["documents"]=new JsonArray(new JsonObject{["modelId"]=9,["windowNumber"]=4,["activationSupported"]=true})},new string('b',64));
            return issued["documents"]![0]!["documentHandle"]!.GetValue<string>();
        }
        public async Task<(string Id,JsonObject Parameters)> Claim(string name)
        {
            var path=await Data.Files.ClaimRequest();var envelope=JsonNode.Parse(await Data.Files.ReadClaimedRequest(path))!;
            Assert.Equal("recipe.prepare",envelope["operation"]!.GetValue<string>());Assert.Equal(name,envelope["arguments"]!["recipeId"]!.GetValue<string>());
            return(Path.GetFileNameWithoutExtension(path),envelope["arguments"]!["parameters"]!.AsObject());
        }
        public void Reply(string id,JsonObject data){data["bridgeRequestId"]=id;Data.Files.Write("responses/"+id+".json",data.ToJsonString());}
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Session.Dispose();stop.Dispose();}
    }
    private static JsonObject Read(CallToolResult value)=>JsonNode.Parse(value.StructuredContent!.Value.GetRawText())!.AsObject();
    private static JsonObject Returned(Connection connection,string id,JsonObject parameters)
    {
        var plan=parameters.DeepClone().AsObject();plan["operationId"]=id;plan["operation"]="translate_owned_solid";plan["executionEnabled"]=true;
        var sha=ArtifactProof.Freeze(connection.Data.Root,"ownedexecutions/"+id+".json",plan);
        return new(){["state"]="owned_translation_executed_unverified",["operationId"]=id,["planSha256"]=sha,["geometryMutationAttempted"]=true,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,["returnCode"]=1,["copyFenced"]=true,["recordedJournalState"]="executed_unverified",["plan"]=plan};
    }

    [Theory]
    [InlineData(false,false,false,17)]
    [InlineData(false,true,false,19)]
    [InlineData(false,true,true,21)]
    [InlineData(true,true,true,6)]
    public async Task CatalogEnforcesFlagsAndReadonlyBeforePublication(bool readOnly,bool open,bool execute,int count)
    {
        await using var connection=await Connection.Create(readOnly,open,execute);Assert.Equal(count,(await connection.Client.ListToolsAsync()).Count);
        foreach(var name in new[]{"activate_document","open_working_copy","verify_working_copy_open","execute_owned_translation","verify_owned_translation"})
        {
            await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync(name,new Dictionary<string,object?>{["command"]="DELETE"}).AsTask());
            if(readOnly)Assert.Throws<McpProtocolException>(()=>connection.Session.AssertToolAllowed(name));
        }
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }

    [Fact]
    public async Task ActivationConsumesChoiceAndRevokesCadLeasesBeforeQueue()
    {
        await using var connection=await Connection.Create();var choice=connection.Choice();
        var args=new Dictionary<string,object?>{["documentHandle"]=choice};var call=connection.Client.CallToolAsync("activate_document",args).AsTask();
        var (id,parameters)=await connection.Claim("activate_document");Assert.Equal(4,parameters["windowNumber"]!.GetValue<int>());
        Assert.Throws<ReferenceRejected>(()=>connection.Session.Documents.Consume(choice));Assert.Throws<ReferenceRejected>(()=>connection.Session.Candidates.DocumentParameters(connection.Document));
        connection.Session.Copies.InspectParameters(connection.Copy);
        connection.Reply(id,new(){["state"]="document_active_verified"});var result=Read(await call);Assert.True(result["documentHandleConsumed"]!.GetValue<bool>());
        var retry=Read(await connection.Client.CallToolAsync("activate_document",args));Assert.Equal("document_choice_rejected",retry["state"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Root,"requests")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutionConsumesPacketBeforeQueueAndNeverUpgradesReturnToVerified(bool forged)
    {
        await using var connection=await Connection.Create();var args=new Dictionary<string,object?>{["ownedOperationHandle"]=connection.Owned};
        var call=connection.Client.CallToolAsync("execute_owned_translation",args).AsTask();var (id,parameters)=await connection.Claim("execute_owned_translation");
        Assert.Throws<ReferenceRejected>(()=>connection.Session.Owned.ValidationParameters(connection.Owned));Assert.Throws<ReferenceRejected>(()=>connection.Session.Candidates.DocumentParameters(connection.Document));connection.Session.Copies.InspectParameters(connection.Copy);
        var result=Returned(connection,id,parameters);if(forged)result["returnCode"]=true;connection.Reply(id,result);
        var received=await call;Assert.Equal(forged,received.IsError);var data=Read(received);
        Assert.Equal(forged?"owned_translation_outcome_unknown":"owned_translation_executed_unverified",data["state"]!.GetValue<string>());
        if(forged)Assert.False(data["retryAllowed"]!.GetValue<bool>());else Assert.True(data["copyFenced"]!.GetValue<bool>());
        var retry=Read(await connection.Client.CallToolAsync("execute_owned_translation",args));Assert.Equal("owned_translation_rejected_before_change",retry["state"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Root,"requests")));
    }

    [Fact]
    public async Task LostReplyKeepsClaimAndCannotResubmitConsumedPacket()
    {
        await using var connection=await Connection.Create();var args=new Dictionary<string,object?>{["ownedOperationHandle"]=connection.Owned};
        var call=connection.Client.CallToolAsync("execute_owned_translation",args).AsTask();var (id,_)=await connection.Claim("execute_owned_translation");
        var data=Read(await call);Assert.Equal("timeout_outcome_unknown",data["state"]!.GetValue<string>());Assert.True(data["geometryMutationAttempted"]!.GetValue<bool>());Assert.False(data["retryAllowed"]!.GetValue<bool>());
        Assert.True(File.Exists(Path.Combine(connection.Data.Root,"requests",id+".json.processing")));
        Assert.Equal("owned_translation_rejected_before_change",Read(await connection.Client.CallToolAsync("execute_owned_translation",args))["state"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Root,"requests")));
    }

    [Fact]
    public async Task CancellationAfterClaimLeavesUnknownAndOneRequest()
    {
        await using var connection=await Connection.Create();using var cancellation=new CancellationTokenSource();
        var call=new ExecutionTools(connection.Session).ExecuteOwnedTranslation(connection.Owned,cancellation.Token);
        var (id,_)=await connection.Claim("execute_owned_translation");cancellation.Cancel();
        var data=Read(await call);Assert.Equal("cancelled_outcome_unknown",data["state"]!.GetValue<string>());
        Assert.True(data["geometryMutationAttempted"]!.GetValue<bool>());Assert.False(data["retryAllowed"]!.GetValue<bool>());
        Assert.True(File.Exists(Path.Combine(connection.Data.Root,"requests",id+".json.processing")));
        Assert.Throws<ReferenceRejected>(()=>connection.Session.Owned.ValidationParameters(connection.Owned));
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Root,"requests")));
    }

    [Theory]
    [InlineData("execute_owned_translation",false)]
    [InlineData("execute_owned_translation",true)]
    [InlineData("open_working_copy",false)]
    [InlineData("open_working_copy",true)]
    [InlineData("activate_document",false)]
    [InlineData("activate_document",true)]
    public async Task OfficialSdkCancellationPreservesClaimedExecutionAndCannotReplay(string recipe,bool notify)
    {
        await using var connection=await Connection.Create(timeout:5);
        var choice=recipe=="execute_owned_translation"?null:connection.Choice();
        var args=recipe switch
        {
            "execute_owned_translation"=>new Dictionary<string,object?>{["ownedOperationHandle"]=connection.Owned},
            "open_working_copy"=>new Dictionary<string,object?>{["workingCopyHandle"]=connection.Copy,["candidateDocumentHandle"]=connection.Document,["activeDocumentHandle"]=choice},
            _=>new Dictionary<string,object?>{["documentHandle"]=choice}
        };
        using var cancellation=new CancellationTokenSource();
        var call=connection.Client.CallToolAsync(recipe,args,cancellationToken:cancellation.Token).AsTask();
        var (id,_)=await connection.Claim(recipe);
        await TransportCancellationProof.CancelClaimed(call,cancellation,connection.Session,connection.Data.Root,id,connection.Client,connection.CancellationRecorder,notify);
        Assert.Throws<ReferenceRejected>(()=>connection.Session.Candidates.DocumentParameters(connection.Document));
        if(recipe=="execute_owned_translation")Assert.Throws<ReferenceRejected>(()=>connection.Session.Owned.ValidationParameters(connection.Owned));
        if(recipe=="open_working_copy")Assert.Throws<ReferenceRejected>(()=>connection.Session.Copies.InspectParameters(connection.Copy));
        if(choice is not null)Assert.Throws<ReferenceRejected>(()=>connection.Session.Documents.Consume(choice));
        var before=TransportCancellationProof.Snapshot(connection.Data.Root);
        Assert.True((await connection.Client.CallToolAsync(recipe,args)).IsError);
        TransportCancellationProof.Unchanged(connection.Data.Root,before);
    }

    [Fact]
    public async Task CancelBeforeGateAndMaintenanceDoNotConsumePacket()
    {
        await using var connection=await Connection.Create();var tools=new ExecutionTools(connection.Session);
        await connection.Session.ReferenceGate.WaitAsync();
        try
        {
            var cancelled=Read(await tools.ExecuteOwnedTranslation(connection.Owned,new CancellationToken(true)));
            Assert.Equal("cancelled_before_submission",cancelled["state"]!.GetValue<string>());
            connection.Session.Owned.ValidationParameters(connection.Owned);
        }
        finally{connection.Session.ReferenceGate.Release();}
        connection.Data.Files.Write("maintenance.json","{}");
        Assert.Equal("maintenance_before_submission",Read(await tools.ExecuteOwnedTranslation(connection.Owned,default))["state"]!.GetValue<string>());
        connection.Session.Owned.ValidationParameters(connection.Owned);Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateVerifierConsumesFreshChoiceAndChecksOriginalIdentity(bool forged)
    {
        await using var connection=await Connection.Create();var choice=connection.Choice();var original=new string('e',32);var sha=new string('f',64);
        var args=new Dictionary<string,object?>{["executionRequestId"]=original,["executionReceiptSha256"]=sha,["candidateDocumentHandle"]=connection.Document,["activeDocumentHandle"]=choice};
        var call=connection.Client.CallToolAsync("verify_owned_translation",args).AsTask();var (id,parameters)=await connection.Claim("verify_owned_translation");
        Assert.Equal(original,parameters["executionRequestId"]!.GetValue<string>());Assert.Throws<ReferenceRejected>(()=>connection.Session.Documents.Consume(choice));
        connection.Reply(id,new(){["state"]="owned_translation_verified_observed",["executionOperationId"]=forged?new string('d',32):original,["executionReceiptSha256"]=sha,["recordedJournalState"]="succeeded_verified",["copyFenceReleased"]=true,["noCommandReplay"]=true,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false});
        var received=await call;Assert.Equal(forged,received.IsError);Assert.Equal(forged?"owned_translation_verification_rejected":"owned_translation_verified_observed",Read(received)["state"]!.GetValue<string>());
        Assert.True((await connection.Client.CallToolAsync("verify_owned_translation",args)).IsError);Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Root,"requests")));
    }

    [Fact]
    public async Task MissingOpenProofKeepsOutcomeUnknownAndRevokesOwnershipLease()
    {
        await using var connection=await Connection.Create();var args=new Dictionary<string,object?>{["workingCopyHandle"]=connection.Copy,["candidateDocumentHandle"]=connection.Document,["activeDocumentHandle"]=connection.Choice()};
        var call=connection.Client.CallToolAsync("open_working_copy",args).AsTask();var (id,parameters)=await connection.Claim("open_working_copy");
        Assert.Throws<ReferenceRejected>(()=>connection.Session.Candidates.DocumentParameters(connection.Document));
        connection.Reply(id,new(){["state"]="working_copy_opened_observed",["operationId"]=id,["planSha256"]=new string('b',64),["copyId"]=parameters["copyId"]!.DeepClone(),["ownershipSha256"]=parameters["ownershipSha256"]!.DeepClone(),["contextChangeAttempted"]=true,["geometryMutationAttempted"]=false,["cadSaveAttempted"]=false});
        var result=Read(await call);Assert.Equal("working_copy_open_outcome_unknown",result["state"]!.GetValue<string>());Assert.False(result["retryAllowed"]!.GetValue<bool>());
        Assert.Throws<ReferenceRejected>(()=>connection.Session.Copies.InspectParameters(connection.Copy));
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Data.Root,"requests")));
    }
}
