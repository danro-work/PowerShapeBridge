using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class LevelReadToolsTests
{
    internal static JsonObject Receipt()
    {
        using var input=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","observations.json")));
        var before=JsonNode.Parse(input.RootElement[0].GetProperty("snapshot").GetRawText())!.AsObject();
        foreach(var field in new[]{"levelsContainingEntities","workplanes","selectionBefore","selectionAfter"})before[field]=new JsonArray();
        before["contextBefore"]=new JsonArray(0,0);before["contextAfter"]=new JsonArray(0,0);before["busy"]=false;
        before["state"]="succeeded_verified";before["modelScope"]="active_document_read_only";before["productVersion"]="23.2.2";
        foreach(var field in new[]{"lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"})if(!before.ContainsKey(field))before[field]=null;
        var model=before["model"]!.AsObject();var window=new JsonObject{["modelId"]=model["id"]!.DeepClone(),["modelName"]=model["name"]!.DeepClone(),["modelPath"]=model["path"]!.DeepClone()};
        var windows=new JsonObject{["activeWindow"]=window,["windowCount"]=1,["windows"]=new JsonArray(window.DeepClone())};
        var reply=new JsonObject{["state"]="native_levels_read_unqualified",["slotCount"]=1000,["levelSlots"]=new JsonArray(),["filterFlagsRaw"]=new JsonArray(0,0,0),["filterOrder"]=new JsonArray("used","named","on"),["before"]=before,["after"]=before.DeepClone(),["observedDocument"]=model.DeepClone(),["timestampUtc"]=DateTimeOffset.UtcNow.ToString("o"),["bridgeRequestId"]=Guid.NewGuid().ToString("N")};
        foreach(var field in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","nativeLayoutQualified"})reply[field]=false;
        foreach(var field in new[]{"processId","processStartedUtc","windowsSessionId"}){reply[field]=before[field]!.DeepClone();windows[field]=before[field]!.DeepClone();}
        reply["windowsBefore"]=windows;reply["windowsAfter"]=windows.DeepClone();
        for(int n=0;n<1000;n++)reply["levelSlots"]!.AsArray().Add(new JsonObject{["number"]=n,["name"]=n==0?"General":n==5?"Named unused":"",["usedRaw"]=0,["activeRaw"]=0,["usedValueType"]="System.Double",["activeValueType"]="System.Double"});
        return reply;
    }
    [Fact] public void CompleteReceiptKeepsNamedUnusedSlotsAndIssuesNoEditHandles()
    {
        var result=LevelReadTools.Validate(Receipt());Assert.Equal("model_levels_inspected_read_only",result["state"]!.GetValue<string>());
        Assert.Equal("Named unused",result["levelSlots"]![5]!["name"]!.GetValue<string>());Assert.Equal(1000,result["levelSlots"]!.AsArray().Count);
        Assert.False(result.ContainsKey("documentBindingHandle"));Assert.False(result["cadMutationAttempted"]!.GetValue<bool>());
    }
    [Theory]
    [InlineData("missing_before")][InlineData("missing_field")][InlineData("count")][InlineData("order")][InlineData("flag_string")][InlineData("flag_fraction")][InlineData("flag_bool")][InlineData("value_type")][InlineData("selection_drift")][InlineData("document_drift")][InlineData("windows_drift")][InlineData("viewport")][InlineData("future")][InlineData("stale")][InlineData("mutation")][InlineData("qualified")][InlineData("busy")][InlineData("filter_order")]
    public void UnsafeOrIncompleteReceiptsAreRefused(string fault)
    {
        var reply=Receipt();var row=reply["levelSlots"]![5]!.AsObject();
        switch(fault){
            case "missing_before":reply.Remove("before");break;case "missing_field":reply["after"]!.AsObject().Remove("generalToleranceRaw");break;
            case "count":reply["slotCount"]=999;break;case "order":row["number"]=6;break;
            case "flag_string":row["usedRaw"]="0";break;case "flag_fraction":row["usedRaw"]=0.5;break;case "flag_bool":row["usedRaw"]=false;break;case "value_type":row["usedValueType"]="System.String";break;
            case "selection_drift":reply["after"]!["selectionAfter"]=new JsonArray("Solid1");break;case "document_drift":reply["after"]!["model"]!["id"]=99;break;
            case "windows_drift":reply["windowsAfter"]!["windowCount"]=2;break;case "viewport":reply["before"]!["viewportCapture"]=new JsonObject();break;
            case "future":reply["timestampUtc"]=DateTimeOffset.UtcNow.AddHours(1).ToString("o");break;case "stale":reply["timestampUtc"]=DateTimeOffset.UtcNow.AddHours(-1).ToString("o");break;
            case "mutation":reply["cadSaveAttempted"]=true;break;case "qualified":reply["nativeLayoutQualified"]=true;break;case "busy":reply["before"]!["busy"]=true;break;
            case "filter_order":reply["filterOrder"]=new JsonArray("on","used","named");break;
        }
        Assert.ThrowsAny<Exception>(()=>LevelReadTools.Validate(reply));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task ReadOnlyAndFullUseTheFixedNoArgumentRecipe(bool readOnly)
    {
        var fixture=new Fixture();fixture.Config["levelReadingExperimental"]=true;fixture.SaveConfig();fixture.Write("PSBridge.Worker.exe","Fake marker only");
        using var session=new BridgeSession(fixture.Options(readOnly,seconds:3));
        var tools=ToolCatalog.Create(session);var tool=Assert.Single(tools,t=>t.ProtocolTool.Name=="inspect_model_levels");
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);Assert.False(tool.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean());
        var pending=new LevelReadTools(session).Inspect(CancellationToken.None);var request=await fixture.ClaimRequest();
        var envelope=JsonNode.Parse(await fixture.ReadClaimedRequest(request))!.AsObject();
        Assert.Equal("recipe.prepare",envelope["operation"]!.GetValue<string>());Assert.Equal("inspect_model_levels",envelope["arguments"]!["recipeId"]!.GetValue<string>());Assert.Empty(envelope["arguments"]!["parameters"]!.AsObject());
        var response=Receipt();response.Remove("bridgeRequestId");fixture.Write("responses/"+envelope["requestId"]!.GetValue<string>()+".json",response.ToJsonString());
        var result=await pending;Assert.False(result.IsError);Assert.Equal("model_levels_inspected_read_only",result.StructuredContent!.Value.GetProperty("state").GetString());
    }
    [Fact] public void DisabledFlagDoesNotExposeTheTool()
    {var fixture=new Fixture();using var session=new BridgeSession(fixture.Options(true));Assert.DoesNotContain(ToolCatalog.Create(session),t=>t.ProtocolTool.Name=="inspect_model_levels");Assert.ThrowsAny<Exception>(()=>session.AssertToolAllowed("inspect_model_levels"));}
    [Fact] public async Task ActualMcpProtocolRejectsExtraArgumentsBeforeQueueSubmission()
    {
        var fixture=new Fixture();fixture.Config["levelReadingExperimental"]=true;fixture.SaveConfig();fixture.Write("PSBridge.Worker.exe","Fake marker only");
        using var session=new BridgeSession(fixture.Options(true,seconds:3));var input=new Pipe();var output=new Pipe();
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(session)],ServerInfo=new(){Name="powershape-bridge",Version=HostOptions.Version}});
        var run=server.RunAsync(stop.Token);var transport=new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream());
        await using var client=await McpClient.CreateAsync(transport,new McpClientOptions{ProtocolVersion="2025-11-25"},cancellationToken:stop.Token);
        var tools=await client.ListToolsAsync(cancellationToken:stop.Token);var tool=Assert.Single(tools,t=>t.Name=="inspect_model_levels");
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);Assert.False(tool.JsonSchema.GetProperty("additionalProperties").GetBoolean());
        await Assert.ThrowsAnyAsync<Exception>(()=>client.CallToolAsync("inspect_model_levels",new Dictionary<string,object?>{{"levelNumber",0}},cancellationToken:stop.Token).AsTask());
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
        await client.DisposeAsync();stop.Cancel();try{await run;}catch(OperationCanceledException){}
    }
    [Theory][InlineData("1")][InlineData("\"true\"")][InlineData("null")]
    public void InvalidFlagTypesAreRefused(string json)
    {var fixture=new Fixture();fixture.Config["levelReadingExperimental"]=JsonNode.Parse(json);fixture.SaveConfig();Assert.ThrowsAny<Exception>(()=>fixture.Options().ReadConfiguration());}
}
