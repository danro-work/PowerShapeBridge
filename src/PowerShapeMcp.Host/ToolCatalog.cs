using ModelContextProtocol.Server;

namespace PowerShapeMcp;

public static class ToolCatalog
{
    public static McpServerTool[] Create(BridgeSession session)=>[..DiagnosticTools.Create(session),..ReadTools.Create(session),..LevelReadTools.Create(session),..OwnedLevelMoveTools.Create(session),..OwnedWorkplaneTools.Create(session),..OwnedWorkplaneActivationTools.Create(session),..PreparedTools.Create(session),..ExecutionTools.Create(session),..OwnedSaveTools.Create(session),..OwnedSavedReopenTools.Create(session),..SavedCopyCloseTools.Create(session),..SavedResultCloseTools.Create(session),..RecoveryTools.Create(session),..CheckpointRecoveryTools.Create(session),..RotationPlanTools.Create(session),..OwnedRotationTools.Create(session),..SolidQuarterTurnTools.Create(session),..SolidRotationTools.Create(session),..UniformScalePlanTools.Create(session),..OwnedUniformScaleTools.Create(session),..SolidScaleTools.Create(session),..ScaleRestoreTools.Create(session),..OwnedOpenAdoptionTools.Create(session),..SavedResultRestoreTools.Create(session),..OwnedDgkTargetTools.Create(session)];
}
