using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class WorkplaneActivationTests
{
    private static JsonObject Source(DateTimeOffset utc)=>new()
    {
        ["state"]="succeeded_verified",["bridgeRequestId"]=new string('a',32),["timestampUtc"]=LeaseTime.Format(utc),
        ["processId"]=1,["processStartedUtc"]="2026-01-01T00:00:00Z",["windowsSessionId"]=1,
        ["model"]=new JsonObject{["id"]=1,["name"]="Synthetic",["path"]="synthetic.psmodel"},["entityCount"]=2,
        ["entities"]=new JsonArray(new JsonObject{["nativeTypeCode"]=40,["nativeId"]=5,["type"]="SOLID",["name"]="Body",["levelNumber"]=0},new JsonObject{["nativeTypeCode"]=54,["nativeId"]=11,["type"]="WORKPLANE",["name"]="Plane",["levelNumber"]=0}),
        ["contextBefore"]=new JsonArray(0,0),["contextAfter"]=new JsonArray(0,0),["workplanes"]=new JsonArray()
    };
    [Theory][InlineData("control")][InlineData("wrong_type")][InlineData("wrong_binding")][InlineData("wrong_sha")][InlineData("expired")][InlineData("context_changed")][InlineData("other_connection")]
    public void CandidateMustBeFreshWorkplaneFromTheBindingInspection(string fault)
    {
        var utc=DateTimeOffset.UtcNow;double tick=0;var store=new CandidateStore(()=>tick,()=>utc);var source=Source(utc);string sha=new('b',64);
        var reply=store.Issue(source,sha);var key=reply["entities"]!.AsArray()[fault=="wrong_type"?0:1]!["candidateEntityHandle"]!.GetValue<string>();
        var binding=new JsonObject{["inspectionRequestId"]=source["bridgeRequestId"]!.DeepClone(),["inspectionSha256"]=sha};
        if(fault=="wrong_binding")binding["inspectionRequestId"]=new string('c',32);if(fault=="wrong_sha")binding["inspectionSha256"]=new string('c',64);
        if(fault=="expired")tick=301;if(fault=="other_connection")store=new CandidateStore(()=>tick,()=>utc);
        if(fault=="context_changed"){var other=Source(utc);other["contextBefore"]=new JsonArray(0,1080000011);store.Observe(other);}
        if(fault!="control"){Assert.Throws<ReferenceRejected>(()=>store.WorkplaneTarget(key,binding));return;}
        Assert.True(JsonNode.DeepEquals(new JsonObject{["mode"]="existing",["nativeId"]=11},store.WorkplaneTarget(key,binding)));
        Assert.Throws<ReferenceRejected>(()=>store.PrepareParameters(reply["candidateDocumentHandle"]!.GetValue<string>(),[key]));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task OnlyFullServerRegistersActivationAndMalformedOrUnknownIntentNeverQueues(bool readOnly)
    {
        var files=new Fixture();foreach(var field in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","ownedWorkplaneActivationExperimental"})files.Config[field]=true;files.SaveConfig();using var session=new BridgeSession(files.Options(readOnly,5));
        var input=new Pipe();var output=new Pipe();using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));await using var server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(session)]});var run=server.RunAsync(stop.Token);
        await using var client=await McpClient.CreateAsync(new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream()),cancellationToken:stop.Token);var tools=await client.ListToolsAsync();
        foreach(var name in new[]{"prepare_owned_workplane_activation","execute_owned_workplane_activation","verify_owned_workplane_activation","adopt_owned_workplane_activation_execution"})Assert.Equal(!readOnly,tools.Any(t=>t.Name==name));
        if(!readOnly)
        {
            var prepare=tools.Single(t=>t.Name=="prepare_owned_workplane_activation");Assert.False(prepare.JsonSchema.GetProperty("additionalProperties").GetBoolean());Assert.Equal(2,prepare.JsonSchema.GetProperty("properties").GetProperty("parameters").GetProperty("oneOf").GetArrayLength());
            var denied=await client.CallToolAsync("execute_owned_workplane_activation",new Dictionary<string,object?>{{"workplaneActivationHandle",new string('a',32)}});Assert.True(denied.IsError);
            var malformed=await client.CallToolAsync("prepare_owned_workplane_activation",new Dictionary<string,object?>{{"workingDocumentBindingHandle",new string('a',32)},{"parameters",new{mode="world",command="SAVE"}}});Assert.True(malformed.IsError);
            await Assert.ThrowsAnyAsync<Exception>(()=>client.CallToolAsync("execute_owned_workplane_activation",new Dictionary<string,object?>{{"workplaneActivationHandle",new string('a',32)},{"command","SAVE"}}).AsTask());
        }
        Assert.False(Directory.Exists(Path.Combine(files.Root,"requests")));stop.Cancel();try{await run;}catch(OperationCanceledException){}
    }
    [Theory][InlineData("valid")][InlineData("creation_receipt")][InlineData("unknown")][InlineData("fence")][InlineData("extended")]
    public void VerifiedActivationCopyAccessCannotUseCreationOrUnknownReceipts(string fault)
    {
        var now=DateTimeOffset.UtcNow;var owner=new JsonObject{["copyId"]=new string('a',32),["ownershipSha256"]=new string('b',64)};
        var receipt=new JsonObject{["state"]="owned_workplane_activation_verified_observed",["recordedJournalState"]="succeeded_verified",["bridgeRequestId"]=new string('c',32),["executionOperationId"]=new string('d',32),["copyFenceReleased"]=true,["noCommandReplay"]=true,["cadMutationAttempted"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,["timestampUtc"]=LeaseTime.Format(now)};
        if(fault=="creation_receipt")receipt["state"]="owned_workplane_verified_observed";if(fault=="unknown")receipt["state"]="owned_workplane_activation_outcome_unknown";if(fault=="fence")receipt["copyFenceReleased"]=false;
        var expiry=now.AddSeconds(fault=="extended"?301:60);var copies=new WorkingCopyStore(utcClock:()=>now);
        if(fault!="valid"){Assert.Throws<ReferenceRejected>(()=>copies.IssueVerifiedWorkplaneActivation(receipt,owner,expiry));return;}
        var result=copies.IssueVerifiedWorkplaneActivation(receipt,owner,expiry);Assert.True(JsonNode.DeepEquals(owner,copies.InspectParameters(result["workingCopyHandle"]!.GetValue<string>())));Assert.Null(result["executionAuthorized"]);
    }
}
