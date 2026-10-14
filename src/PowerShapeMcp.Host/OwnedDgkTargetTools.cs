using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class OwnedDgkTargetTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_owned_dgk_export",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),
     Description("Experimental: reserve a disabled owned DGK bundle target for this connection's entity set and working-document binding. Explicit World/variant/required companions. Native filesystem preparation only; no CAD export, selection, save, execution claim or edit authority. Feature flag required; absent from read-only server.")]
    public async Task<CallToolResult> Prepare(
        [Description("Observed entitySetHandle issued in this MCP connection.")]string entitySetHandle,
        [Description("Fresh owned workingDocumentBindingHandle issued in this MCP connection.")]string workingDocumentBindingHandle,
        [Description("Exact world frame, geometry/geometry_features variant, triangle and explicitly named feature companions with boolean required; settingsQualification=planned_unqualified. No paths or commands.")]JsonElement parameters,
        CancellationToken cancellationToken)
    {
        session.AssertToolAllowed("prepare_owned_dgk_export");bool gate=false,submitted=false;string? handle=null,request=null;var timer=Stopwatch.StartNew();
        try
        {
            var parsed=DgkTargetDefinition.Read(JsonNode.Parse(parameters.GetRawText())!.AsObject());
            gate=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);if(!gate)return Failure("queue_timeout_before_submission",false);
            cancellationToken.ThrowIfCancellationRequested();
            if(File.Exists(FileArtifacts.FixedPath(session.Options.BridgeRoot,"maintenance.json")))return Failure("maintenance_before_submission",false);
            var args=session.DgkTargets.PrepareParameters(entitySetHandle,workingDocumentBindingHandle,parsed);submitted=true;
            var result=await session.Ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]="prepare_owned_dgk_export",["parameters"]=args},cancellationToken,session.Options.Timeout-timer.Elapsed);
            request=result["bridgeRequestId"]?.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)return Failure("owned_dgk_target_preparation_outcome_unknown",true,request);
            if(result["state"]?.GetValue<string>()!="owned_dgk_target_prepared_files")return DiagnosticTools.Result(result,true);
            var issued=session.DgkTargets.Issue(result,args,entitySetHandle,workingDocumentBindingHandle);handle=issued["ownedDgkTargetHandle"]!.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout){session.DgkTargets.Revoke(handle);return Failure("owned_dgk_target_preparation_outcome_unknown",true,request);}
            return DiagnosticTools.Result(issued);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return Failure(submitted?"owned_dgk_target_preparation_outcome_unknown":"cancelled_before_submission",submitted,request);}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            if(handle is not null)session.DgkTargets.Revoke(handle);
            var failure=new JsonObject{["state"]=submitted?"owned_dgk_target_preparation_outcome_unknown":"owned_dgk_target_preparation_rejected",
                ["localArtifactsMayExist"]=submitted,["errorCode"]=error is ReferenceRejected rejected?rejected.Code:"dgk_target_proof_unavailable",
                ["error"]=error.Message,["executionEnabled"]=false,["retryAllowed"]=false};
            if(request is not null)failure["bridgeRequestId"]=request;return DiagnosticTools.Result(failure,true);
        }
        finally{if(gate)session.ReferenceGate.Release();}
    }
    private static CallToolResult Failure(string state,bool submitted,string? request=null)
    {
        var data=new JsonObject{["state"]=state,["localArtifactsMayExist"]=submitted,["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,["retryAllowed"]=false};
        if(request is not null)data["bridgeRequestId"]=request;return DiagnosticTools.Result(data,true);
    }
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_owned_dgk_export"))return [];
        var schema=JsonNode.Parse("""
        {"type":"object","properties":{
          "entitySetHandle":{"type":"string","pattern":"^[0-9a-f]{32}$","description":"Observed set lease in this connection."},
          "workingDocumentBindingHandle":{"type":"string","pattern":"^[0-9a-f]{32}$","description":"Owned binding lease in this connection."},
          "parameters":{"type":"object","properties":{
            "coordinateFrame":{"type":"string","enum":["world"],"description":"Explicit export frame; no active workplane fallback."},
            "exportVariant":{"type":"string","enum":["geometry","geometry_features"],"description":"Geometry only or explicitly declared feature companions."},
            "triangleCompanions":{"type":"array","maxItems":2,"description":"Documented optional/required triangle files.","items":{"type":"object","properties":{"suffix":{"type":"string","enum":[".dmt",".dgk.dmt"]},"required":{"type":"boolean"}},"required":["suffix","required"],"additionalProperties":false}},
            "featureCompanions":{"type":"array","maxItems":2,"description":"Explicit feature basenames; never executed.","items":{"type":"object","properties":{"name":{"type":"string","minLength":1,"maxLength":120},"role":{"type":"string","enum":["features_mac","features_xml"]},"required":{"type":"boolean"}},"required":["name","role","required"],"additionalProperties":false}},
            "settingsQualification":{"type":"string","enum":["planned_unqualified"],"description":"Planned parameters do not claim effective Autodesk settings."}},
            "required":["coordinateFrame","exportVariant","triangleCompanions","featureCompanions","settingsQualification"],"additionalProperties":false}},
          "required":["entitySetHandle","workingDocumentBindingHandle","parameters"],"additionalProperties":false}
        """)!.AsObject();
        return [new StrictTool(McpServerTool.Create(typeof(OwnedDgkTargetTools).GetMethod(nameof(Prepare))!,new OwnedDgkTargetTools(session)),session,schema,
            args=>DgkTargetDefinition.Read(args["parameters"]!.AsObject()))];
    }
}
