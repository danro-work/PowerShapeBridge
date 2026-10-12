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

public sealed class ReadRoutesTests
{
    private sealed class Connection: IAsyncDisposable
    {
        public Fixture Fixture { get; }=new();
        public BridgeSession Session { get; }
        public McpClient Client { get; private set; }=null!;
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(30));
        private readonly McpServer server;
        private readonly Task run;
        private Connection(bool readOnly,bool candidates,Func<double>? clock)
        {
            Fixture.Config["entitySetsExperimental"]=candidates;Fixture.SaveConfig();Fixture.Write("PSBridge.Worker.exe","Fake marker; never executed.");
            Session=new(Fixture.Options(readOnly,seconds:2),clock);
            var input=new Pipe();var output=new Pipe();
            server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),
                new McpServerOptions{ToolCollection=[..ToolCatalog.Create(Session)],ServerInfo=new(){Name="powershape-bridge",Version=HostOptions.Version}});
            run=server.RunAsync(stop.Token);
            Transport=new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream());
        }
        private StreamClientTransport Transport { get; }
        public static async Task<Connection> Create(bool readOnly=true,bool candidates=true,Func<double>? clock=null)
        {
            var value=new Connection(readOnly,candidates,clock);
            value.Client=await McpClient.CreateAsync(value.Transport,new McpClientOptions{ProtocolVersion="2025-11-25"},cancellationToken:value.stop.Token);
            return value;
        }
        public async Task<CallToolResult> Read(string name,JsonObject receipt,Dictionary<string,object?>? arguments=null)
        {
            var pending=Client.CallToolAsync(name,arguments,cancellationToken:stop.Token).AsTask();
            var request=await Fixture.ClaimRequest();var id=Path.GetFileNameWithoutExtension(request);
            var envelope=JsonNode.Parse(File.ReadAllText(request+".processing"))!.AsObject();
            Assert.Equal(new[]{"arguments","operation","requestId"},envelope.Select(p=>p.Key).Order());
            Assert.Equal("recipe.prepare",envelope["operation"]!.GetValue<string>());
            Assert.Equal(name=="list_documents"?"list_documents":"inspect_active_model",envelope["arguments"]!["recipeId"]!.GetValue<string>());
            Assert.Empty(envelope["arguments"]!["parameters"]!.AsObject());
            var response=receipt.DeepClone().AsObject();response.Remove("bridgeRequestId");response["timestampUtc"]=DateTimeOffset.UtcNow.ToString("o");
            Fixture.Write("responses/"+id+".json",response.ToJsonString());
            return await pending;
        }
        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();
            try{await run;}catch(OperationCanceledException){}
            Session.Dispose();stop.Dispose();
        }
    }
    private static JsonObject Snapshot()
    {
        using var data=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","observations.json")));
        return JsonNode.Parse(data.RootElement[0].GetProperty("snapshot").GetRawText())!.AsObject();
    }
    private static JsonObject Data(CallToolResult result)=>JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!.AsObject();
    private static Dictionary<string,object?> References(JsonObject issued)=>new()
    { ["documentReference"]=issued["documentReference"]!.GetValue<string>(),["entityReferences"]=new[]{issued["entities"]![0]!["entityReference"]!.GetValue<string>()} };

    [Theory]
    [InlineData(true,true,6)]
    [InlineData(true,false,5)]
    [InlineData(false,true,6)]
    public async Task ExplicitCatalogAndEveryMutationRefuseBeforeQueue(bool readOnly,bool candidates,int count)
    {
        await using var connection=await Connection.Create(readOnly,candidates);
        var tools=await connection.Client.ListToolsAsync();Assert.Equal(count,tools.Count);
        Assert.All(tools,tool=>{Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);Assert.False(tool.JsonSchema.GetProperty("additionalProperties").GetBoolean());});
        foreach(var name in new[]{"activate_document","prepare_entity_set","validate_entity_set","prepare_operation_plan","inspect_operation_plan","prepare_working_copy","inspect_working_copy",
                    "bind_working_document","validate_working_document_binding","prepare_owned_operation","validate_owned_operation","open_working_copy","verify_working_copy_open","execute_owned_translation","verify_owned_translation",
                    "model_snapshot","select_entities","save_as","screenshot","execute_command"})
            await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync(name,new Dictionary<string,object?>{["command"]="DELETE"}).AsTask());
        Assert.False(Directory.Exists(Path.Combine(connection.Fixture.Root,"requests")));
        Assert.Throws<McpProtocolException>(()=>connection.Session.AssertToolAllowed("activate_document"));
    }

    [Fact]
    public async Task InspectResolveChangedPropertiesAndExpiredReferences()
    {
        double time=100;await using var connection=await Connection.Create(clock:()=>time);
        var source=Snapshot();var first=await connection.Read("inspect_active_model",source);Assert.False(first.IsError);
        var issued=Data(first);var references=References(issued);
        var resolved=await connection.Read("resolve_entity_references",source,references);Assert.False(resolved.IsError);
        Assert.Equal("entity_references_resolved_observed",Data(resolved)["state"]!.GetValue<string>());
        source["entities"]![0]!["geometry"]!["volumeRaw"]=JsonNode.Parse("2.0");
        var changed=await connection.Read("resolve_entity_references",source,references);Assert.True(changed.IsError);
        Assert.Equal("observed_properties_changed",Data(changed)["error"]!["code"]!.GetValue<string>());
        var requests=Directory.GetFiles(Path.Combine(connection.Fixture.Root,"requests"));
        var rejected=await connection.Client.CallToolAsync("resolve_entity_references",references);Assert.True(rejected.IsError);
        Assert.Equal(requests,Directory.GetFiles(Path.Combine(connection.Fixture.Root,"requests")));
        var second=Data(await connection.Read("inspect_active_model",source));time=400;
        var expired=await connection.Client.CallToolAsync("resolve_entity_references",References(second));Assert.True(expired.IsError);
        Assert.Equal("document_reference_unknown_or_expired",Data(expired)["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task CandidateReceiptHashAndReadHandlesStaySeparate()
    {
        await using var connection=await Connection.Create();var source=Snapshot();
        var issued=Data(await connection.Read("inspect_active_model",source));
        var candidates=Data(await connection.Read("inspect_entity_candidates",source));
        var document=candidates["candidateDocumentHandle"]!.GetValue<string>();
        var parameters=connection.Session.Candidates.DocumentParameters(document);
        var id=candidates["bridgeRequestId"]!.GetValue<string>();
        Assert.Equal(FileArtifacts.Sha256(File.ReadAllBytes(Path.Combine(connection.Fixture.Root,"responses",id+".json"))),parameters["inspectionSha256"]!.GetValue<string>());
        var foreign=await connection.Client.CallToolAsync("resolve_entity_references",new Dictionary<string,object?>{["documentReference"]=document,["entityReferences"]=new[]{candidates["entities"]![0]!["candidateEntityHandle"]!.GetValue<string>()}});
        Assert.True(foreign.IsError);Assert.Empty(Directory.GetFiles(Path.Combine(connection.Fixture.Root,"requests"),"*.json"));
        Assert.False((await connection.Read("resolve_entity_references",source,References(issued))).IsError);
        await using var other=await Connection.Create();
        var cross=await other.Client.CallToolAsync("resolve_entity_references",References(issued));Assert.True(cross.IsError);
        Assert.False(Directory.Exists(Path.Combine(other.Fixture.Root,"requests")));
    }

    [Fact]
    public async Task DocumentListingIssuesLocalChoicesAndNeverActivates()
    {
        await using var connection=await Connection.Create();
        var receipt=new JsonObject{["state"]="documents_listed_verified",["documents"]=new JsonArray(new JsonObject{["modelId"]=9,["windowNumber"]=4,["activationSupported"]=true})};
        var result=await connection.Read("list_documents",receipt);Assert.False(result.IsError);
        var handle=Data(result)["documents"]![0]!["documentHandle"]!.GetValue<string>();
        Assert.True(FileArtifacts.Token(handle));
        await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("activate_document",new Dictionary<string,object?>{["documentHandle"]=handle}).AsTask());
        Assert.Single(Directory.GetFiles(Path.Combine(connection.Fixture.Root,"requests"),"*.processing"));
        Assert.Empty(Directory.GetFiles(Path.Combine(connection.Fixture.Root,"requests"),"*.json"));
    }

    [Theory]
    [InlineData("inspect_active_model")]
    [InlineData("inspect_entity_candidates")]
    [InlineData("list_documents")]
    public async Task UnexpectedCaptureIsRefusedWithoutReadingImage(string name)
    {
        await using var connection=await Connection.Create();
        var receipt=name=="list_documents"?new JsonObject{["state"]="documents_listed_verified",["documents"]=new JsonArray()}:Snapshot();
        receipt["viewportCapture"]=new JsonObject{["path"]="C:/unowned-image.jpg",["sha256"]=new string('a',64)};
        var result=await connection.Read(name,receipt);Assert.True(result.IsError);
        Assert.DoesNotContain(result.Content,item=>item is ImageContentBlock);
    }

    [Fact]
    public async Task StrictReferenceArgumentsAreRefusedBeforeSubmission()
    {
        await using var connection=await Connection.Create();
        foreach(var args in new[]{new Dictionary<string,object?>(),new(){["documentReference"]=true,["entityReferences"]=new[]{new string('a',32)}},
                    new(){["documentReference"]=new string('a',32),["entityReferences"]=Array.Empty<string>()},
                    new(){["documentReference"]=new string('a',32),["entityReferences"]=new[]{new string('b',32),new string('b',32)}},
                    new(){["documentReference"]=new string('a',32),["entityReferences"]=new[]{new string('b',32)},["includeImage"]=true}})
            Assert.Equal(McpErrorCode.InvalidParams,(await Assert.ThrowsAsync<McpProtocolException>(()=>connection.Client.CallToolAsync("resolve_entity_references",args).AsTask())).ErrorCode);
        Assert.False(Directory.Exists(Path.Combine(connection.Fixture.Root,"requests")));
    }

    [Fact]
    public async Task CancellationRevokesReadLeasesAndPreservesClaimedRequest()
    {
        await using var connection=await Connection.Create();var issued=Data(await connection.Read("inspect_active_model",Snapshot()));
        using var cancellation=new CancellationTokenSource();
        var pending=connection.Client.CallToolAsync("inspect_active_model",cancellationToken:cancellation.Token).AsTask();
        var path=await connection.Fixture.ClaimRequest();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await connection.Session.ReferenceGate.WaitAsync(deadline.Token);
        try{Assert.Throws<ReferenceRejected>(()=>connection.Session.References.Require(issued["documentReference"]!.GetValue<string>(),(string[])References(issued)["entityReferences"]!));}
        finally{connection.Session.ReferenceGate.Release();}
        Assert.True(File.Exists(path+".processing"));
    }

    [Fact]
    public void AdmissionRejectsDuplicateAndNinthCallButDistinguishesStringIds()
    {
        var fixture=new Fixture();using var session=new BridgeSession(fixture.Options());var leases=new List<IDisposable>();
        try
        {
            leases.Add(session.AcquireCall(new RequestId(1)));leases.Add(session.AcquireCall(new RequestId("1")));
            Assert.Throws<McpProtocolException>(()=>session.AcquireCall(new RequestId(1)));
            for(var i=2;i<=7;i++)leases.Add(session.AcquireCall(new RequestId(i)));
            Assert.Throws<McpProtocolException>(()=>session.AcquireCall(new RequestId(8)));
        }
        finally{foreach(var lease in leases)lease.Dispose();}
        using var accepted=session.AcquireCall(new RequestId(8));
    }
    [Fact]
    public async Task SemanticGateWaitIsIncludedInReadDeadline()
    {
        await using var connection=await Connection.Create();
        await connection.Session.ReferenceGate.WaitAsync();
        try
        {
            var result=await connection.Client.CallToolAsync("inspect_active_model");
            Assert.True(result.IsError);
            Assert.Equal("queue_timeout_before_submission",Data(result)["state"]!.GetValue<string>());
            Assert.False(Directory.Exists(Path.Combine(connection.Fixture.Root,"requests")));
        }
        finally{connection.Session.ReferenceGate.Release();}
    }
}
