using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class LevelReadTools(BridgeSession session)
{
    [McpServerTool(Name="inspect_model_levels",ReadOnly=true,Destructive=false,OpenWorld=false)]
    [Description("Experimental passive read of all 1000 level slots in the active PowerShape 2023 document. Returns names, raw used/active flags and current filter state without changing filters, visibility, selection, active document, geometry or saved files. Empty/unused slots are not authorization to reuse, rename or delete them. Requires the matching native level-reading profile; issues no edit handles.")]
    public async Task<CallToolResult> Inspect(CancellationToken cancellationToken)
    {
        session.AssertToolAllowed("inspect_model_levels");var acquired=false;var timer=Stopwatch.StartNew();
        try
        {
            if(!await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken))
                return DiagnosticTools.Result(new(){["state"]="queue_timeout_before_submission"},true);
            acquired=true;
            var reply=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]="inspect_model_levels",["parameters"]=new JsonObject()},cancellationToken,session.Options.Timeout-timer.Elapsed);
            if(cancellationToken.IsCancellationRequested)throw new OperationCanceledException(cancellationToken);
            if(reply.ContainsKey("viewportCapture"))throw new ReferenceRejected("unexpected_level_capture");
            if(reply["state"]?.GetValue<string>()!="native_levels_read_unqualified")return DiagnosticTools.Result(reply,true);
            return DiagnosticTools.Result(Validate(reply));
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested)
        {return DiagnosticTools.Result(new(){["state"]="cancelled_read",["cadMutationAttempted"]=false},true);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or JsonException or NullReferenceException or FormatException)
        {return DiagnosticTools.Result(new(){["state"]="level_read_rejected",["error"]=new JsonObject{["code"]=error is ReferenceRejected rejected?rejected.Code:"level_receipt_unavailable"},["cadMutationAttempted"]=false},true);}
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    internal static JsonObject Validate(JsonObject reply)
    {
        void Need(bool value){if(!value)throw new ReferenceRejected("level_receipt_invalid");}
        int Bit(JsonNode? node)
        {
            var value=JsonSerializer.SerializeToElement(node);
            Need(value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var n)&&(n==0||n==1));
            return value.GetDouble()==1?1:0;
        }
        Need(reply["state"]?.GetValue<string>()=="native_levels_read_unqualified");
        foreach(var field in new[]{"before","after","levelSlots","slotCount","filterFlagsRaw","filterOrder","observedDocument","windowsBefore","windowsAfter","timestampUtc","bridgeRequestId"})Need(reply.ContainsKey(field)&&reply[field] is not null);
        foreach(var field in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","nativeLayoutQualified"})Need(reply[field]?.GetValue<bool>()==false);
        var before=reply["before"]!.AsObject();var after=reply["after"]!.AsObject();
        Need(!reply.ContainsKey("viewportCapture")&&!before.ContainsKey("viewportCapture")&&!after.ContainsKey("viewportCapture"));
        Need(ObservationPolicy.Read(before).Digest==ObservationPolicy.Read(after).Digest);
        foreach(var field in new[]{"processId","processStartedUtc","windowsSessionId","productVersion","model","entityCount","entities","levelsContainingEntities","contextBefore","contextAfter","selectionBefore","selectionAfter","workplanes","lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"})
            Need(before.ContainsKey(field)&&after.ContainsKey(field)&&JsonNode.DeepEquals(before[field],after[field]));
        Need(before["modelScope"]?.GetValue<string>()=="active_document_read_only"&&after["modelScope"]?.GetValue<string>()=="active_document_read_only"&&before["busy"]?.GetValue<bool>()==false&&after["busy"]?.GetValue<bool>()==false);
        Need(JsonNode.DeepEquals(before["contextBefore"],before["contextAfter"])&&JsonNode.DeepEquals(after["contextBefore"],after["contextAfter"])&&JsonNode.DeepEquals(before["selectionBefore"],before["selectionAfter"])&&JsonNode.DeepEquals(after["selectionBefore"],after["selectionAfter"]));
        Need(FileArtifacts.Token(reply["bridgeRequestId"]!.GetValue<string>()));
        Need(JsonNode.DeepEquals(reply["observedDocument"],after["model"]));
        var windows=reply["windowsBefore"]!.AsObject();Need(JsonNode.DeepEquals(windows,reply["windowsAfter"]));
        foreach(var field in new[]{"processId","processStartedUtc","windowsSessionId"})Need(JsonNode.DeepEquals(reply[field],after[field])&&JsonNode.DeepEquals(windows[field],after[field]));
        var active=windows["activeWindow"]!.AsObject();var model=after["model"]!.AsObject();
        Need(JsonNode.DeepEquals(active["modelId"],model["id"])&&JsonNode.DeepEquals(active["modelName"],model["name"])&&
             ObservationPolicy.NormalizePath(active["modelPath"]!.GetValue<string>())==ObservationPolicy.NormalizePath(model["path"]!.GetValue<string>()));
        var stamp=LeaseTime.Parse(reply["timestampUtc"]!.GetValue<string>(),"level_receipt_time_invalid");
        Need(stamp<=DateTimeOffset.UtcNow.AddSeconds(5)&&stamp>=DateTimeOffset.UtcNow.AddSeconds(-300));
        var slots=reply["levelSlots"]!.AsArray();Need(reply["slotCount"]?.GetValue<int>()==1000&&slots.Count==1000);
        for(int number=0;number<1000;number++)
        {
            var row=slots[number]!.AsObject();Need(ArtifactProof.Fields(row,"number","name","usedRaw","activeRaw","usedValueType","activeValueType"));
            Need(row["number"]?.GetValue<int>()==number&&row["name"] is JsonValue name&&name.TryGetValue<string>(out var text)&&text.Length<=4096);
            _=Bit(row["usedRaw"]);_=Bit(row["activeRaw"]);
            foreach(var field in new[]{"usedValueType","activeValueType"})Need(row[field]?.GetValue<string>() is "System.Int32" or "System.Double");
        }
        var filters=reply["filterFlagsRaw"]!.AsArray();Need(filters.Count==3);foreach(var item in filters)_=Bit(item);
        Need(JsonNode.DeepEquals(reply["filterOrder"],new JsonArray("used","named","on")));
        foreach(var level in after["levelsContainingEntities"]!.AsArray())
        {
            var row=level!.AsObject();int number=row["number"]!.GetValue<int>();Need(number>=0&&number<1000);
            Need(JsonNode.DeepEquals(row["name"],slots[number]!["name"])&&Bit(row["activeRaw"])==Bit(slots[number]!["activeRaw"]));
        }
        return new(){["state"]="model_levels_inspected_read_only",["levelSlots"]=slots.DeepClone(),["slotCount"]=1000,
            ["filterFlagsRaw"]=filters.DeepClone(),["filterOrder"]=reply["filterOrder"]!.DeepClone(),["model"]=model.DeepClone(),
            ["processId"]=reply["processId"]!.DeepClone(),["processStartedUtc"]=reply["processStartedUtc"]!.DeepClone(),["windowsSessionId"]=reply["windowsSessionId"]!.DeepClone(),
            ["timestampUtc"]=reply["timestampUtc"]!.DeepClone(),["bridgeRequestId"]=reply["bridgeRequestId"]!.DeepClone(),
            ["nativeLayoutQualified"]=false,["cadMutationAttempted"]=false,
            ["qualification"]="Current level slot names/flags only; allocation, reuse permission, continuous identity and edit authorization are not established."};
    }
    public static McpServerTool[] Create(BridgeSession session)=>session.LevelReadingEnabled?
        [new StrictTool(McpServerTool.Create(new LevelReadTools(session).Inspect),session,StrictTool.Empty())]:[];
}
