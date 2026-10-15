using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedLevelMoveStoreTests
{
    internal sealed class Data
    {
        internal readonly OwnedUniformScaleFixture Base=new(false,DateTimeOffset.UtcNow,source=>{source["busy"]=false;source["levelsContainingEntities"]=new JsonArray(new JsonObject{["number"]=0,["name"]="General",["activeRaw"]=1});});
        internal string Root=>Base.Root;
        internal readonly OwnedLevelMoveStore Store;
        internal readonly string Set;
        internal readonly JsonObject Levels,Request;
        internal Data()
        {
            Store=new(Root,Base.Candidates,Base.Bindings,()=>Base.Time,()=>Base.Utc);
            Set=Base.Candidates.IssueSet(new(){["state"]="entity_set_prepared_observed",["entitySetId"]=Base.Parameters["entitySetId"]!.DeepClone(),["entitySetSha256"]=Base.Parameters["entitySetSha256"]!.DeepClone(),["expiresUtc"]=Base.BindingPacket["expiresUtc"]!.DeepClone()})["entitySetHandle"]!.GetValue<string>();
            Levels=LevelReadToolsTests.Receipt();Levels["before"]=Base.Source.DeepClone();Levels["after"]=Base.Source.DeepClone();Levels["observedDocument"]=Base.Source["model"]!.DeepClone();
            var windows=Levels["windowsBefore"]!.AsObject();foreach(var field in new[]{"processId","processStartedUtc","windowsSessionId"}){Levels[field]=Base.Source[field]!.DeepClone();windows[field]=Base.Source[field]!.DeepClone();}
            windows["activeWindow"]!["modelId"]=Base.Source["model"]!["id"]!.DeepClone();windows["activeWindow"]!["modelName"]=Base.Source["model"]!["name"]!.DeepClone();windows["activeWindow"]!["modelPath"]=Base.Source["model"]!["path"]!.DeepClone();windows["windows"]=new JsonArray(windows["activeWindow"]!.DeepClone());Levels["windowsAfter"]=windows.DeepClone();
            Levels["levelSlots"]![0]!["activeRaw"]=1;Levels["levelSlots"]![0]!["usedRaw"]=1;
            ArtifactProof.Freeze(Root,"responses/"+Levels["bridgeRequestId"]!.GetValue<string>()+".json",Levels);
            Request=Store.AddLevelReceipt(Store.Sources(Set,Base.Binding,Parameters()),Levels);
        }
        internal static JsonObject Parameters()=>JsonNode.Parse("""{"targetLevelNumber":5,"targetLevelName":"Named unused","targetActiveRaw":0,"selectionAfterPolicy":"cleared_by_documented_api","mode":"move_in_place"}""")!.AsObject();
        internal JsonObject Native(Action<JsonObject>? mutate=null)
        {
            var id=Guid.NewGuid().ToString("N");var packet=new JsonObject{["schemaVersion"]=1,["ownedLevelMoveId"]=id,["operation"]="move_entity_to_level",["preparedUtc"]=LeaseTime.Format(Base.Utc),["expiresUtc"]=Base.BindingPacket["expiresUtc"]!.DeepClone(),["executionEnabled"]=false,["nativeProviderQualified"]=false,["arguments"]=Request.DeepClone(),["entityKeys"]=new JsonArray(new JsonArray(40,5)),["sourceLevelNumber"]=0,
                ["workingSha256"]=FileArtifacts.Sha256(File.ReadAllBytes(Base.Working)),["nativeObservedPropertiesSha256"]=Base.BindingPacket["observedPropertiesSha256"]!.DeepClone(),["requiredBeforeExecution"]=new JsonArray("qualified_set_items_level_provider","fresh_owned_context_and_composite_mapping","once_claim_and_mutation_fence","independent_level_and_geometry_verifier","saved_result_reopen","recovery_fault_matrix")};
            foreach(var field in new[]{"copyId","ownershipSha256","windowContextSha256"})packet[field]=Base.BindingPacket[field]!.DeepClone();mutate?.Invoke(packet);
            var sha=ArtifactProof.Freeze(Root,"ownedlevelmoves/"+id+".json",packet);PreparedJournal.Create(Root,id,sha,Base.Utc,new(){["executionEnabled"]=false,["contextChangeAttempted"]=false});
            var result=new JsonObject{["state"]="owned_level_move_prepared_observed",["bridgeRequestId"]=id,["ownedLevelMoveId"]=id,["ownedLevelMoveSha256"]=sha,["ownedLevelMove"]=packet};foreach(var flag in new[]{"executionEnabled","nativeProviderQualified","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})result[flag]=false;return result;
        }
        internal string Issue()=>Store.Issue(Native(),Request,Set,Base.Binding)["ownedLevelMoveHandle"]!.GetValue<string>();
    }
    [Fact] public void ConnectionHandleIsSingleUseAndExecutionSurvivesPreparedRevocation()
    {
        var data=new Data();var handle=data.Issue();Assert.Throws<ReferenceRejected>(()=>new OwnedLevelMoveStore(data.Root,data.Base.Candidates,data.Base.Bindings).Consume(handle));
        var execution=data.Store.Consume(handle);Assert.Matches("^[0-9a-f]{32}$",execution.ExecutionHandle);Assert.Equal(2,execution.Parameters.Count);Assert.Throws<ReferenceRejected>(()=>data.Store.Consume(handle));
        data.Store.ClearPrepared();data.Store.Record(execution.ExecutionHandle,new(){["state"]="timeout_outcome_unknown",["bridgeRequestId"]=Guid.NewGuid().ToString("N")});Assert.ThrowsAny<Exception>(()=>data.Store.ExecutionProof(execution.ExecutionHandle));
    }
    [Theory][InlineData("extra")][InlineData("bool")][InlineData("string")][InlineData("fraction")][InlineData("negative")][InlineData("construction")][InlineData("blank_name")][InlineData("active_bool")][InlineData("implicit_clear")]
    public void UnsafeParametersAreRejected(string fault)
    {
        var value=Data.Parameters();switch(fault){case "extra":value["command"]="x";break;case "bool":value["targetLevelNumber"]=true;break;case "string":value["targetLevelNumber"]="5";break;case "fraction":value["targetLevelNumber"]=5.1;break;case "negative":value["targetLevelNumber"]=-1;break;case "construction":value["targetLevelNumber"]=998;break;case "blank_name":value["targetLevelName"]=" ";break;case "active_bool":value["targetActiveRaw"]=false;break;case "implicit_clear":value["selectionAfterPolicy"]="preserve";break;}
        Assert.Throws<ReferenceRejected>(()=>OwnedLevelMoveStore.Parameters(value));
    }
    [Theory][InlineData("arguments")][InlineData("qualified")][InlineData("copy")][InlineData("expiry")][InlineData("requirements")][InlineData("source_level")][InlineData("entity")][InlineData("working_sha")]
    public void CorruptNativePacketsDoNotIssueHandles(string fault)
    {
        var data=new Data();var result=data.Native(packet=>{switch(fault){case "arguments":packet["arguments"]!["parameters"]!["targetLevelNumber"]=6;break;case "qualified":packet["nativeProviderQualified"]=true;break;case "copy":packet["copyId"]=new string('f',32);break;case "expiry":packet["expiresUtc"]=LeaseTime.Format(data.Base.Utc.AddSeconds(301));break;case "requirements":packet["requiredBeforeExecution"]=new JsonArray();break;case "source_level":packet["sourceLevelNumber"]=5;break;case "entity":packet["entityKeys"]=new JsonArray(new JsonArray(21,8));break;case "working_sha":packet["workingSha256"]=new string('f',64);break;}});
        Assert.Throws<ReferenceRejected>(()=>data.Store.Issue(result,data.Request,data.Set,data.Base.Binding));
    }
    [Fact] public void ChangedWorkingBytesAndExpiredSourcePreventConsumption()
    {
        var data=new Data();var handle=data.Issue();File.AppendAllText(data.Base.Working,"Own fixture saved-byte change.");Assert.Throws<ReferenceRejected>(()=>data.Store.Consume(handle));
        var expired=new Data();var stale=expired.Issue();expired.Base.Time+=301;Assert.Throws<ReferenceRejected>(()=>expired.Store.Consume(stale));
    }
    [Fact] public void MissingRawReceiptIdentityAndTamperedLevelBytesAreRejected()
    {
        var data=new Data();var raw=data.Levels.DeepClone().AsObject();raw.Remove("bridgeRequestId");
        var id=data.Levels["bridgeRequestId"]!.GetValue<string>();File.WriteAllText(Path.Combine(data.Root,"responses",id+".json"),raw.ToJsonString());Assert.Throws<ReferenceRejected>(()=>data.Store.AddLevelReceipt(data.Request,data.Levels));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void AllThreeToolsAreFullOnlyAndDefaultCatalogIsUnchanged(bool readOnly)
    {
        var files=new Fixture();files.Write("bridge.config.json","""{"coreProfile":"general-v1","expectedVersion":"23.2.2","timeoutSeconds":40,"operationTimeoutSeconds":80,"maxEntities":10000,"captureViewport":false,"levelReadingExperimental":true,"entitySetsExperimental":true,"workingCopiesExperimental":true,"documentBindingsExperimental":true,"ownedLevelMovesExperimental":true}""");
        using var session=new BridgeSession(new(files.Root,TimeSpan.FromSeconds(1),readOnly));var names=ToolCatalog.Create(session).Select(t=>t.ProtocolTool.Name).ToHashSet();
        foreach(var name in new[]{"prepare_owned_level_move","execute_owned_level_move","verify_owned_level_move","adopt_owned_level_move_execution"}){Assert.Equal(!readOnly,names.Contains(name));if(readOnly)Assert.ThrowsAny<Exception>(()=>session.AssertToolAllowed(name));}
        Assert.Equal(0,Directory.Exists(Path.Combine(files.Root,"requests"))?Directory.GetFiles(Path.Combine(files.Root,"requests")).Length:0);
    }
    [Fact] public void ActiveWorkingWriterDoesNotPreventPlanReceiptChecks()
    {
        var data=new Data();var native=data.Native();using var writer=new FileStream(data.Base.Working,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);var issued=data.Store.Issue(native,data.Request,data.Set,data.Base.Binding);Assert.Equal(2,data.Store.Consume(issued["ownedLevelMoveHandle"]!.GetValue<string>()).Parameters.Count);
    }
    [Theory][InlineData("true")][InlineData("1")][InlineData("null")][InlineData("missing_dependency")]
    public void InvalidConfigurationIsRejectedBeforeToolRegistration(string fault)
    {
        var fixture=new Fixture();fixture.Config["levelReadingExperimental"]=true;fixture.Config["workingCopiesExperimental"]=true;fixture.Config["documentBindingsExperimental"]=true;
        fixture.Config["ownedLevelMovesExperimental"]=fault=="true"?JsonValue.Create("true"):fault=="1"?JsonValue.Create(1):fault=="null"?null:JsonValue.Create(true);
        if(fault=="missing_dependency")fixture.Config["levelReadingExperimental"]=false;fixture.SaveConfig();Assert.ThrowsAny<Exception>(()=>fixture.Options().ReadConfiguration());
    }
    [Fact] public async Task ProtocolRefusesExtraFieldsCoercionAndForeignHandlesWithoutQueue()
    {
        var fixture=new Fixture();foreach(var name in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","levelReadingExperimental","ownedLevelMovesExperimental"})fixture.Config[name]=true;fixture.SaveConfig();
        using var session=new BridgeSession(fixture.Options());var input=new Pipe();var output=new Pipe();using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(session)],ServerInfo=new(){Name="powershape-bridge",Version=HostOptions.Version}});
        var run=server.RunAsync(stop.Token);var client=await McpClient.CreateAsync(new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream()),cancellationToken:stop.Token);
        try {
            var parameters=Data.Parameters();parameters["targetLevelNumber"]=true;
            await Assert.ThrowsAnyAsync<Exception>(()=>client.CallToolAsync("prepare_owned_level_move",new Dictionary<string,object?>{{"entitySetHandle",new string('a',32)},{"workingDocumentBindingHandle",new string('b',32)},{"parameters",parameters}},cancellationToken:stop.Token).AsTask());
            await Assert.ThrowsAnyAsync<Exception>(()=>client.CallToolAsync("execute_owned_level_move",new Dictionary<string,object?>{{"ownedLevelMoveHandle",new string('a',32)},{"command","MOVE"}},cancellationToken:stop.Token).AsTask());
            var rejected=await client.CallToolAsync("execute_owned_level_move",new Dictionary<string,object?>{{"ownedLevelMoveHandle",new string('a',32)}},cancellationToken:stop.Token);Assert.True(rejected.IsError);
            await Assert.ThrowsAnyAsync<Exception>(()=>client.CallToolAsync("adopt_owned_level_move_execution",new Dictionary<string,object?>{{"executionRequestId",true},{"executionReceiptSha256",new string('a',64)}},cancellationToken:stop.Token).AsTask());
            await Assert.ThrowsAnyAsync<Exception>(()=>client.CallToolAsync("adopt_owned_level_move_execution",new Dictionary<string,object?>{{"executionRequestId",new string('a',32)},{"executionReceiptSha256",new string('a',63)}},cancellationToken:stop.Token).AsTask());
            await Assert.ThrowsAnyAsync<Exception>(()=>client.CallToolAsync("adopt_owned_level_move_execution",new Dictionary<string,object?>{{"executionRequestId",new string('a',32)},{"executionReceiptSha256",new string('a',64)},{"command","MOVE"}},cancellationToken:stop.Token).AsTask());
            var missing=await client.CallToolAsync("adopt_owned_level_move_execution",new Dictionary<string,object?>{{"executionRequestId",new string('a',32)},{"executionReceiptSha256",new string('a',64)}},cancellationToken:stop.Token);Assert.True(missing.IsError);
            Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
            Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
        }finally{await client.DisposeAsync();stop.Cancel();try{await run;}catch(OperationCanceledException){}}
    }
    [Fact] public async Task CancelledExecutionBeforeGateDoesNotConsumeOrSubmit()
    {
        var fixture=new Fixture();foreach(var name in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","levelReadingExperimental","ownedLevelMovesExperimental"})fixture.Config[name]=true;fixture.SaveConfig();using var session=new BridgeSession(fixture.Options());
        using var cancel=new CancellationTokenSource();cancel.Cancel();var result=await new OwnedLevelMoveTools(session).Execute(new string('a',32),cancel.Token);
        Assert.True(result.IsError);Assert.Equal("cancelled_before_submission",result.StructuredContent!.Value.GetProperty("state").GetString());Assert.False(result.StructuredContent!.Value.GetProperty("cadMutationMayHaveOccurred").GetBoolean());Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
    }
}
