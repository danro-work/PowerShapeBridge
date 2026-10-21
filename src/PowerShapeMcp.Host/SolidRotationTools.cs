using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class SolidRotationTools(BridgeSession session)
{
    [McpServerTool(Name="prepare_owned_solid_rotation",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Experimental: prepare one owned SOLID, the only model entity, for a nonzero integer WORLD rotation from -359 through 359 degrees in mm/factor1. Explicit pivot/direction required. Pins face UV positions/normals and immutable proofs; no rotation, selection, save or activation. Separate feature flag; absent from read-only. Fractional angles, sets and other types are unsupported.")]
    public Task<CallToolResult> Prepare([Description("Passive rotationPlanHandle from this connection; explicit integer angle and WORLD axis.")]string rotationPlanHandle,[Description("Fresh binding handle to this connection's owned working document.")]string workingDocumentBindingHandle,CancellationToken cancellationToken)=>new SolidQuarterTurnTools(session).RunGeneral("prepare_owned_solid_rotation",new(){["rotationPlanHandle"]=rotationPlanHandle,["workingDocumentBindingHandle"]=workingDocumentBindingHandle},cancellationToken);

    [McpServerTool(Name="execute_owned_solid_rotation",ReadOnly=false,Destructive=true,Idempotent=false,OpenWorld=false)]
    [Description("Execute the prepared owned single-SOLID integer WORLD rotation exactly once. Consumes preparation/CAD leases before submission; one RotateItem, no selection/save/activation. Result remains unverified and fenced. Unknown outcomes must never be repeated or compensated by an inverse rotation. Approved owned-copy operations only; absent from read-only.")]
    public Task<CallToolResult> Execute([Description("Unconsumed ownedRotationHandle from prepare_owned_solid_rotation in this connection.")]string ownedRotationHandle,CancellationToken cancellationToken)=>new SolidQuarterTurnTools(session).RunGeneral("execute_owned_solid_rotation",new(){["ownedRotationHandle"]=ownedRotationHandle},cancellationToken);

    [McpServerTool(Name="verify_owned_solid_rotation",ReadOnly=false,Destructive=false,Idempotent=false,OpenWorld=false)]
    [Description("Independently verify the original single-SOLID integer rotation using immutable receipt and fresh candidate/listing handles. Checks pinned UV points/normals, COG/raw metrics, two-way conservative bounds, ownership/files/context and journal. Explicit engineering profile: face/normals 2e-6 absolute, COG 1e-5 mm, raw volume 1e-6 absolute plus 1ppm relative. No full topology or persistence claim. Releases only a verified matching fence; never rotates/replays/saves/activates. Full server only.")]
    public Task<CallToolResult> Verify([Description("Original execution request ID; never a new intent.")]string executionRequestId,[Description("Exact lowercase SHA256 of the original raw execution receipt.")]string executionReceiptSha256,[Description("Fresh candidate document handle from inspection after command return.")]string candidateDocumentHandle,[Description("Fresh active document choice from a listing after command return.")]string activeDocumentHandle,CancellationToken cancellationToken)=>new SolidQuarterTurnTools(session).RunGeneral("verify_owned_solid_rotation",new(){["executionRequestId"]=executionRequestId,["executionReceiptSha256"]=executionReceiptSha256,["candidateDocumentHandle"]=candidateDocumentHandle,["activeDocumentHandle"]=activeDocumentHandle},cancellationToken);

    public static McpServerTool[] Create(BridgeSession session)
    {
        if(!session.PreparedEnabled("prepare_owned_solid_rotation"))return[];
        var tools=new SolidRotationTools(session);
        return [new StrictTool(McpServerTool.Create(tools.Prepare),session,SolidQuarterTurnTools.Schema("rotationPlanHandle","workingDocumentBindingHandle")),new StrictTool(McpServerTool.Create(tools.Execute),session,SolidQuarterTurnTools.Schema("ownedRotationHandle")),new StrictTool(McpServerTool.Create(tools.Verify),session,SolidQuarterTurnTools.Schema("executionRequestId","executionReceiptSha256","candidateDocumentHandle","activeDocumentHandle"))];
    }
}
