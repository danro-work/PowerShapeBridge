using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class DiagnosticsTests
{
    [Theory]
    [InlineData("2025-03-26", false)]
    [InlineData("2025-11-25", true)]
    [InlineData("2026-07-28", true)]
    public async Task RealSdkNegotiationCatalogAndStrictArguments(string protocol, bool readOnly)
    {
        var fixture = new Fixture();
        var session = new BridgeSession(fixture.Options(readOnly));
        var toServer = new Pipe(); var toClient = new Pipe();
        await using var server = McpServer.Create(new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()),
            new McpServerOptions { ServerInfo = new() { Name = "powershape-bridge", Version = HostOptions.Version }, ToolCollection = [.. DiagnosticTools.Create(session)] });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = server.RunAsync(stop.Token);
        await using (var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()),
                         new McpClientOptions { ProtocolVersion = protocol }, cancellationToken: stop.Token))
        {
            Assert.Equal("powershape-bridge", client.ServerInfo.Name);
            var tools = await client.ListToolsAsync(cancellationToken: stop.Token);
            Assert.Equal(new[] { "bridge_status", "operation_result" }, tools.Select(t => t.Name).Order());
            foreach (var tool in tools)
            {
                Assert.NotEmpty(tool.Description!);
                Assert.False(tool.JsonSchema.GetProperty("additionalProperties").GetBoolean());
            }
            Assert.NotEmpty(tools.Single(t => t.Name == "operation_result").JsonSchema.GetProperty("properties").GetProperty("operationId").GetProperty("description").GetString()!);
            var status = await client.CallToolAsync("bridge_status", cancellationToken: stop.Token);
            Assert.False(status.IsError);
            var data = status.StructuredContent!.Value;
            Assert.Equal(readOnly ? "read_only" : "general_core", data.GetProperty("serverMode").GetString());
            Assert.Equal("not_verified", data.GetProperty("panelLiveness").GetString());
            Assert.Equal("disabled", data.GetProperty("geometryMutations").GetString());
            foreach (var (name, args) in new[]
                     {
                         ("bridge_status", new Dictionary<string, object?> { ["command"] = "DELETE" }),
                         ("operation_result", new Dictionary<string, object?>()),
                         ("operation_result", new Dictionary<string, object?> { ["operationId"] = "../bad" }),
                         ("operation_result", new Dictionary<string, object?> { ["operationId"] = true }),
                         ("operation_result", new Dictionary<string, object?> { ["operationId"] = new string('a',32), ["path"] = "bad" })
                     })
            {
                var exception = await Assert.ThrowsAsync<McpProtocolException>(() => client.CallToolAsync(name, args, cancellationToken: stop.Token).AsTask());
                Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
            }
            // Forging an unavailable mutation name must not reach IPC in either mode.
            await Assert.ThrowsAsync<McpProtocolException>(() => client.CallToolAsync("execute_owned_translation", cancellationToken: stop.Token).AsTask());
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, "requests")));
        }
        stop.Cancel();
        try { await run; } catch (OperationCanceledException) { }
    }

    [Theory]
    [InlineData("captureViewport", "true")]
    [InlineData("mcpMutationsPanel", "true")]
    [InlineData("sourceStep", "\"C:/original.step\"")]
    [InlineData("entitySetsExperimental", "1")]
    [InlineData("ownedTranslationsExperimental", "true")]
    [InlineData("ownedSaveTargetsExperimental", "true")]
    [InlineData("ownedSavesExperimental", "true")]
    [InlineData("ownedSaveTargetsExperimental", "\"true\"")]
    [InlineData("ownedSavesExperimental", "\"true\"")]
    [InlineData("timeoutSeconds", "true")]
    [InlineData("operationTimeoutSeconds", "111")]
    [InlineData("maxEntities", "0")]
    [InlineData("expectedVersion", "\"2024\"")]
    public void UnsafeConfigurationFailsBeforeAnyQueue(string field, string json)
    {
        var fixture = new Fixture();
        fixture.Config[field] = System.Text.Json.Nodes.JsonNode.Parse(json);
        fixture.SaveConfig();
        Assert.Throws<InvalidDataException>(() => HostOptions.Parse(["--bridge-root", fixture.Root]));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "requests")));
    }

    [Fact]
    public void ReceiptHashUsesOriginalBytesAndMissingReceiptIsError()
    {
        var fixture = new Fixture();
        var id = new string('a', 32);
        var raw = "{ \"operationId\": \"" + id + "\", \"state\": \"executed_unverified\", \"name\": \"Тест\" }\r\n";
        fixture.Write("operations/" + id + ".json", raw);
        var result = new DiagnosticTools(new BridgeSession(fixture.Options())).OperationResult(id);
        Assert.False(result.IsError);
        Assert.Equal(FileArtifacts.Sha256(System.Text.Encoding.UTF8.GetBytes(raw)), result.StructuredContent!.Value.GetProperty("receiptSha256").GetString());
        Assert.True(new DiagnosticTools(new BridgeSession(fixture.Options())).OperationResult(new string('b', 32)).IsError);
    }
}
