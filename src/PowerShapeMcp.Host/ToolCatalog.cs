using ModelContextProtocol.Server;

namespace PowerShapeMcp;

public static class ToolCatalog
{
    public static McpServerTool[] Create(BridgeSession session)=>[..DiagnosticTools.Create(session),..ReadTools.Create(session)];
}
