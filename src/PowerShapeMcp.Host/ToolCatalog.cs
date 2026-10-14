using ModelContextProtocol.Server;

namespace PowerShapeMcp;

public static class ToolCatalog
{
    public static McpServerTool[] Create(BridgeSession session)=>[..DiagnosticTools.Create(session),..ReadTools.Create(session),..PreparedTools.Create(session),..ExecutionTools.Create(session),..OwnedSaveTools.Create(session),..OwnedSavedReopenTools.Create(session),..RecoveryTools.Create(session),..CheckpointRecoveryTools.Create(session),..RotationPlanTools.Create(session),..OwnedRotationTools.Create(session),..UniformScalePlanTools.Create(session),..OwnedUniformScaleTools.Create(session)];
}
