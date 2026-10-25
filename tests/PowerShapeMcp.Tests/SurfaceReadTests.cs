using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class SurfaceReadTests
{
    static JsonObject Grid()=>new(){["tStart"]=1,["tEnd"]=2,["tDivisions"]=2,["uStart"]=1,["uEnd"]=2,["uDivisions"]=3};
    static JsonObject Snapshot(DateTimeOffset utc,bool solid=false)=>new(){["state"]="succeeded_verified",["bridgeRequestId"]=new string('a',32),["timestampUtc"]=LeaseTime.Format(utc),["processId"]=1,["processStartedUtc"]="2026-01-01T00:00:00Z",["windowsSessionId"]=1,["model"]=new JsonObject{["id"]=1,["name"]="Synthetic",["path"]="synthetic.psmodel"},["entityCount"]=1,["entities"]=new JsonArray(new JsonObject{["nativeTypeCode"]=solid?40:21,["nativeId"]=2,["name"]="Surface",["type"]=solid?"SOLID":"SURFACE",["levelNumber"]=0}),["contextBefore"]=new JsonArray(0,0),["contextAfter"]=new JsonArray(0,0),["workplanes"]=new JsonArray()};
    [Theory][InlineData("control")][InlineData("wrong_type")][InlineData("wrong_document")][InlineData("expired")][InlineData("other_connection")]
    public void OnlyFreshSurfaceFromExactCandidateDocumentIsResolved(string fault)
    {
        var utc=DateTimeOffset.UtcNow;double tick=0;var store=new CandidateStore(()=>tick,()=>utc);var reply=store.Issue(Snapshot(utc,fault=="wrong_type"),new string('b',64));string document=reply["candidateDocumentHandle"]!.GetValue<string>(),entity=reply["entities"]![0]!["candidateEntityHandle"]!.GetValue<string>();
        if(fault=="wrong_document")document=store.Issue(Snapshot(utc),new string('b',64))["candidateDocumentHandle"]!.GetValue<string>();if(fault=="expired")tick=301;if(fault=="other_connection")store=new CandidateStore();
        if(fault!="control"){Assert.Throws<ReferenceRejected>(()=>store.SurfaceReadParameters(document,entity));return;}
        var args=store.SurfaceReadParameters(document,entity);Assert.True(JsonNode.DeepEquals(args["entityKey"],new JsonArray(21,2)));Assert.Equal(new string('a',32),args["inspectionRequestId"]!.GetValue<string>());Assert.False(args.ContainsKey("bindingId"));
    }
    [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(true,false)]
    public async Task SamplingIsExplicitlyEnabledInBothModesAndBadAuthorityNeverQueues(bool readOnly,bool enabled)
    {
        var files=new Fixture();files.Config["entitySetsExperimental"]=true;files.Config["surfaceSamplingExperimental"]=enabled;files.SaveConfig();using var session=new BridgeSession(files.Options(readOnly,5));var input=new Pipe();var output=new Pipe();using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));await using var server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(session)]});var run=server.RunAsync(stop.Token);await using var client=await McpClient.CreateAsync(new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream()),cancellationToken:stop.Token);var tools=await client.ListToolsAsync();Assert.Equal(enabled,tools.Any(t=>t.Name=="inspect_surface_samples"));
        if(enabled){var tool=tools.Single(t=>t.Name=="inspect_surface_samples");Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);var valid=new Dictionary<string,object?>{{"candidateDocumentHandle",new string('a',32)},{"candidateEntityHandle",new string('b',32)},{"grid",Grid()}};Assert.True((await client.CallToolAsync("inspect_surface_samples",valid)).IsError);var bad=Grid();bad["tDivisions"]=true;valid["grid"]=bad;await Assert.ThrowsAsync<ModelContextProtocol.McpProtocolException>(()=>client.CallToolAsync("inspect_surface_samples",valid).AsTask());}
        if(readOnly)Assert.DoesNotContain(tools,t=>t.Name.StartsWith("execute_",StringComparison.Ordinal));Assert.False(Directory.Exists(Path.Combine(files.Root,"requests")));stop.Cancel();try{await run;}catch(OperationCanceledException){}
    }
    [Theory][InlineData("noninteger")][InlineData("extra")][InlineData("range")][InlineData("limit")]
    public void GridRejectsAmbiguousOrUnboundedParameters(string fault)
    {
        var grid=Grid();if(fault=="noninteger")grid["tDivisions"]=2.5;if(fault=="extra")grid["command"]="SAVE";if(fault=="range")grid["tEnd"]=1;if(fault=="limit")grid["uDivisions"]=33;Assert.Throws<ReferenceRejected>(()=>SurfaceReadTools.Grid(grid));
    }
}
