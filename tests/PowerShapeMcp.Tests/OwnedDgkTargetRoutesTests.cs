using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedDgkTargetRoutesTests
{
    private sealed class Connection:IAsyncDisposable
    {
        internal readonly OwnedDgkTargetStoreTests.Data Data=new();internal readonly BridgeSession Session;internal readonly string Set,Binding;
        internal McpClient Client=null!;private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(45));
        private Connection(bool readOnly,bool enabled)
        {
            var basic=Data.Base;foreach(var flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})basic.Files.Config[flag]=true;
            basic.Files.Config["ownedDgkTargetsExperimental"]=enabled;basic.Files.SaveConfig();basic.Files.Write("PSBridge.Worker.exe","Fake marker; never execute");
            Session=new(basic.Files.Options(readOnly,5),()=>basic.Time,()=>basic.Utc);
            var doc=Session.Candidates.Issue(basic.Source,basic.BindingPacket["inspectionSha256"]!.GetValue<string>())["candidateDocumentHandle"]!.GetValue<string>();
            Set=Session.Candidates.IssueSet(new(){["state"]="entity_set_prepared_observed",["entitySetId"]=Data.Request["entitySetId"]!.DeepClone(),["entitySetSha256"]=Data.Request["entitySetSha256"]!.DeepClone(),["expiresUtc"]=basic.BindingPacket["expiresUtc"]!.DeepClone()})["entitySetHandle"]!.GetValue<string>();
            var id=basic.BindingPacket["copyId"]!.GetValue<string>();var copy=Session.Copies.Issue(new(){["state"]="working_copy_prepared_disk",["copyId"]=id,["bridgeRequestId"]=id,["ownershipSha256"]=basic.BindingPacket["ownershipSha256"]!.DeepClone(),["timestampUtc"]=LeaseTime.Format(basic.Utc)})["workingCopyHandle"]!.GetValue<string>();
            var args=Session.Bindings.PrepareParameters(doc,copy);var bindingId=basic.BindingPacket["bindingId"]!.GetValue<string>();
            Binding=Session.Bindings.Issue(new(){["state"]="working_document_bound_observed",["bridgeRequestId"]=bindingId,["bindingId"]=bindingId,["bindingSha256"]=Data.Request["bindingSha256"]!.DeepClone(),["binding"]=basic.BindingPacket.DeepClone()},args,doc,copy)["workingDocumentBindingHandle"]!.GetValue<string>();
            var input=new Pipe();var output=new Pipe();server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new(){ToolCollection=[..ToolCatalog.Create(Session)]});run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        internal static async Task<Connection> Create(bool readOnly=false,bool enabled=true){var value=new Connection(readOnly,enabled);value.Client=await McpClient.CreateAsync(value.transport,cancellationToken:value.stop.Token);return value;}
        internal Dictionary<string,object?> Args()=>new(){["entitySetHandle"]=Set,["workingDocumentBindingHandle"]=Binding,["parameters"]=JsonSerializer.Deserialize<object>(OwnedDgkTargetStoreTests.Data.Parameters().ToJsonString())};
        internal async Task Panel(bool tamper=false)
        {
            var folder=Path.Combine(Data.Root,"requests");string? path=null;
            for(var n=0;n<500&&path is null;n++){path=Directory.Exists(folder)?Directory.GetFiles(folder,"*.json").SingleOrDefault():null;if(path is null)await Task.Delay(10,stop.Token);}
            Assert.NotNull(path);string requestText;
            // Publication may briefly retain a Windows rename/scan handle.
            // Re-read only this immutable request; never invoke the handler twice.
            for(int retry=0;;retry++) {
                try{requestText=File.ReadAllText(path!);break;}
                catch(IOException error)when(((error.HResult&0xffff) is 32 or 33)&&retry<99){await Task.Delay(10,stop.Token);}
            }
            var request=JsonNode.Parse(requestText)!.AsObject();Assert.Equal("prepare_owned_dgk_export",request["arguments"]!["recipeId"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(Data.Request,request["arguments"]!["parameters"]));var id=request["requestId"]!.GetValue<string>();
            var reply=Data.Native(id);if(tamper)reply["nativeProviderQualified"]=true;
            Directory.CreateDirectory(Path.Combine(Data.Root,"responses"));File.WriteAllText(Path.Combine(Data.Root,"responses",id+".json"),reply.ToJsonString());
        }
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Session.Dispose();stop.Dispose();}
    }
    [Theory][InlineData(false,true,true)][InlineData(true,true,false)][InlineData(false,false,false)]
    public async Task CatalogueRequiresFullModeAndExplicitFlag(bool readOnly,bool enabled,bool present)
    {
        await using var c=await Connection.Create(readOnly,enabled);var tools=await c.Client.ListToolsAsync();
        Assert.Equal(present,tools.Any(t=>t.Name=="prepare_owned_dgk_export"));Assert.DoesNotContain(tools,t=>t.Name=="execute_owned_dgk_export");
        await Assert.ThrowsAsync<McpProtocolException>(()=>c.Client.CallToolAsync("prepare_owned_dgk_export",new Dictionary<string,object?>{{"path","NeverRead.psmodel"}}).AsTask());
        Assert.False(Directory.Exists(Path.Combine(c.Data.Root,"requests")));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task NativeReplyIsCorrelatedAndTamperIssuesNoHandle(bool tamper)
    {
        await using var c=await Connection.Create();var panel=c.Panel(tamper);var reply=await c.Client.CallToolAsync("prepare_owned_dgk_export",c.Args());await panel;
        var data=JsonNode.Parse(reply.StructuredContent!.Value.GetRawText())!.AsObject();Assert.Equal(tamper,reply.IsError==true);
        if(tamper){Assert.False(data.ContainsKey("ownedDgkTargetHandle"));Assert.Equal("owned_dgk_target_preparation_outcome_unknown",data["state"]!.GetValue<string>());}
        else{var handle=data["ownedDgkTargetHandle"]!.GetValue<string>();Assert.NotNull(c.Session.DgkTargets.ValidationParameters(handle));}
        Assert.False(data["executionEnabled"]!.GetValue<bool>());
    }
    [Fact]
    public async Task InvalidVariantAndForeignHandleDoNotSubmit()
    {
        await using var c=await Connection.Create();var args=c.Args();args["entitySetHandle"]=Guid.NewGuid().ToString("N");
        var reply=await c.Client.CallToolAsync("prepare_owned_dgk_export",args);Assert.True(reply.IsError);
        args=c.Args();args["parameters"]=JsonSerializer.Deserialize<object>("""{"coordinateFrame":"world","exportVariant":"geometry_features","triangleCompanions":[],"featureCompanions":[],"settingsQualification":"planned_unqualified"}""");
        await Assert.ThrowsAsync<McpProtocolException>(()=>c.Client.CallToolAsync("prepare_owned_dgk_export",args).AsTask());Assert.False(Directory.Exists(Path.Combine(c.Data.Root,"requests")));
    }
}
