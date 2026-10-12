using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class RecoveryTools(BridgeSession session)
{
    [McpServerTool(Name="inspect_operation_recovery",ReadOnly=true,Destructive=false,Idempotent=true,OpenWorld=false),Description("Inspect one recorded owned operation's journal, plan, claim/fence and current saved working/checkpoint bytes. Distinguishes missing authoritative return, non-success code, changed protected files and historical verification. Never resolves unknown from disk alone, grants execution, retries commands, contacts CAD, rewrites journals or removes claims/fences. Enabled only by recoveryInspectionExperimental; available in the separate read-only server.")]
    public async Task<CallToolResult> Inspect([Description("Lowercase 32-hex original owned operation ID; never a path or command.")]string operationId,CancellationToken cancellationToken)
    {
        session.AssertToolAllowed("inspect_operation_recovery");var timer=Stopwatch.StartNew();bool acquired=false;
        try
        {
            acquired=await session.ReferenceGate.WaitAsync(session.Options.Timeout,cancellationToken);
            if(!acquired)return DiagnosticTools.Result(new(){["state"]="recovery_inspection_unavailable",["errorCode"]="recovery_gate_timeout",["retryAllowed"]=false,["executionAuthorized"]=false},true);
            var result=new RecoveryInspection(session.Options.BridgeRoot).Read(operationId,session.Options.Timeout-timer.Elapsed,cancellationToken);
            return DiagnosticTools.Result(result,result["state"]?.GetValue<string>()!="recovery_inspected_recorded");
        }
        catch(OperationCanceledException)when(cancellationToken.IsCancellationRequested){return DiagnosticTools.Result(new(){["state"]="recovery_inspection_cancelled",["retryAllowed"]=false,["executionAuthorized"]=false},true);}
        finally{if(acquired)session.ReferenceGate.Release();}
    }
    public static McpServerTool[] Create(BridgeSession session)=>session.RecoveryInspectionEnabled?[new StrictTool(McpServerTool.Create(new RecoveryTools(session).Inspect),session,StrictTool.Token("operationId","Lowercase 32-hex original owned operation ID; not a path or command."))]:[];
}
