using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;
namespace PowerShapeMcp.Tests;
public sealed class OwnedSelectionTests
{
    [Theory][InlineData("control")][InlineData("wrong_type")][InlineData("wrong_binding")][InlineData("expired")][InlineData("other_connection")]
    public void SelectionTargetRequiresCurrentSoleSolidCandidateFromBinding(string fault)
    {
        var utc=DateTimeOffset.UtcNow;double tick=0;var store=new CandidateStore(()=>tick,()=>utc);
        var s=new JsonObject{["state"]="succeeded_verified",["bridgeRequestId"]=new string('a',32),["timestampUtc"]=LeaseTime.Format(utc),["processId"]=1,["processStartedUtc"]="2026-01-01T00:00:00Z",["windowsSessionId"]=1,["model"]=new JsonObject{["id"]=1,["name"]="Synthetic",["path"]="synthetic.psmodel"},["entityCount"]=1,["entities"]=new JsonArray(new JsonObject{["nativeTypeCode"]=fault=="wrong_type"?21:40,["nativeId"]=5,["type"]=fault=="wrong_type"?"SURFACE":"SOLID",["name"]="Body",["levelNumber"]=0}),["contextBefore"]=new JsonArray(0,0),["contextAfter"]=new JsonArray(0,0),["workplanes"]=new JsonArray()};
        string sha=new('b',64);var issued=store.Issue(s,sha);string handle=issued["entities"]![0]!["candidateEntityHandle"]!.GetValue<string>();var b=new JsonObject{["inspectionRequestId"]=new string(fault=="wrong_binding"?'c':'a',32),["inspectionSha256"]=sha};if(fault=="expired")tick=301;if(fault=="other_connection")store=new CandidateStore();
        if(fault!="control"){Assert.Throws<ReferenceRejected>(()=>store.SelectionTarget(handle,b));return;}
        Assert.True(JsonNode.DeepEquals(new JsonObject{["mode"]="select_single",["entityKey"]=new JsonArray(40,5)},store.SelectionTarget(handle,b)));store.Clear();Assert.Throws<ReferenceRejected>(()=>store.SelectionTarget(handle,b));
    }
    [Theory][InlineData(false,true)][InlineData(true,true)][InlineData(false,false)]
    public async Task FullOnlyAndMalformedOrMissingAuthorityNeverQueues(bool readOnly,bool enabled)
    {
        var files=new Fixture();foreach(var field in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})files.Config[field]=true;files.Config["ownedSelectionsExperimental"]=enabled;files.SaveConfig();using var session=new BridgeSession(files.Options(readOnly,5));
        var input=new Pipe();var output=new Pipe();using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));await using var server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(session)]});var run=server.RunAsync(stop.Token);await using var client=await McpClient.CreateAsync(new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream()),cancellationToken:stop.Token);var tools=await client.ListToolsAsync();
        foreach(var name in new[]{"prepare_owned_selection","execute_owned_selection","verify_owned_selection"})Assert.Equal(!readOnly&&enabled,tools.Any(t=>t.Name==name));
        if(!readOnly&&enabled){
            var p=tools.Single(t=>t.Name=="prepare_owned_selection");Assert.Equal(2,p.JsonSchema.GetProperty("properties").GetProperty("parameters").GetProperty("oneOf").GetArrayLength());Assert.False(p.JsonSchema.GetProperty("additionalProperties").GetBoolean());
            foreach(var parameters in new[]{new JsonObject{["mode"]="clear",["command"]="SAVE"},new JsonObject{["mode"]="select_single"}})await Assert.ThrowsAsync<ModelContextProtocol.McpProtocolException>(()=>client.CallToolAsync("prepare_owned_selection",new Dictionary<string,object?>{{"workingDocumentBindingHandle",new string('a',32)},{"parameters",parameters}}).AsTask());
            var denied=await client.CallToolAsync("prepare_owned_selection",new Dictionary<string,object?>{{"workingDocumentBindingHandle",new string('a',32)},{"parameters",new JsonObject{["mode"]="clear"}}});Assert.True(denied.IsError);
            var missing=await client.CallToolAsync("execute_owned_selection",new Dictionary<string,object?>{{"ownedSelectionHandle",new string('a',32)}});Assert.True(missing.IsError);
            var verify=await client.CallToolAsync("verify_owned_selection",new Dictionary<string,object?>{{"executionRequestId",new string('a',32)},{"executionReceiptSha256",new string('b',64)},{"candidateDocumentHandle",new string('a',32)},{"activeDocumentHandle",new string('a',32)}});Assert.True(verify.IsError);
        }
        Assert.False(Directory.Exists(Path.Combine(files.Root,"requests")));stop.Cancel();try{await run;}catch(OperationCanceledException){}
    }
    [Theory][InlineData("string")][InlineData("dependencies")]
    public void SelectionFlagRequiresBooleanAndDependencies(string fault)
    {
        var files=new Fixture();files.Config["ownedSelectionsExperimental"]=fault=="string"?JsonValue.Create("true"):JsonValue.Create(true);files.SaveConfig();Assert.Throws<InvalidDataException>(()=>files.Options().ReadConfiguration());
    }
}
