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

public sealed class PreparedRoutesTests
{
    private sealed class Connection:IAsyncDisposable
    {
        public SafetyFixture Data { get; }=new();
        public BridgeSession Session { get; }
        public McpClient Client { get; private set; }=null!;
        public JsonObject Candidates { get; }
        public string Copy { get; }
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(40));
        private readonly McpServer server;
        private readonly Task run;
        private readonly StreamClientTransport transport;
        private Connection(bool readOnly)
        {
            foreach(var flag in new[]{"entitySetsExperimental","operationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedOperationPlansExperimental"})Data.Files.Config[flag]=true;
            Data.Files.SaveConfig();Data.Files.Write("PSBridge.Worker.exe","Fake marker; never executed.");
            Session=new(Data.Files.Options(readOnly,2),()=>Data.Time,()=>Data.Utc);
            Candidates=Session.Candidates.Issue(Data.Source,Data.SourceSha);
            var copyId=Data.BindingReceipt["binding"]!["copyId"]!.GetValue<string>();
            Copy=Session.Copies.Issue(new(){["state"]="working_copy_prepared_disk",["copyId"]=copyId,["bridgeRequestId"]=copyId,["ownershipSha256"]=new string('d',64),["timestampUtc"]=LeaseTime.Format(Data.Utc)})["workingCopyHandle"]!.GetValue<string>();
            var input=new Pipe();var output=new Pipe();
            server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(Session)]});
            run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        public static async Task<Connection> Create(bool readOnly=false)
        {var result=new Connection(readOnly);result.Client=await McpClient.CreateAsync(result.transport,cancellationToken:result.stop.Token);return result;}
        public async Task<JsonObject> Native(string name,Dictionary<string,object?> args,Func<string,JsonObject,JsonObject> reply)
        {
            var call=Client.CallToolAsync(name,args).AsTask();var path=await Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);
            var envelope=JsonNode.Parse(await Data.Files.ReadClaimedRequest(path))!.AsObject();
            Assert.Equal("recipe.prepare",envelope["operation"]!.GetValue<string>());Assert.Equal(name,envelope["arguments"]!["recipeId"]!.GetValue<string>());
            var result=reply(id,envelope["arguments"]!["parameters"]!.AsObject());result["bridgeRequestId"]=id;
            Data.Files.Write("responses/"+id+".json",result.ToJsonString());
            var received=await call;Assert.False(received.IsError,received.Content.OfType<TextContentBlock>().First().Text);return Read(received);
        }
        public async ValueTask DisposeAsync()
        {await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Session.Dispose();stop.Dispose();}
    }
    private static JsonObject Read(CallToolResult value)=>JsonNode.Parse(value.StructuredContent!.Value.GetRawText())!.AsObject();

    [Theory]
    [InlineData(false,17)]
    [InlineData(true,6)]
    public async Task PreparedCatalogIsExplicitAndSeparateFromReadonly(bool readOnly,int expected)
    {
        await using var connection=await Connection.Create(readOnly);var tools=await connection.Client.ListToolsAsync();Assert.Equal(expected,tools.Count);
        foreach(var tool in tools)Assert.False(tool.JsonSchema.GetProperty("additionalProperties").GetBoolean());
        foreach(var name in new[]{"execute_owned_translation","verify_owned_translation","open_working_copy","verify_working_copy_open","activate_document","execute_command"})
            await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync(name).AsTask());
        if(readOnly)
        {
            foreach(var name in new[]{"prepare_entity_set","validate_entity_set","prepare_operation_plan","inspect_operation_plan","prepare_working_copy","inspect_working_copy","bind_working_document","validate_working_document_binding","prepare_owned_operation","validate_owned_operation"})
            {await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync(name).AsTask());Assert.Throws<McpProtocolException>(()=>connection.Session.AssertToolAllowed(name));}
        }
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }

    [Fact]
    public async Task AllPreparedStagesUseFixedPrivateParametersAndStayDisabled()
    {
        await using var connection=await Connection.Create();var doc=connection.Candidates["candidateDocumentHandle"]!.GetValue<string>();var candidate=connection.Candidates["entities"]![0]!["candidateEntityHandle"]!.GetValue<string>();
        var set=await connection.Native("prepare_entity_set",new(){["candidateDocumentHandle"]=doc,["candidateEntityHandles"]=new[]{candidate}},(id,parameters)=>
        {
            Assert.Equal(connection.Data.SourceSha,parameters["inspectionSha256"]!.GetValue<string>());
            var sha=ArtifactProof.Freeze(connection.Data.Root,"entitysets/"+id+".json",parameters);
            return new(){["state"]="entity_set_prepared_observed",["entitySetId"]=id,["entitySetSha256"]=sha,["expiresUtc"]=parameters["expiresUtc"]!.DeepClone()};
        });
        var setHandle=set["entitySetHandle"]!.GetValue<string>();
        await connection.Native("validate_entity_set",new(){["entitySetHandle"]=setHandle},(_,parameters)=>new(){["state"]="entity_set_validated_observed",["entitySetId"]=parameters["entitySetId"]!.DeepClone(),["entitySetSha256"]=parameters["entitySetSha256"]!.DeepClone()});
        var queued=Directory.GetFiles(Path.Combine(connection.Data.Root,"requests"));
        var plan=Read(await connection.Client.CallToolAsync("prepare_operation_plan",new Dictionary<string,object?>{["operation"]="translate_entities",["entitySetHandles"]=new[]{setHandle},["parameters"]=JsonSerializer.Deserialize<JsonElement>("{\"vector\":[1.0,0,0],\"coordinateFrame\":\"world\",\"lengthUnits\":\"document_raw\",\"mode\":\"move\"}")}));
        Assert.Equal("operation_plan_prepared_local",plan["state"]!.GetValue<string>());Assert.False(plan["plan"]!["executionEnabled"]!.GetValue<bool>());
        var planHandle=plan["operationPlanHandle"]!.GetValue<string>();
        var inspected=Read(await connection.Client.CallToolAsync("inspect_operation_plan",new Dictionary<string,object?>{["operationPlanHandle"]=planHandle}));Assert.Equal("prepared",inspected["journal"]!["state"]!.GetValue<string>());
        Assert.Equal(queued,Directory.GetFiles(Path.Combine(connection.Data.Root,"requests")));
        var copy=await connection.Native("prepare_working_copy",new(){["sourcePath"]="C:/owned/Source.psmodel",["sourceSha256"]=new string('b',64)},(id,parameters)=>
            new(){["state"]="working_copy_prepared_disk",["copyId"]=id,["ownershipSha256"]=new string('d',64),["timestampUtc"]=LeaseTime.Format(connection.Data.Utc)});
        var copyHandle=copy["workingCopyHandle"]!.GetValue<string>();
        await connection.Native("inspect_working_copy",new(){["workingCopyHandle"]=copyHandle},(_,parameters)=>{var result=parameters.DeepClone().AsObject();result["state"]="working_copy_inspected_disk";return result;});
        var bound=await connection.Native("bind_working_document",new(){["candidateDocumentHandle"]=doc,["workingCopyHandle"]=copyHandle},(id,parameters)=>
        {
            var binding=parameters.DeepClone().AsObject();binding["schemaVersion"]=1;binding["bindingId"]=id;binding["bridgeVersion"]="0.31.0";binding["timestampUtc"]=LeaseTime.Format(connection.Data.Utc);
            binding["observedPropertiesSha256"]=new string('e',64);binding["windowContextSha256"]=new string('f',64);binding["executionEnabled"]=false;
            var sha=ArtifactProof.Freeze(connection.Data.Root,"documentbindings/"+id+".json",binding);
            return new(){["state"]="working_document_bound_observed",["bindingId"]=id,["bindingSha256"]=sha,["binding"]=binding};
        });
        var bindingHandle=bound["workingDocumentBindingHandle"]!.GetValue<string>();
        await connection.Native("validate_working_document_binding",new(){["workingDocumentBindingHandle"]=bindingHandle},(_,parameters)=>
        {var result=bound.DeepClone().AsObject();result.Remove("workingDocumentBindingHandle");result.Remove("bindingLifetimeSeconds");result["state"]="working_document_binding_validated_observed";return result;});
        var packet=await connection.Native("prepare_owned_operation",new(){["operationPlanHandle"]=planHandle,["workingDocumentBindingHandle"]=bindingHandle},(id,parameters)=>
        {
            var value=parameters.DeepClone().AsObject();value["schemaVersion"]=1;value["ownedOperationId"]=id;value["preparedUtc"]=LeaseTime.Format(connection.Data.Utc);value["expiresUtc"]=bound["binding"]!["expiresUtc"]!.DeepClone();value["executionEnabled"]=false;
            value["nativeObservedPropertiesSha256"]=new string('e',64);value["windowContextSha256"]=new string('f',64);value["workingSha256"]=new string('a',64);value["copyId"]=copy["copyId"]!.DeepClone();value["ownershipSha256"]=new string('d',64);
            var sha=ArtifactProof.Freeze(connection.Data.Root,"ownedoperations/"+id+".json",value);PreparedJournal.Create(connection.Data.Root,id,sha,connection.Data.Utc,new(){["executionEnabled"]=false});
            return new(){["state"]="owned_operation_prepared_observed",["ownedOperationId"]=id,["ownedOperationSha256"]=sha,["ownedOperation"]=value,["expiresUtc"]=value["expiresUtc"]!.DeepClone(),["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};
        });
        var packetHandle=packet["ownedOperationHandle"]!.GetValue<string>();var journalPath=Path.Combine(connection.Data.Root,"operations",packet["ownedOperationId"]!.GetValue<string>(),"000000.json");var original=File.ReadAllBytes(journalPath);
        await connection.Native("validate_owned_operation",new(){["ownedOperationHandle"]=packetHandle},(_,parameters)=>
        {var result=packet.DeepClone().AsObject();result.Remove("ownedOperationHandle");result.Remove("ownedOperationLifetimeSeconds");result["state"]="owned_operation_validated_observed";return result;});
        Assert.Equal(original,File.ReadAllBytes(journalPath));Assert.False(packet["executionEnabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task InvalidSourceAndPlanArgsFailProtocolBeforeAnyQueueOrPlan()
    {
        await using var connection=await Connection.Create();
        await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("prepare_working_copy",new Dictionary<string,object?>{["sourcePath"]="C:/../original.psmodel",["sourceSha256"]=new string('a',64)}).AsTask());
        await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("prepare_working_copy",new Dictionary<string,object?>{["sourcePath"]="C:/owned/Source.psmodel",["sourceSha256"]=new string('a',32)}).AsTask());
        await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("prepare_operation_plan",new Dictionary<string,object?>{["operation"]="translate_entities",["entitySetHandles"]=new[]{new string('a',32)},["parameters"]=JsonSerializer.Deserialize<JsonElement>("{\"vector\":[true,0,0],\"coordinateFrame\":\"world\",\"lengthUnits\":\"document_raw\",\"mode\":\"move\"}")}).AsTask());
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
    }

    [Fact]
    public async Task ExpiredLeaseAndClaimedTimeoutNeverRetry()
    {
        await using var connection=await Connection.Create();connection.Data.Time=400;
        var rejected=await connection.Client.CallToolAsync("inspect_working_copy",new Dictionary<string,object?>{["workingCopyHandle"]=connection.Copy});Assert.True(rejected.IsError);
        Assert.False(Directory.Exists(Path.Combine(connection.Data.Root,"requests")));
        var pending=connection.Client.CallToolAsync("prepare_working_copy",new Dictionary<string,object?>{["sourcePath"]="C:/owned/Source.psmodel",["sourceSha256"]=new string('a',64)}).AsTask();
        var path=await connection.Data.Files.ClaimRequest();var unknown=Read(await pending);
        Assert.Equal("timeout_outcome_unknown",unknown["state"]!.GetValue<string>());Assert.True(File.Exists(path+".processing"));Assert.Empty(Directory.GetFiles(Path.Combine(connection.Data.Root,"requests"),"*.json"));
    }
    [Fact]
    public async Task DependencyExpiryDuringNativeBindingDoesNotIssueHandle()
    {
        await using var connection=await Connection.Create();var doc=connection.Candidates["candidateDocumentHandle"]!.GetValue<string>();
        var call=connection.Client.CallToolAsync("bind_working_document",new Dictionary<string,object?>{["candidateDocumentHandle"]=doc,["workingCopyHandle"]=connection.Copy}).AsTask();
        var path=await connection.Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);
        var parameters=JsonNode.Parse(await connection.Data.Files.ReadClaimedRequest(path))!["arguments"]!["parameters"]!.AsObject();
        var binding=parameters.DeepClone().AsObject();binding["schemaVersion"]=1;binding["bindingId"]=id;binding["bridgeVersion"]="0.31.0";binding["timestampUtc"]=LeaseTime.Format(connection.Data.Utc);
        binding["observedPropertiesSha256"]=new string('e',64);binding["windowContextSha256"]=new string('f',64);binding["executionEnabled"]=false;
        var sha=ArtifactProof.Freeze(connection.Data.Root,"documentbindings/"+id+".json",binding);
        connection.Data.Time=400;
        connection.Data.Files.Write("responses/"+id+".json",new JsonObject{["state"]="working_document_bound_observed",["bindingId"]=id,["bindingSha256"]=sha,["binding"]=binding}.ToJsonString());
        var result=await call;Assert.True(result.IsError);var data=Read(result);
        Assert.Equal("working_document_binding_rejected",data["state"]!.GetValue<string>());Assert.Null(data["workingDocumentBindingHandle"]);
    }
    [Fact]
    public async Task CancellationAfterClaimRevokesCopyLeasesAndKeepsProcessing()
    {
        await using var connection=await Connection.Create();using var cancellation=new CancellationTokenSource();
        var call=connection.Client.CallToolAsync("prepare_working_copy",new Dictionary<string,object?>{["sourcePath"]="C:/owned/Source.psmodel",["sourceSha256"]=new string('a',64)},cancellationToken:cancellation.Token).AsTask();
        var path=await connection.Data.Files.ClaimRequest();cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>call);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));await connection.Session.ReferenceGate.WaitAsync(deadline.Token);
        try{Assert.Throws<ReferenceRejected>(()=>connection.Session.Copies.InspectParameters(connection.Copy));}
        finally{connection.Session.ReferenceGate.Release();}
        Assert.True(File.Exists(path+".processing"));Assert.Empty(Directory.GetFiles(Path.Combine(connection.Data.Root,"requests"),"*.json"));
    }
}
