using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class IpcTests
{
    [Theory]
    [InlineData("release")][InlineData("timeout")][InlineData("cancel")]
    public async Task LockedOriginalReceiptIsOnlyReadAgainWithinOriginalBudget(string finish)
    {
        var fixture=new Fixture();fixture.Write("PSBridge.Worker.exe","Opaque marker; never launched.");
        using var ipc=new BridgeIpc(fixture.Options(seconds:5));using var stop=new CancellationTokenSource();
        var call=ipc.SubmitAsync("recipe.prepare",new(){["recipeId"]="list_documents",["parameters"]=new JsonObject()},stop.Token);
        var request=await fixture.ClaimRequest();var id=Path.GetFileNameWithoutExtension(request);
        var response=FileArtifacts.FixedPath(fixture.Root,"responses",id+".json");Directory.CreateDirectory(Path.GetDirectoryName(response)!);
        var bytes=System.Text.Encoding.UTF8.GetBytes("{\"state\":\"documents_listed_verified\"}");
        var held=new FileStream(response,FileMode.CreateNew,FileAccess.Write,FileShare.None);held.Write(bytes);held.Flush(true);
        JsonObject result;
        try
        {
            await Task.Delay(75);Assert.False(call.IsCompleted); // File exists, but cannot yet be read.
            if(finish=="release"){held.Dispose();result=await call;}
            else{if(finish=="cancel")stop.Cancel();result=await call;}
        }
        finally{held.Dispose();}
        Assert.Equal(finish=="release"?"documents_listed_verified":finish=="cancel"?"cancelled_outcome_unknown":"timeout_outcome_unknown",result["state"]!.GetValue<string>());
        Assert.Equal(id,result["bridgeRequestId"]!.GetValue<string>());Assert.True(File.Exists(request+".processing"));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Root,"requests")));Assert.Equal(bytes,File.ReadAllBytes(response));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root,"requests"),"*.json")); // No new publication or CAD replay.
    }
    [Fact]
    public async Task FakePanelReceivesAtomicEnvelopeAndOnlyMatchedResponse()
    {
        var fixture = new Fixture(); fixture.Write("PSBridge.Worker.exe", "opaque fake marker; never launched");
        using var ipc = new BridgeIpc(fixture.Options());
        var resultTask = ipc.SubmitAsync("recipe.prepare",new JsonObject { ["recipeId"] = "list_documents", ["parameters"] = new JsonObject() });
        var path = await fixture.WaitRequest(); var id = Path.GetFileNameWithoutExtension(path);
        var request = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(id,request["requestId"]!.GetValue<string>());
        Assert.Equal("recipe.prepare",request["operation"]!.GetValue<string>());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));
        fixture.Write("responses/"+new string('b',32)+".json","{\"state\":\"succeeded_verified\"}");
        Assert.False(resultTask.IsCompleted);
        fixture.Write("responses/"+id+".json","{\"state\":\"documents_listed_verified\"}");
        var result = await resultTask;
        Assert.Equal("documents_listed_verified",result["state"]!.GetValue<string>());
        Assert.Equal(id,result["bridgeRequestId"]!.GetValue<string>());
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClaimedTimeoutOrCancellationNeverRepeatsOrDeletesProcessing(bool cancel)
    {
        var fixture = new Fixture(); fixture.Write("PSBridge.Worker.exe", "fake");
        // This case tests a *claimed* timeout/cancellation. Give publication and
        // the fake claim enough time on loaded CI; a 200ms fsync budget instead
        // intermittently tested pre-submission timeout. DeadlineTests separately
        // covers expiration before publication and while waiting for the gate.
        using var ipc = new BridgeIpc(fixture.Options(seconds: 5)); using var token = new CancellationTokenSource();
        var task = ipc.SubmitAsync("recipe.prepare",new JsonObject(),token.Token);
        var request = await fixture.WaitRequest();
        File.Move(request,request+".processing");
        if (cancel) token.Cancel();
        var result = await task;
        Assert.Equal(cancel ? "cancelled_outcome_unknown" : "timeout_outcome_unknown",result["state"]!.GetValue<string>());
        Assert.True(File.Exists(request+".processing"));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root,"requests"),"*.json"));
    }

    [Fact]
    public async Task MaintenanceAndEarlyCancellationNeverSubmit()
    {
        var fixture = new Fixture(); fixture.Write("PSBridge.Worker.exe", "fake"); fixture.Write("maintenance.json","{}");
        using var ipc = new BridgeIpc(fixture.Options());
        Assert.Equal("maintenance_before_submission",(await ipc.SubmitAsync("model.snapshot",null))["state"]!.GetValue<string>());
        Assert.Equal("cancelled_before_submission",(await ipc.SubmitAsync("model.snapshot",null,new CancellationToken(true)))["state"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
        await Assert.ThrowsAsync<ArgumentException>(() => ipc.SubmitAsync("execute_command",new JsonObject { ["command"] = "DELETE" }));
    }

    [Fact]
    public async Task PendingLimitIsEightAndCancelledWaitersDoNotSubmit()
    {
        var fixture = new Fixture(); fixture.Write("PSBridge.Worker.exe", "fake");
        using var ipc = new BridgeIpc(fixture.Options(seconds: 2));
        using var cancellation = new CancellationTokenSource();
        var first = ipc.SubmitAsync("model.snapshot", null, cancellation.Token);
        await fixture.WaitRequest();
        var waiters = Enumerable.Range(0,7).Select(_ => ipc.SubmitAsync("model.snapshot",null,cancellation.Token)).ToArray();
        Assert.Equal("too_many_pending_calls",(await ipc.SubmitAsync("model.snapshot",null))["state"]!.GetValue<string>());
        cancellation.Cancel();
        Assert.Equal("cancelled_outcome_unknown",(await first)["state"]!.GetValue<string>());
        foreach (var result in await Task.WhenAll(waiters)) Assert.Equal("cancelled_before_submission",result["state"]!.GetValue<string>());
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root,"requests"),"*.json"));
    }

    [Fact]
    public async Task WaitingCallsIncludeQueueTimeAndObserveCancellation()
    {
        var fixture = new Fixture(); fixture.Write("PSBridge.Worker.exe", "fake");
        using var ipc = new BridgeIpc(fixture.Options(seconds: .4));
        var first = ipc.SubmitAsync("model.snapshot",null);
        await fixture.WaitRequest();
        using var cancellation = new CancellationTokenSource();
        var next = ipc.SubmitAsync("model.snapshot",null,cancellation.Token);
        cancellation.Cancel();
        Assert.Equal("cancelled_before_submission",(await next)["state"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Root,"requests"),"*.json"));
        Assert.Equal("timeout_outcome_unknown",(await first)["state"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{\"state\":\"succeeded_verified\",\"bridgeRequestId\":\"wrong\"}")]
    [InlineData("{\"state\":true}")]
    [InlineData("{broken")]
    public async Task MalformedClaimedReceiptIsUnknown(string response)
    {
        var fixture = new Fixture(); fixture.Write("PSBridge.Worker.exe", "fake");
        using var ipc = new BridgeIpc(fixture.Options());
        var task = ipc.SubmitAsync("model.snapshot",null);
        var request = await fixture.WaitRequest(); var id = Path.GetFileNameWithoutExtension(request);
        File.Move(request,request+".processing"); fixture.Write("responses/"+id+".json",response);
        Assert.Equal("ipc_failure_outcome_unknown",(await task)["state"]!.GetValue<string>());
        Assert.True(File.Exists(request+".processing"));
    }
}
