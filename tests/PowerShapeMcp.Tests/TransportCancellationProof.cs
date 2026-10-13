using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

// Observes real SDK cancellation without treating a cancelled client as a native
// command return. Explicit wire cancellation must release the server gate
// promptly; a local-only cancellation remains bounded by the server deadline.
internal static class TransportCancellationProof
{
    internal sealed class Recorder
    {
        internal RequestId RequestId { get; private set; }
        internal McpServerTool[] Wrap(McpServerTool[] tools)=>tools.Select(tool=>(McpServerTool)new Observer(tool,this)).ToArray();
        private sealed class Observer(McpServerTool inner,Recorder recorder):DelegatingMcpServerTool(inner)
        {
            public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request,CancellationToken cancellationToken=default)
            {recorder.RequestId=request.JsonRpcRequest.Id;return base.InvokeAsync(request,cancellationToken);}
        }
    }

    internal static Task Notify(McpClient client,RequestId id)=>client.SendMessageAsync(new JsonRpcNotification
    {
        Method="notifications/cancelled",Params=new JsonObject
        {
            ["requestId"]=id.Id is long number?JsonValue.Create(number):JsonValue.Create(id.Id?.ToString())
        }
    });

    internal static Dictionary<string,string> Snapshot(string root)=>
        Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(
            path=>path,path=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    internal static void Unchanged(string root,Dictionary<string,string> before)
    {
        var after=Snapshot(root);Assert.Equal(before.Keys.Order(),after.Keys.Order());
        foreach(var pair in before)Assert.Equal(pair.Value,after[pair.Key]);
    }

    internal static void Record(string root,string id,string recipe,string fault,string state,Dictionary<string,string> before)
    {
        Unchanged(root,before);
        var output=Environment.GetEnvironmentVariable("PSBRIDGE_TEST_EVIDENCE");
        if(string.IsNullOrWhiteSpace(output))return;
        Directory.CreateDirectory(output);
        var archive=Path.Combine(output,id,"artifacts");
        Assert.False(Directory.Exists(archive));
        foreach(var pair in before)
        {
            var destination=Path.Combine(archive,Path.GetRelativePath(root,pair.Key));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(pair.Key,destination);
            Assert.Equal(pair.Value,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))));
        }
        var report=new
        {
            schema="psbridge.sdk-transport-fault.v1",root,artifactArchive=id+"/artifacts",requestId=id,recipe,fault,state,
            cadConnectionAttempted=false,syntheticNativeProof=true,replayRejected=true,
            newAuthorityGranted=false,files=before.Select(pair=>new
            {
                path=Path.GetRelativePath(root,pair.Key),sha256=pair.Value.ToLowerInvariant()
            }).OrderBy(value=>value.path).ToArray()
        };
        using var stream=new FileStream(Path.Combine(output,id+".json"),FileMode.CreateNew,FileAccess.Write,FileShare.None);
        JsonSerializer.Serialize(stream,report);stream.Flush(true);
    }

    internal static async Task CancelClaimed(Task<CallToolResult> call,
        CancellationTokenSource cancellation,BridgeSession session,string root,string id,
        McpClient client,Recorder recorder,bool notify)
    {
        Assert.Equal(0,session.ReferenceGate.CurrentCount);
        Assert.True(session.Options.Timeout>=TimeSpan.FromSeconds(5));
        var before=Snapshot(root);cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>call.WaitAsync(TimeSpan.FromSeconds(2)));
        if(notify)await Notify(client,recorder.RequestId);
        Assert.True(await session.ReferenceGate.WaitAsync(notify?TimeSpan.FromSeconds(2):session.Options.Timeout+TimeSpan.FromSeconds(2)),
            notify?"Explicit cancellation must reach the server before its IPC deadline.":"A client-only cancellation must still release the server gate by its own deadline.");
        session.ReferenceGate.Release();
        Assert.True(File.Exists(FileArtifacts.FixedPath(root,"requests",id+".json.processing")));
        Assert.False(File.Exists(FileArtifacts.FixedPath(root,"responses",id+".json")));
        Assert.Single(Directory.GetFiles(Path.Combine(root,"requests")));
        Unchanged(root,before);
    }
}
