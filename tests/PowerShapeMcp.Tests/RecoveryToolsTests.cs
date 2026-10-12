using System.IO.Pipelines;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class RecoveryToolsTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task ExplicitFlagAddsOneFilesystemOnlyToolInBothModes(bool readOnly)
    {
        var fixture=new Fixture();fixture.Config["recoveryInspectionExperimental"]=true;fixture.SaveConfig();using var session=new BridgeSession(fixture.Options(readOnly,5));
        var input=new Pipe();var output=new Pipe();using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(session)]});var run=server.RunAsync(stop.Token);
        await using var client=await McpClient.CreateAsync(new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream()),cancellationToken:stop.Token);
        var tools=await client.ListToolsAsync();var recovery=tools.Single(t=>t.Name=="inspect_operation_recovery");Assert.True(recovery.ProtocolTool.Annotations!.ReadOnlyHint);Assert.False(recovery.JsonSchema.GetProperty("additionalProperties").GetBoolean());
        await Assert.ThrowsAsync<McpProtocolException>(()=>client.CallToolAsync(recovery.Name,new Dictionary<string,object?>{["operationId"]="../source",["command"]="UNDO"}).AsTask());
        var result=await client.CallToolAsync(recovery.Name,new Dictionary<string,object?>{["operationId"]=Guid.NewGuid().ToString("N")});Assert.True(result.IsError);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));Assert.False(Directory.Exists(Path.Combine(fixture.Root,"operations")));
        stop.Cancel();try{await run;}catch(OperationCanceledException){}
    }
    [Fact]
    public void DisabledOrNonbooleanFlagNeverAdmitsTool()
    {
        var fixture=new Fixture();using var session=new BridgeSession(fixture.Options());Assert.Throws<McpProtocolException>(()=>session.AssertToolAllowed("inspect_operation_recovery"));
        fixture.Config["recoveryInspectionExperimental"]="true";fixture.SaveConfig();Assert.Throws<InvalidDataException>(()=>fixture.Options().ReadConfiguration());
    }
}
