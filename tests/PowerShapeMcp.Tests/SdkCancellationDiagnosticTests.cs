using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class SdkCancellationDiagnosticTests
{
    [Theory]
    [InlineData("2026-07-28")]
    [InlineData("2025-11-25")]
    public async Task ExplicitCancellationNotificationCancelsPlainSdkHandler(string version)
    {
        var started=new TaskCompletionSource<(CancellationToken Token,RequestId Id)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received=new System.Collections.Concurrent.ConcurrentQueue<string>();
        var input=new Pipe();var output=new Pipe();using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[McpServerTool.Create(async(RequestContext<CallToolRequestParams> request,CancellationToken token)=>{started.SetResult((token,request.JsonRpcRequest.Id));await Task.Delay(10000,token);return "complete";},new(){Name="probe"})]});
        await using var observer=server.RegisterNotificationHandler("notifications/cancelled",(notification,_)=>{received.Enqueue(notification.Params?.ToJsonString()??"null");return ValueTask.CompletedTask;});
        var run=server.RunAsync(stop.Token);
        await using var client=await McpClient.CreateAsync(new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream()),new(){ProtocolVersion=version},cancellationToken:stop.Token);
        using var cancellation=new CancellationTokenSource();
        var call=client.CallToolAsync("probe",cancellationToken:cancellation.Token).AsTask();
        var invocation=await started.Task.WaitAsync(TimeSpan.FromSeconds(5));cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>call);
        await TransportCancellationProof.Notify(client,invocation.Id);
        var observed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration=invocation.Token.Register(()=>observed.TrySetResult());
        try{Assert.True(await Task.WhenAny(observed.Task,Task.Delay(2000))==observed.Task,$"Handler {invocation.Id}: received cancellations [{string.Join(";",received)}], token.CanBeCanceled={invocation.Token.CanBeCanceled}");}
        finally{stop.Cancel();try{await run;}catch(OperationCanceledException){}}
    }
}
