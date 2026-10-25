using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class RotationPlanTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_rotation_plan",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false),Description("Experimental K8: freeze a passive rotation plan for this connection's observed SOLID/SURFACE/COMPCURVE set. Explicit world origin/direction and integer degrees only, matching documented RotateItem long angle. No native queue, CAD command, binding or execution authority. Not available in read-only mode. Execution requires a separate owned path: prepare_owned_solid_rotation supports one SOLID, nonzero integer -359..359 WORLD degrees and explicit pivot in mm/factor1; the legacy quarter-turn path accepts only +/-90. With the separate surface flag, prepare_owned_rotation plus execute_owned_surface_rotation supports one SURFACE with an explicit UV grid and bounded non-noop integer WORLD angle/pivot/axis; sampled Z90 and negative oblique cases are separately qualified. Other types/sets and fractional angles remain unsupported for execution.")]
    public async Task<CallToolResult> Prepare([Description("Observed entitySetHandle from this connection.")]string entitySetHandle,[Description("Exact angleDegrees (integer), origin[3], direction[3], coordinateFrame=world, lengthUnits=document_raw. Nonzero direction; no implicit UI pivot or rounding.")]JsonElement parameters,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed("prepare_rotation_plan");var acquired=false;var timer=Stopwatch.StartNew();string? handle=null;
        try
        {
            acquired=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);
            if(!acquired)return Failure("queue_timeout_before_preparation");
            cancellationToken.ThrowIfCancellationRequested();
            var result=session.RotationPlans.Prepare(entitySetHandle,JsonNode.Parse(parameters.GetRawText())!.AsObject());handle=result["rotationPlanHandle"]!.GetValue<string>();
            if(cancellationToken.IsCancellationRequested||timer.Elapsed>=session.Options.Timeout){session.RotationPlans.Revoke(handle);return Failure("rotation_plan_cancelled_or_expired_after_write");}
            return DiagnosticTools.Result(result);
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return Failure("cancelled_before_preparation");}
        catch(Exception error)when(error is ReferenceRejected or IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException)
        {
            if(handle is not null)session.RotationPlans.Revoke(handle);
            return DiagnosticTools.Result(new JsonObject{["state"]="rotation_plan_rejected",["errorCode"]=error is ReferenceRejected rejected?rejected.Code:"rotation_plan_artifact_unavailable",["executionEnabled"]=false,["error"]=error.Message},true);
        }
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    private static CallToolResult Failure(string state)=>DiagnosticTools.Result(new JsonObject{["state"]=state,["executionEnabled"]=false,["geometryMutationAttempted"]=false},true);
    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_rotation_plan"))return [];
        var schema=JsonNode.Parse("""{"type":"object","properties":{"entitySetHandle":{"type":"string","pattern":"^[0-9a-f]{32}$"},"parameters":{"type":"object","properties":{"angleDegrees":{"type":"integer","minimum":-1000000,"maximum":1000000},"origin":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3},"direction":{"type":"array","items":{"type":"number"},"minItems":3,"maxItems":3},"coordinateFrame":{"type":"string","enum":["world"]},"lengthUnits":{"type":"string","enum":["document_raw"]}},"required":["angleDegrees","origin","direction","coordinateFrame","lengthUnits"],"additionalProperties":false}},"required":["entitySetHandle","parameters"],"additionalProperties":false}""")!.AsObject();
        schema["properties"]!["entitySetHandle"]!["description"]="Observed entity set lease in this MCP connection; no native IDs or paths.";
        schema["properties"]!["parameters"]!["description"]="Explicit integer degrees, world origin/direction and document_raw units; no UI defaults or fractional rounding.";
        return [new StrictTool(McpServerTool.Create(typeof(RotationPlanTools).GetMethod(nameof(Prepare))!,new RotationPlanTools(session)),session,schema,
            args=>RotationDefinition.Read(args["parameters"]!.AsObject()))];
    }
}
