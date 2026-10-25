using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
namespace PowerShapeMcp;

[McpServerToolType]
public sealed class SurfaceReadTools(BridgeSession session)
{
    internal const string Profile="surface_uv_endpoint_samples_v1";
    internal static void Need(bool value){if(!value)throw new ReferenceRejected("surface_sample_proof_invalid");}
    internal static double Number(JsonNode? value){Need(value is not null);var e=JsonSerializer.SerializeToElement(value);Need(e.ValueKind==JsonValueKind.Number&&e.TryGetDouble(out var n)&&double.IsFinite(n));return e.GetDouble();}
    internal static int Integer(JsonNode? value){Need(value is not null);var e=JsonSerializer.SerializeToElement(value);Need(e.ValueKind==JsonValueKind.Number&&e.TryGetInt32(out _));return e.GetInt32();}
    internal static void Grid(JsonObject grid)
    {
        Need(ArtifactProof.Fields(grid,"tStart","tEnd","tDivisions","uStart","uEnd","uDivisions"));
        foreach(var k in new[]{"tStart","tEnd","uStart","uEnd"})Need(Math.Abs(Number(grid[k]))<=1000000);
        Need(Number(grid["tStart"])<Number(grid["tEnd"])&&Number(grid["uStart"])<Number(grid["uEnd"]));
        foreach(var k in new[]{"tDivisions","uDivisions"})Need(Integer(grid[k])is >=1 and <=32);
    }
    [McpServerTool(Name="inspect_surface_samples",ReadOnly=true,Destructive=false,OpenWorld=false)]
    [Description("Experimental passive SURFACE sampling in the current WORLD/empty-selection document. Use fresh same-document candidate handles and an explicit known UV range, never guessed from a box. Grid fields: tStart,tEnd,tDivisions,uStart,uEnd,uDivisions; divisions are integers1..32 and include both endpoints, producing (tDivisions+1)*(uDivisions+1) points. Returns world/document_raw positions, raw normals and visibility in native reported order, observed units and explicit unavailable bounds. No selection clearing, activation, images, geometry edits, save, inferred domain, full topology certificate or edit handles. Available in full and readonly when enabled.")]
    public async Task<CallToolResult> Inspect([Description("Fresh candidate document from inspect_entity_candidates in this connection.")]string candidateDocumentHandle,[Description("SURFACE candidate from that same inspection/document.")]string candidateEntityHandle,[Description("Exact six-field object: explicit finite increasing tStart/tEnd and uStart/uEnd, tDivisions/uDivisions integers1..32. No automatic parameter-domain inference.")]JsonElement grid,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed("inspect_surface_samples");bool acquired=false,submitted=false;var timer=Stopwatch.StartNew();
        try{
            acquired=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!acquired)return DiagnosticTools.Result(new(){["state"]="queue_timeout_before_submission"},true);
            var g=JsonNode.Parse(grid.GetRawText())!.AsObject();Grid(g);var args=session.Candidates.SurfaceReadParameters(candidateDocumentHandle,candidateEntityHandle);args["grid"]=g;
            cancellationToken.ThrowIfCancellationRequested();submitted=true;var reply=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]="inspect_surface_samples",["parameters"]=args},cancellationToken,session.Options.Timeout-timer.Elapsed);
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return DiagnosticTools.Result(new(){["state"]="surface_read_cancelled_or_timed_out",["cadMutationAttempted"]=false},true);
            if(reply["state"]?.GetValue<string>()!="surface_samples_read_observed")return DiagnosticTools.Result(reply,true);
            var output=ValidateReply(session.Options.BridgeRoot,reply,args);session.Candidates.Observe(reply["after"]!.AsObject());
            return DiagnosticTools.Result(output);
        }catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return DiagnosticTools.Result(new(){["state"]="cancelled_read",["cadMutationAttempted"]=false},true);}
        catch(Exception e)when(e is ReferenceRejected or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException){
            if(submitted)session.Candidates.Clear();return DiagnosticTools.Result(new(){["state"]="surface_samples_read_rejected",["errorCode"]=e is ReferenceRejected x?x.Code:"surface_read_data_unavailable",["cadMutationAttempted"]=false,["contextChangeAttempted"]=false,["geometryMutationAttempted"]=false,["cadSaveAttempted"]=false},true);
        }finally{if(acquired)session.ReferenceGate.Release();}
    }
    internal static JsonObject ValidateReply(string root,JsonObject reply,JsonObject args)
    {
        string id=ArtifactProof.String(reply,"bridgeRequestId","surface_read_receipt_invalid");var original=SolidQuarterTurnProof.Raw(root,id);Need(JsonNode.DeepEquals(original.Value,reply));
        Need(reply["state"]?.GetValue<string>()=="surface_samples_read_observed"&&reply["profile"]?.GetValue<string>()==Profile&&ArtifactProof.Boolean(reply["nativeProviderQualified"],true));
        foreach(var k in new[]{"contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","captureAttempted","nativeLayoutQualified","retryAllowed","parameterRangeInferred","topologyCertified"})Need(ArtifactProof.Boolean(reply[k],false));Need(!reply.ContainsKey("viewportCapture")&&reply["boundsWorldRawPaired"]is null&&reply["boundsStatus"]?.GetValue<string>()=="unavailable_in_sample_only_profile");
        var actualArgs=reply["arguments"]!.AsObject();Need(ArtifactProof.Fields(actualArgs,"inspectionRequestId","inspectionSha256","expiresUtc","entityKey","grid"));foreach(var k in new[]{"inspectionRequestId","inspectionSha256","expiresUtc","entityKey"})Need(JsonNode.DeepEquals(actualArgs[k],args[k]));Need(JsonNode.DeepEquals(reply["entityKey"],args["entityKey"]));Grid(reply["grid"]!.AsObject());Grid(actualArgs["grid"]!.AsObject());
        var source=ArtifactProof.Verified(root,"responses",args["inspectionRequestId"]!.GetValue<string>(),args["inspectionSha256"]!.GetValue<string>(),16000000);var before=reply["before"]!.AsObject();var after=reply["after"]!.AsObject();
        foreach(var s in new[]{source,before,after}){
            Need(ObservationPolicy.Read(source).Digest==ObservationPolicy.Read(s).Digest&&JsonNode.DeepEquals(source["model"],s["model"]));
            Need(JsonNode.DeepEquals(s["contextBefore"],new JsonArray(0,0))&&JsonNode.DeepEquals(s["contextAfter"],new JsonArray(0,0))&&JsonNode.DeepEquals(s["selectionBefore"],new JsonArray())&&JsonNode.DeepEquals(s["selectionAfter"],new JsonArray())&&ArtifactProof.Boolean(s["busy"],false));
            foreach(var k in new[]{"processId","processStartedUtc","windowsSessionId","productVersion"})Need(JsonNode.DeepEquals(source[k],s[k]));
        }
        Need(JsonNode.DeepEquals(reply["windowsBefore"],reply["windowsAfter"]));var windows=reply["windowsAfter"]!.AsObject();var active=windows["activeWindow"]!.AsObject();Need(JsonNode.DeepEquals(active["modelId"],after["model"]!["id"])&&ObservationPolicy.NormalizePath(active["modelPath"]!.GetValue<string>())==ObservationPolicy.NormalizePath(after["model"]!["path"]!.GetValue<string>()));
        foreach(var k in new[]{"processId","processStartedUtc","windowsSessionId"})Need(JsonNode.DeepEquals(windows[k],after[k]));
        var key=args["entityKey"]!.AsArray();var entity=after["entities"]!.AsArray().Single(e=>JsonNode.DeepEquals(e!["nativeTypeCode"],key[0])&&JsonNode.DeepEquals(e["nativeId"],key[1]));Need(Integer(key[0])==21&&JsonNode.DeepEquals(reply["entity"],entity));
        var g=args["grid"]!.AsObject();foreach(var k in new[]{"tStart","tEnd","uStart","uEnd"})Need(Number(g[k])==Number(reply["grid"]![k])&&Number(g[k])==Number(actualArgs["grid"]![k]));foreach(var k in new[]{"tDivisions","uDivisions"})Need(Integer(g[k])==Integer(reply["grid"]![k])&&Integer(g[k])==Integer(actualArgs["grid"]![k]));
        int tp=Integer(g["tDivisions"])+1,up=Integer(g["uDivisions"])+1,count=tp*up;Need(Integer(reply["shape"]!["tPoints"])==tp&&Integer(reply["shape"]!["uPoints"])==up&&Integer(reply["shape"]!["pointCount"])==count&&reply["shape"]!["order"]!.GetValue<string>()=="native_reported_order");
        var raw=reply["rawResponse"]!.AsArray();Need(raw.Count==4&&Integer(raw[0])==0);foreach(var pair in new[]{("positionsWorldRaw",1),("normalsWorldRaw",2)}){
            var flat=raw[pair.Item2]!.AsArray();var points=reply[pair.Item1]!.AsArray();Need(flat.Count==3*count&&points.Count==count);for(int i=0;i<count;i++){var p=points[i]!.AsArray();Need(p.Count==3);for(int j=0;j<3;j++)Need(Number(p[j])==Number(flat[3*i+j]));}
        }
        var flags=raw[3]!.AsArray();Need(flags.Count==count&&JsonNode.DeepEquals(flags,reply["visibility"]));foreach(var flag in flags)Need(flag is JsonValue v&&v.TryGetValue<bool>(out _));
        Need(reply["coordinateFrame"]?.GetValue<string>()=="world"&&reply["lengthUnits"]?.GetValue<string>()=="document_raw"&&JsonNode.DeepEquals(reply["lengthUnitsRaw"],source["lengthUnitsRaw"])&&Number(reply["lengthUnitFactorRaw"])==Number(source["lengthUnitFactorRaw"])&&Number(reply["lengthUnitFactorRaw"])>0);
        var expiry=LeaseTime.Parse(args["expiresUtc"]!.GetValue<string>(),"surface_read_expired");var stamp=LeaseTime.Parse(reply["timestampUtc"]!.GetValue<string>(),"surface_read_time_invalid");Need(stamp<=DateTimeOffset.UtcNow.AddSeconds(5)&&stamp<expiry&&DateTimeOffset.UtcNow<expiry);foreach(var s in new[]{before,after})Need(LeaseTime.Parse(s["timestampUtc"]!.GetValue<string>(),"surface_read_time_invalid")<=stamp);
        var result=reply.DeepClone().AsObject();result["state"]="surface_samples_inspected_read_only";result["nativeReceiptSha256"]=original.Sha;result["cadMutationAttempted"]=false;return result;
    }
    internal static JsonObject GridSchema(int maxDivisions=32)
    {
        var fields=new JsonObject();foreach(var k in new[]{"tStart","tEnd","uStart","uEnd"})fields[k]=new JsonObject{["type"]="number",["minimum"]=-1000000,["maximum"]=1000000};foreach(var k in new[]{"tDivisions","uDivisions"})fields[k]=new JsonObject{["type"]="integer",["minimum"]=1,["maximum"]=maxDivisions};return new JsonObject{["type"]="object",["properties"]=fields,["required"]=new JsonArray("tStart","tEnd","tDivisions","uStart","uEnd","uDivisions"),["additionalProperties"]=false};
    }
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.SurfaceSamplingEnabled)return[];var p=new JsonObject();foreach(var k in new[]{"candidateDocumentHandle","candidateEntityHandle"})p[k]=StrictTool.Token(k,"Fresh same-connection passive candidate.")["properties"]![k]!.DeepClone();
        p["grid"]=GridSchema();
        return[new StrictTool(McpServerTool.Create(new SurfaceReadTools(session).Inspect),session,new JsonObject{["type"]="object",["properties"]=p,["required"]=new JsonArray("candidateDocumentHandle","candidateEntityHandle","grid"),["additionalProperties"]=false},x=>Grid(x["grid"]!.AsObject()))];
    }
}
