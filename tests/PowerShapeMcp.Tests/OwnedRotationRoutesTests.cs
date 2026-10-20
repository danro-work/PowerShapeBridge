using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedRotationRoutesTests
{
    internal sealed class Connection:IAsyncDisposable
    {
        internal readonly SafetyFixture Data;private readonly bool quarterTurn;internal readonly BridgeSession Session;internal readonly string Plan,Binding;
        internal McpClient Client=null!;private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(30));
        private Connection(bool readOnly,bool enabled,bool quarterTurn=false)
        {
            Data=new(quarterTurn);this.quarterTurn=quarterTurn;
            foreach(var flag in new[]{"entitySetsExperimental","operationPlansExperimental","rotationPlansExperimental","workingCopiesExperimental","documentBindingsExperimental"})Data.Files.Config[flag]=true;
            Data.Files.Config["ownedRotationPlansExperimental"]=enabled;Data.Files.Config["ownedSolidQuarterTurnsExperimental"]=enabled&&quarterTurn;Data.Files.SaveConfig();Data.Files.Write("PSBridge.Worker.exe","Fake marker; never executed");
            Session=new(Data.Files.Options(readOnly,5),()=>Data.Time,()=>Data.Utc);var candidates=Session.Candidates.Issue(Data.Source,Data.SourceSha);var doc=candidates["candidateDocumentHandle"]!.GetValue<string>();
            var nativeSet=Data.Candidates.SetParameters(Data.Sets[0]);var set=Session.Candidates.IssueSet(new(){["state"]="entity_set_prepared_observed",["entitySetId"]=nativeSet["entitySetId"]!.DeepClone(),["entitySetSha256"]=nativeSet["entitySetSha256"]!.DeepClone(),["expiresUtc"]=LeaseTime.Format(Data.Utc.AddSeconds(300))})["entitySetHandle"]!.GetValue<string>();
            Plan=Session.RotationPlans.Prepare(set,(quarterTurn?JsonNode.Parse("""{"angleDegrees":90,"origin":[0,0,0],"direction":[0,0,1],"coordinateFrame":"world","lengthUnits":"document_raw"}""")!.AsObject():RotationPlanTests.Parameters()))["rotationPlanHandle"]!.GetValue<string>();
            var copyId=Data.BindingReceipt["binding"]!["copyId"]!.GetValue<string>();var copy=Session.Copies.Issue(new(){["state"]="working_copy_prepared_disk",["copyId"]=copyId,["bridgeRequestId"]=copyId,["ownershipSha256"]=new string('d',64),["timestampUtc"]=LeaseTime.Format(Data.Utc)})["workingCopyHandle"]!.GetValue<string>();
            Binding=Session.Bindings.Issue(Data.BindingReceipt,Session.Bindings.PrepareParameters(doc,copy),doc,copy)["workingDocumentBindingHandle"]!.GetValue<string>();
            var input=new Pipe();var output=new Pipe();server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new(){ToolCollection=[..ToolCatalog.Create(Session)]});run=server.RunAsync(stop.Token);transport=new(input.Writer.AsStream(),output.Reader.AsStream());
        }
        internal static async Task<Connection> Create(bool readOnly=false,bool enabled=true,bool quarterTurn=false){var value=new Connection(readOnly,enabled,quarterTurn);value.Client=await McpClient.CreateAsync(value.transport,cancellationToken:value.stop.Token);return value;}
        internal Dictionary<string,object?> Args()=>new(){["rotationPlanHandle"]=Plan,["workingDocumentBindingHandle"]=Binding};
        internal JsonObject Reply(string id,JsonObject parameters)
        {
            var binding=Data.BindingReceipt["binding"]!.AsObject();var packet=parameters.DeepClone().AsObject();packet["schemaVersion"]=1;packet["ownedRotationId"]=id;packet["preparedUtc"]=LeaseTime.Format(Data.Utc);packet["expiresUtc"]=LeaseTime.Format(Data.Utc.AddSeconds(300));packet["executionEnabled"]=false;
            packet["nativeObservedPropertiesSha256"]=binding["observedPropertiesSha256"]!.DeepClone();packet["windowContextSha256"]=binding["windowContextSha256"]!.DeepClone();packet["workingSha256"]=new string('a',64);packet["copyId"]=binding["copyId"]!.DeepClone();packet["ownershipSha256"]=binding["ownershipSha256"]!.DeepClone();
            var sha=ArtifactProof.Freeze(Data.Root,"ownedrotations/"+id+".json",packet);PreparedJournal.Create(Data.Root,id,sha,Data.Utc,new(){["executionEnabled"]=false});
            var reply=new JsonObject{["state"]="owned_rotation_prepared_observed",["bridgeRequestId"]=id,["ownedRotationId"]=id,["ownedRotationSha256"]=sha,["ownedRotation"]=packet,["expiresUtc"]=packet["expiresUtc"]!.DeepClone(),["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};
            if(quarterTurn){var bundle=new JsonObject{["ownedRotationId"]=id,["ownedRotationSha256"]=sha};foreach(var key in new[]{"bindingId","bindingSha256","copyId","ownershipSha256","windowContextSha256"})bundle[key]=packet[key]!.DeepClone();bundle["descriptor"]=new JsonObject();reply["solidWitnessBundleSha256"]=ArtifactProof.Freeze(Data.Root,"solidrotationdescriptors/"+id+".json",bundle);reply["qualificationScope"]="one_solid_world_z_plus_90_mm_observed_samples";}
            return reply;
        }
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Session.Dispose();stop.Dispose();}
    }
    [Theory]
    [InlineData(false,true,true)]
    [InlineData(true,true,false)]
    [InlineData(false,false,false)]
    public async Task CatalogGatesDisabledOwnedPacketWithoutExposingExecutor(bool readOnly,bool enabled,bool present)
    {
        await using var c=await Connection.Create(readOnly,enabled);var tools=await c.Client.ListToolsAsync();Assert.Equal(present,tools.Any(tool=>tool.Name=="prepare_owned_rotation"));Assert.DoesNotContain(tools,tool=>tool.Name=="execute_owned_rotation");
        await Assert.ThrowsAsync<McpProtocolException>(()=>c.Client.CallToolAsync("prepare_owned_rotation",new Dictionary<string,object?>{["path"]="NeverRead.psmodel"}).AsTask());Assert.False(Directory.Exists(Path.Combine(c.Data.Root,"requests")));
    }
    [Fact]
    public async Task ActualSdkReceivesOnlyDisabledPacketAfterPrivateDependencyCheck()
    {
        await using var c=await Connection.Create();var call=c.Client.CallToolAsync("prepare_owned_rotation",c.Args()).AsTask();var path=await c.Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);var envelope=JsonNode.Parse(await c.Data.Files.ReadClaimedRequest(path))!;
        Assert.Equal("prepare_owned_rotation",envelope["arguments"]!["recipeId"]!.GetValue<string>());var parameters=envelope["arguments"]!["parameters"]!.AsObject();Assert.Equal(4,parameters.Count);
        c.Data.Files.Write("responses/"+id+".json",c.Reply(id,parameters).ToJsonString());var response=await call;Assert.False(response.IsError,response.Content.OfType<TextContentBlock>().First().Text);
        var result=JsonNode.Parse(response.StructuredContent!.Value.GetRawText())!;Assert.False(result["executionEnabled"]!.GetValue<bool>());Assert.NotNull(c.Session.OwnedRotations.ValidationParameters(result["ownedRotationHandle"]!.GetValue<string>()));
    }
    [Fact]
    public async Task LostReplyGrantsNoHandleAndPreservesPreparedArtifacts()
    {
        await using var c=await Connection.Create();var call=c.Client.CallToolAsync("prepare_owned_rotation",c.Args()).AsTask();var path=await c.Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);var envelope=JsonNode.Parse(await c.Data.Files.ReadClaimedRequest(path))!;
        c.Reply(id,envelope["arguments"]!["parameters"]!.AsObject());var before=TransportCancellationProof.Snapshot(c.Data.Root);var response=await call;
        var result=JsonNode.Parse(response.StructuredContent!.Value.GetRawText())!;
        Assert.True(response.IsError);Assert.Null(result["ownedRotationHandle"]);Assert.Equal(id,result["bridgeRequestId"]!.GetValue<string>());
        TransportCancellationProof.Unchanged(c.Data.Root,before);Assert.Single(Directory.GetFiles(Path.Combine(c.Data.Root,"requests")));
    }
    [Fact]
    public async Task ChangedPacketRetainsOriginalRequestForDiagnosisWithoutHandleOrReplay()
    {
        await using var c=await Connection.Create();var call=c.Client.CallToolAsync("prepare_owned_rotation",c.Args()).AsTask();var path=await c.Data.Files.ClaimRequest();var id=Path.GetFileNameWithoutExtension(path);var envelope=JsonNode.Parse(await c.Data.Files.ReadClaimedRequest(path))!;
        var reply=c.Reply(id,envelope["arguments"]!["parameters"]!.AsObject());File.AppendAllText(Path.Combine(c.Data.Root,"ownedrotations",id+".json")," ");
        c.Data.Files.Write("responses/"+id+".json",reply.ToJsonString());var before=TransportCancellationProof.Snapshot(c.Data.Root);var response=await call;
        var result=JsonNode.Parse(response.StructuredContent!.Value.GetRawText())!;
        Assert.True(response.IsError);Assert.Null(result["ownedRotationHandle"]);Assert.Equal(id,result["bridgeRequestId"]!.GetValue<string>());Assert.False(result["retryAllowed"]!.GetValue<bool>());
        TransportCancellationProof.Unchanged(c.Data.Root,before);Assert.Single(Directory.GetFiles(Path.Combine(c.Data.Root,"requests")));
    }
}
