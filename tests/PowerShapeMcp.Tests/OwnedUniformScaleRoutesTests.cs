using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedUniformScaleRoutesTests
{
    private sealed class Connection:IAsyncDisposable
    {
        internal readonly OwnedUniformScaleFixture Data=new();internal readonly BridgeSession Session;internal readonly string Plan,Binding;
        internal McpClient Client=null!;private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(45));
        private Connection(bool readOnly,bool enabled)
        {
            foreach(var flag in new[]{"entitySetsExperimental","operationPlansExperimental","uniformScalePlansExperimental","workingCopiesExperimental","documentBindingsExperimental"})Data.Files.Config[flag]=true;
            Data.Files.Config["ownedUniformScalePlansExperimental"]=enabled;Data.Files.SaveConfig();Data.Files.Write("PSBridge.Worker.exe","Fake marker; never execute");
            Session=new(Data.Files.Options(readOnly,5),()=>Data.Time,()=>Data.Utc);
            var sourceSha=Data.BindingPacket["inspectionSha256"]!.GetValue<string>();var doc=Session.Candidates.Issue(Data.Source,sourceSha)["candidateDocumentHandle"]!.GetValue<string>();
            var localPlan=Data.Plans.Inspect(Data.Plan);var setRefs=localPlan["entitySet"]!.AsObject();
            var set=Session.Candidates.IssueSet(new(){["state"]="entity_set_prepared_observed",["entitySetId"]=setRefs["entitySetId"]!.DeepClone(),["entitySetSha256"]=setRefs["entitySetSha256"]!.DeepClone(),["expiresUtc"]=Data.BindingPacket["expiresUtc"]!.DeepClone()})["entitySetHandle"]!.GetValue<string>();
            Plan=Session.ScalePlans.Prepare(set,localPlan["parameters"]!.AsObject())["scalePlanHandle"]!.GetValue<string>();
            var copyId=Data.BindingPacket["copyId"]!.GetValue<string>();var copy=Session.Copies.Issue(new(){["state"]="working_copy_prepared_disk",["copyId"]=copyId,["bridgeRequestId"]=copyId,["ownershipSha256"]=Data.BindingPacket["ownershipSha256"]!.DeepClone(),["timestampUtc"]=LeaseTime.Format(Data.Utc)})["workingCopyHandle"]!.GetValue<string>();
            var bindingId=Data.BindingPacket["bindingId"]!.GetValue<string>();var args=Session.Bindings.PrepareParameters(doc,copy);
            Binding=Session.Bindings.Issue(new(){["state"]="working_document_bound_observed",["bridgeRequestId"]=bindingId,["bindingId"]=bindingId,["bindingSha256"]=Data.Parameters["bindingSha256"]!.DeepClone(),["binding"]=Data.BindingPacket.DeepClone()},args,doc,copy)["workingDocumentBindingHandle"]!.GetValue<string>();
            var input=new Pipe();var output=new Pipe();server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new(){ToolCollection=[..ToolCatalog.Create(Session)]});run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        internal static async Task<Connection> Create(bool readOnly=false,bool enabled=true){var value=new Connection(readOnly,enabled);value.Client=await McpClient.CreateAsync(value.transport,cancellationToken:value.stop.Token);return value;}
        internal Dictionary<string,object?> Args()=>new(){["scalePlanHandle"]=Plan,["workingDocumentBindingHandle"]=Binding};
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Session.Dispose();stop.Dispose();}
    }
    [Theory][InlineData(false,true,true)][InlineData(true,true,false)][InlineData(false,false,false)]
    public async Task FullModeAndExplicitFlagGatePassivePreparation(bool readOnly,bool enabled,bool present)
    {
        await using var c=await Connection.Create(readOnly,enabled);var tools=await c.Client.ListToolsAsync();
        Assert.Equal(present,tools.Any(t=>t.Name=="prepare_owned_uniform_scale"));Assert.DoesNotContain(tools,t=>t.Name=="execute_owned_uniform_scale");
        await Assert.ThrowsAsync<McpProtocolException>(()=>c.Client.CallToolAsync("prepare_owned_uniform_scale",new Dictionary<string,object?>{{"path","NeverRead.psmodel"}}).AsTask());
        Assert.False(Directory.Exists(Path.Combine(c.Data.Root,"requests")));
    }
    [Fact]
    public async Task OfficialSdkCorrelatesDisabledPacketAndOwnedBytes()
    {
        await using var c=await Connection.Create();var call=c.Client.CallToolAsync("prepare_owned_uniform_scale",c.Args()).AsTask();
        var path=await c.Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);var envelope=JsonNode.Parse(await c.Data.Files.ReadClaimedRequest(path))!;
        Assert.Equal("prepare_owned_uniform_scale",envelope["arguments"]!["recipeId"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(c.Data.Parameters,envelope["arguments"]!["parameters"]));
        c.Data.Files.Write("responses/"+id+".json",c.Data.Native(id).ToJsonString());var response=await call;
        Assert.False(response.IsError,response.Content.OfType<TextContentBlock>().First().Text);
        var result=JsonNode.Parse(response.StructuredContent!.Value.GetRawText())!;var handle=result["ownedScaleHandle"]!.GetValue<string>();
        Assert.False(result["executionEnabled"]!.GetValue<bool>());Assert.NotNull(c.Session.OwnedScales.ValidationParameters(handle));
        c.Session.RevokeCadLeases(false);Assert.Throws<ReferenceRejected>(()=>c.Session.OwnedScales.ValidationParameters(handle));
    }
    [Theory][InlineData("lost_reply")][InlineData("tampered_packet")][InlineData("changed_working")]
    public async Task UncertainOrChangedPreparationGrantsNoHandleAndNeverReplays(string fault)
    {
        await using var c=await Connection.Create();var call=c.Client.CallToolAsync("prepare_owned_uniform_scale",c.Args()).AsTask();
        var path=await c.Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);var result=c.Data.Native(id);
        if(fault=="tampered_packet")File.AppendAllText(Path.Combine(c.Data.Root,"ownedscales",id+".json")," ");
        if(fault=="changed_working")File.AppendAllText(c.Data.Working,"changed");
        if(fault!="lost_reply")c.Data.Files.Write("responses/"+id+".json",result.ToJsonString());
        var before=TransportCancellationProof.Snapshot(c.Data.Root);var response=await call;var data=JsonNode.Parse(response.StructuredContent!.Value.GetRawText())!;
        Assert.True(response.IsError);Assert.Null(data["ownedScaleHandle"]);Assert.False(data["retryAllowed"]!.GetValue<bool>());
        Assert.Equal(id,data["bridgeRequestId"]!.GetValue<string>());TransportCancellationProof.Unchanged(c.Data.Root,before);
        Assert.Single(Directory.GetFiles(Path.Combine(c.Data.Root,"requests")));
    }
}
