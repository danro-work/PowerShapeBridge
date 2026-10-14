using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class UniformScalePlanTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_uniform_scale_plan",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),
     Description("Experimental K8: freeze a local positive uniform-scale plan for this connection's observed entity set. Explicit world centre, factor and raw document units; all axes scale equally, in place. Creates local artifacts only, with execution disabled. No native queue, CAD command, selection, save or edit authority. Unavailable in read-only mode; native qualification pending.")]
    public async Task<CallToolResult> Prepare(
        [Description("Observed entitySetHandle issued in this MCP connection.")]string entitySetHandle,
        [Description("Exact factor>0 and !=1, centre[3], coordinateFrame=world, lengthUnits=document_raw, mode=scale_in_place. No axis locks, implicit pivot or keep-original.")]JsonElement parameters,
        CancellationToken cancellationToken)
    {
        session.AssertToolAllowed("prepare_uniform_scale_plan");var acquired=false;var timer=Stopwatch.StartNew();string? handle=null;
        try
        {
            acquired=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);
            if(!acquired)return Failure("queue_timeout_before_preparation");
            cancellationToken.ThrowIfCancellationRequested();
            var result=session.ScalePlans.Prepare(entitySetHandle,JsonNode.Parse(parameters.GetRawText())!.AsObject());
            handle=result["scalePlanHandle"]!.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout)
            {
                session.ScalePlans.Revoke(handle);
                result["state"]="uniform_scale_plan_cancelled_or_expired_after_write";result.Remove("scalePlanHandle");
                return DiagnosticTools.Result(result,true);
            }
            return DiagnosticTools.Result(result);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return Failure("cancelled_before_preparation");}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        {
            if(handle is not null)session.ScalePlans.Revoke(handle);
            var result=new JsonObject{["state"]="uniform_scale_plan_rejected",["errorCode"]=error is ReferenceRejected rejected?rejected.Code:"uniform_scale_plan_artifact_unavailable",["executionEnabled"]=false,["geometryMutationAttempted"]=false,["error"]=error.Message};
            if(error is ReferenceRejected proof&&proof.OperationId is not null){result["operationId"]=proof.OperationId;result["planSha256"]=proof.PlanSha256;result["localArtifactsMayExist"]=true;}
            return DiagnosticTools.Result(result,true);
        }
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private static CallToolResult Failure(string state)=>DiagnosticTools.Result(new JsonObject{["state"]=state,["executionEnabled"]=false,["geometryMutationAttempted"]=false},true);
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_uniform_scale_plan"))return [];
        var schema=JsonNode.Parse("""{"type":"object","properties":{"entitySetHandle":{"type":"string","pattern":"^[0-9a-f]{32}$"},"parameters":{"type":"object","properties":{"factor":{"type":"number","exclusiveMinimum":0},"centre":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3},"coordinateFrame":{"type":"string","enum":["world"]},"lengthUnits":{"type":"string","enum":["document_raw"]},"mode":{"type":"string","enum":["scale_in_place"]}},"required":["factor","centre","coordinateFrame","lengthUnits","mode"],"additionalProperties":false}},"required":["entitySetHandle","parameters"],"additionalProperties":false}""")!.AsObject();
        schema["properties"]!["entitySetHandle"]!["description"]="Observed set lease in this MCP connection; no native IDs, file paths or commands.";
        schema["properties"]!["parameters"]!["description"]="Positive factor !=1; explicit world centre and document_raw units; all axes scale equally in place.";
        return [new StrictTool(McpServerTool.Create(typeof(UniformScalePlanTools).GetMethod(nameof(Prepare))!,new UniformScalePlanTools(session)),session,schema,
            args=>UniformScaleDefinition.Read(args["parameters"]!.AsObject()))];
    }
}
