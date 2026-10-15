using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedWorkplaneTests
{
    private sealed class Data
    {
        internal readonly OwnedUniformScaleFixture Base=new(false,DateTimeOffset.UtcNow);
        internal OwnedWorkplaneStore Fresh()=>new(Base.Root,Base.Bindings,()=>Base.Time,()=>Base.Utc);
        internal static JsonObject Parameters()=>JsonNode.Parse("""{"originWorldRaw":[2,3,4],"zAxis":[0,1,0],"yAxis":[0,0,1],"coordinateFrame":"world","lengthUnits":"document_raw","activeAfter":"world","mode":"create_new"}""")!.AsObject();
        internal JsonObject Native(JsonObject args)
        {
            string id=Guid.NewGuid().ToString("N");var packet=new JsonObject{["schemaVersion"]=1,["ownedWorkplaneId"]=id,["operation"]="create_world_workplane",["preparedUtc"]=LeaseTime.Format(Base.Utc),["expiresUtc"]=Base.BindingPacket["expiresUtc"]!.DeepClone(),["executionEnabled"]=false,["nativeProviderQualified"]=false,["arguments"]=args.DeepClone(),["workingSha256"]=FileArtifacts.Sha256(File.ReadAllBytes(Base.Working)),["nativeObservedPropertiesSha256"]=Base.BindingPacket["observedPropertiesSha256"]!.DeepClone(),
                ["requiredBeforeExecution"]=new JsonArray("qualified_create_workplane_provider","once_claim_and_owned_fence","declared_autoactivation_then_world","independent_created_frame_and_preserved_entities_verifier","saved_result_reopen","recovery_fault_matrix")};
            foreach(var key in new[]{"copyId","ownershipSha256","windowContextSha256"})packet[key]=Base.BindingPacket[key]!.DeepClone();
            string sha=ArtifactProof.Freeze(Base.Root,"ownedworkplanes/"+id+".json",packet);PreparedJournal.Create(Base.Root,id,sha,Base.Utc,new(){["executionEnabled"]=false,["contextChangeAttempted"]=false});
            var result=new JsonObject{["state"]="owned_workplane_prepared_observed",["bridgeRequestId"]=id,["ownedWorkplaneId"]=id,["ownedWorkplaneSha256"]=sha,["ownedWorkplane"]=packet};
            foreach(var flag in new[]{"executionEnabled","nativeProviderQualified","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})result[flag]=false;return result;
        }
        internal (string Id,string Sha) Recorded(string fault="")
        {
            var store=Fresh();var packetResult=Native(store.Sources(Base.Binding,Parameters()));var packet=packetResult["ownedWorkplane"]!.AsObject();string id=Guid.NewGuid().ToString("N");
            var plan=new JsonObject{["schemaVersion"]=1,["operationId"]=id,["operation"]="create_owned_world_workplane",["ownedWorkplaneId"]=packetResult["ownedWorkplaneId"]!.DeepClone(),["ownedWorkplaneSha256"]=packetResult["ownedWorkplaneSha256"]!.DeepClone(),["parameters"]=Parameters(),["before"]=Base.Source.DeepClone()};
            foreach(var key in new[]{"copyId","ownershipSha256","workingSha256","windowContextSha256"})plan[key]=packet[key]!.DeepClone();
            string planSha=ArtifactProof.Freeze(Base.Root,"workplaneexecutions/"+id+".json",plan);
            var claim=new JsonObject{["operationId"]=id,["planSha256"]=planSha,["ownedWorkplaneId"]=fault=="claim_other"?Guid.NewGuid().ToString("N"):plan["ownedWorkplaneId"]!.DeepClone(),["copyId"]=plan["copyId"]!.DeepClone(),["automaticReplayForbidden"]=true};
            string claimSha=ArtifactProof.Freeze(Base.Root,"ownedworkplanes/"+plan["ownedWorkplaneId"]!.GetValue<string>()+".execution-once.json",claim);
            var creation=new JsonObject{["schemaVersion"]=fault=="creation_schema"?2:1,["operationId"]=id,["planSha256"]=planSha,["createdCompositeId"]=1080000011,["nativeReturnType"]="System.Int32",["createdActiveRaw"]=fault=="creation_inactive"?0:1080000011,["createCalls"]=1,["timestampUtc"]=LeaseTime.Format(Base.Utc)};
            string creationSha=ArtifactProof.Freeze(Base.Root,"workplaneexecutions/"+id+".creation-return.json",creation);
            string? prior=null;var states=fault=="no_return"?new[]{"prepared","execution_claimed","command_started","outcome_unknown"}:new[]{"prepared","execution_claimed","command_started","command_returned","executed_unverified"};
            for(int i=0;i<states.Length;i++)
            {
                var details=i==3&&fault!="no_return"?new JsonObject{["nativeRequestId"]=id,["createdCompositeId"]=fault=="negative_return"?-1:1080000011,["nativeReturnType"]=fault=="bool_return"?"System.Boolean":"System.Int32",["worldActivationReturned"]=fault!="world_unknown",["creationReceiptSha256"]=creationSha}:new JsonObject();
                prior=ArtifactProof.Freeze(Base.Root,$"operations/{id}/{i:D6}.json",new(){["schemaVersion"]=1,["operationId"]=id,["planSha256"]=planSha,["sequence"]=i,["previousSha256"]=prior,["state"]=states[i],["timestampUtc"]=LeaseTime.Format(Base.Utc),["details"]=details});
            }
            var fence=new JsonObject{["schemaVersion"]=1,["operationId"]=id,["planSha256"]=planSha,["copyId"]=plan["copyId"]!.DeepClone(),["ownershipSha256"]=plan["ownershipSha256"]!.DeepClone(),["automaticReplayForbidden"]=true};
            string fenceSha=ArtifactProof.Freeze(Base.Root,"workingcopies/"+plan["copyId"]!.GetValue<string>()+"/mutation-fence.json",fence);
            var receipt=new JsonObject{["state"]="owned_workplane_executed_unverified",["operationId"]=id,["planSha256"]=planSha,["plan"]=plan,["claimSha256"]=claimSha,["executionFenceSha256"]=fenceSha,["cadMutationAttempted"]=true,["geometryMutationAttempted"]=true,["contextChangeAttempted"]=true,["cadSaveAttempted"]=false};
            return(id,ArtifactProof.Freeze(Base.Root,"responses/"+id+".json",receipt));
        }
        internal Dictionary<string,string> Inventory()=>Directory.GetFiles(Base.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>FileArtifacts.Sha256(File.ReadAllBytes(p)));
    }
    [Fact] public void PreparedHandleIsConnectionScopedConsumedOnceAndExpiredSourcesRefuse()
    {
        var data=new Data();var store=data.Fresh();var args=store.Sources(data.Base.Binding,Data.Parameters());var native=data.Native(args);string handle=store.Issue(native,args,data.Base.Binding)["ownedWorkplaneHandle"]!.GetValue<string>();
        Assert.Throws<ReferenceRejected>(()=>data.Fresh().Consume(handle));var consumed=store.Consume(handle);Assert.Throws<ReferenceRejected>(()=>store.Consume(handle));store.ClearPrepared();Assert.Throws<ReferenceRejected>(()=>store.ExecutionProof(consumed.ExecutionHandle));
        var expired=new Data();var other=expired.Fresh();var parameters=other.Sources(expired.Base.Binding,Data.Parameters());var h=other.Issue(expired.Native(parameters),parameters,expired.Base.Binding)["ownedWorkplaneHandle"]!.GetValue<string>();expired.Base.Time+=301;Assert.Throws<ReferenceRejected>(()=>other.Consume(h));
    }
    [Theory][InlineData("bool")][InlineData("nonorthogonal")][InlineData("zero")][InlineData("units")][InlineData("extra")]
    public void InvalidFramesDoNotCreateLocalIntent(string fault)
    {
        var value=Data.Parameters();switch(fault){case "bool":value["originWorldRaw"]![0]=true;break;case "nonorthogonal":value["yAxis"]=new JsonArray(0,1,0);break;case "zero":value["zAxis"]=new JsonArray(0,0,0);break;case "units":value["lengthUnits"]="mm";break;case "extra":value["command"]="CREATE";break;}
        Assert.Throws<ReferenceRejected>(()=>OwnedWorkplaneStore.Parameters(value));
    }
    [Fact] public void NativeExpiryFormattingKeepsTheSameInstantWithoutExtension()
    {
        var data=new Data();var store=data.Fresh();var args=store.Sources(data.Base.Binding,Data.Parameters());var reply=data.Native(args);
        var old=reply["ownedWorkplane"]!.AsObject();old["expiresUtc"]=LeaseTime.Parse(old["expiresUtc"]!.GetValue<string>(),"test").UtcDateTime.ToString("o");
        string id=Guid.NewGuid().ToString("N");old["ownedWorkplaneId"]=id;reply["bridgeRequestId"]=id;reply["ownedWorkplaneId"]=id;
        string sha=ArtifactProof.Freeze(data.Base.Root,"ownedworkplanes/"+id+".json",old);reply["ownedWorkplaneSha256"]=sha;PreparedJournal.Create(data.Base.Root,id,sha,data.Base.Utc,new(){["executionEnabled"]=false,["contextChangeAttempted"]=false});
        Assert.NotNull(store.Issue(reply,args,data.Base.Binding)["ownedWorkplaneHandle"]);
    }
    [Theory][InlineData("")][InlineData("claim_other")][InlineData("negative_return")][InlineData("bool_return")][InlineData("world_unknown")][InlineData("changed_working")][InlineData("creation_schema")][InlineData("creation_inactive")][InlineData("no_return")]
    public void ReconnectAdoptionNeedsRecordedReturnsAndNeverRevivesExecution(string fault)
    {
        var data=new Data();var recorded=data.Recorded(fault);data.Base.Time+=301;data.Base.Utc=data.Base.Utc.AddSeconds(301);
        if(fault=="changed_working")File.AppendAllText(data.Base.Working,"Changed own saved bytes.");var before=data.Inventory();var fresh=data.Fresh();
        if(fault!="")Assert.ThrowsAny<Exception>(()=>fresh.AdoptExecution(recorded.Id,recorded.Sha));
        else
        {
            var adopted=fresh.AdoptExecution(recorded.Id,recorded.Sha);Assert.True(adopted["verificationOnly"]!.GetValue<bool>());Assert.False(adopted["executionAuthorized"]!.GetValue<bool>());
            Assert.Throws<ReferenceRejected>(()=>fresh.Consume(adopted["workplaneExecutionHandle"]!.GetValue<string>()));
            Assert.Equal(recorded.Id,fresh.ExecutionProof(adopted["workplaneExecutionHandle"]!.GetValue<string>())["executionRequestId"]!.GetValue<string>());
        }
        var after=data.Inventory();Assert.Equal(before.Count,after.Count);foreach(var p in before)Assert.Equal(p.Value,after[p.Key]);Assert.False(Directory.Exists(Path.Combine(data.Base.Root,"requests")));
    }
    [Theory][InlineData("")][InlineData("world_unknown")][InlineData("bool_return")][InlineData("creation_schema")][InlineData("creation_inactive")][InlineData("no_return")]
    public void RecordedRecoveryIsReadOnlyAndRequiresBothAuthoritativeSteps(string fault)
    {
        var data=new Data();var recorded=data.Recorded(fault);var before=data.Inventory();
        var result=new RecoveryInspection(data.Base.Root).Read(recorded.Id,TimeSpan.FromSeconds(10));
        Assert.False(result["retryAllowed"]!.GetValue<bool>());Assert.False(result["executionAuthorized"]!.GetValue<bool>());
        Assert.False(result["cadConnectionAttempted"]!.GetValue<bool>());Assert.False(result["artifactWritesAttempted"]!.GetValue<bool>());
        if(fault=="")
        {
            Assert.Equal("recovery_inspected_recorded",result["state"]!.GetValue<string>());
            Assert.Equal("returned_requires_independent_verification",result["assessment"]!.GetValue<string>());
            Assert.True(result["authoritativeSuccessReturnRecorded"]!.GetValue<bool>());Assert.True(result["fencePresent"]!.GetValue<bool>());
        }
        else Assert.False(result["authoritativeSuccessReturnRecorded"]?.GetValue<bool>()??false);
        var after=data.Inventory();Assert.Equal(before.Count,after.Count);foreach(var p in before)Assert.Equal(p.Value,after[p.Key]);
        Assert.False(Directory.Exists(Path.Combine(data.Base.Root,"requests")));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ProtocolCatalogAndStrictAdmissionPreserveReadOnly(bool readOnly)
    {
        var files=new Fixture();foreach(var name in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedWorkplanesExperimental"})files.Config[name]=true;files.SaveConfig();using var session=new BridgeSession(files.Options(readOnly,5));
        var input=new Pipe();var output=new Pipe();using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));await using var server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(session)]});var run=server.RunAsync(stop.Token);
        await using var client=await McpClient.CreateAsync(new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream()),cancellationToken:stop.Token);var tools=await client.ListToolsAsync();
        foreach(var name in new[]{"prepare_owned_workplane","execute_owned_workplane","verify_owned_workplane","adopt_owned_workplane_execution"})
        {
            Assert.Equal(!readOnly,tools.Any(t=>t.Name==name));if(readOnly)Assert.ThrowsAny<Exception>(()=>session.AssertToolAllowed(name));
        }
        if(!readOnly)
        {
            Assert.False(tools.Single(t=>t.Name=="prepare_owned_workplane").JsonSchema.GetProperty("additionalProperties").GetBoolean());
            await Assert.ThrowsAnyAsync<Exception>(()=>client.CallToolAsync("execute_owned_workplane",new Dictionary<string,object?>{{"ownedWorkplaneHandle",new string('a',32)},{"command","CREATE"}}).AsTask());
            var denied=await client.CallToolAsync("execute_owned_workplane",new Dictionary<string,object?>{{"ownedWorkplaneHandle",new string('a',32)}});Assert.True(denied.IsError);
        }
        Assert.False(Directory.Exists(Path.Combine(files.Root,"requests")));stop.Cancel();try{await run;}catch(OperationCanceledException){}
    }
}
