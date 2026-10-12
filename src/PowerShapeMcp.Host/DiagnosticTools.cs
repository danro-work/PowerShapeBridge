using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PowerShapeMcp;

[McpServerToolType]
public sealed class DiagnosticTools(BridgeSession session)
{
    private const string IdentityDescription = "Lowercase 32-hex operation ID from an existing receipt; never a path or command.";
    [McpServerTool(Name = "bridge_status", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read local Bridge configuration and last worker phase. Does not contact PowerShape. This is not proof the panel is alive.")]
    public CallToolResult Status() => Result(session.Status());

    [McpServerTool(Name = "operation_result", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read a recorded operation journal by operationId without contacting PowerShape or retrying commands. Returns recorded state/phase and journal SHA256. Does not prove current model state or current hashes of model/export files.")]
    public CallToolResult OperationResult([Description(IdentityDescription)] string operationId)
    {
        try
        {
            var data = session.Journal.Read(operationId);
            return Result(data, data["state"]!.GetValue<string>() != "operation_receipt_read");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Result(new JsonObject { ["state"] = "operation_receipt_unavailable", ["operationId"] = operationId,
                ["outcome"] = "unknown", ["retryAllowed"] = false, ["error"] = ex.Message }, true);
        }
    }

    public static CallToolResult Result(JsonObject data, bool error = false) => new()
    { Content = [new TextContentBlock { Text = data.ToJsonString() }], StructuredContent = JsonSerializer.SerializeToElement(data), IsError = error };

    public static McpServerTool[] Create(BridgeSession session)
    {
        var methods = new DiagnosticTools(session);
        // Explicit allowlist. Neither assembly scanning nor SDK hints enforce our mode policy.
        return [new StrictDiagnosticTool(McpServerTool.Create(methods.Status), false),
                new StrictDiagnosticTool(McpServerTool.Create(methods.OperationResult), true)];
    }

    private sealed class StrictDiagnosticTool : DelegatingMcpServerTool
    {
        private readonly bool hasIdentity;
        public StrictDiagnosticTool(McpServerTool inner, bool hasIdentity) : base(inner)
        {
            this.hasIdentity = hasIdentity;
            // Fixed public schema is kept separate from native receipt identifiers.
            ProtocolTool.InputSchema = JsonSerializer.SerializeToElement(hasIdentity
                ? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["operationId"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[0-9a-f]{32}$", ["description"] = IdentityDescription } }, ["required"] = new JsonArray("operationId"), ["additionalProperties"] = false }
                : new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["additionalProperties"] = false });
        }

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
        {
            var args = request.Params?.Arguments;
            if (hasIdentity ? args is null || args.Count != 1 || !args.TryGetValue("operationId", out var id) ||
                id.ValueKind != JsonValueKind.String || !FileArtifacts.Token(id.GetString()) : args?.Count > 0)
                throw new McpProtocolException("Invalid tool arguments.", McpErrorCode.InvalidParams);
            return base.InvokeAsync(request, cancellationToken);
        }
    }
}
