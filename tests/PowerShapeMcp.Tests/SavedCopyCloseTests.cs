using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class SavedCopyCloseTests
{
    internal sealed class Case
    {
        internal readonly SavedReopenFixture Reopen=new(close:true);
        internal BridgeSession Session=>Reopen.Session;
        internal string Root=>Reopen.Root;
        internal string Binding;
        internal JsonObject Original=null!,Plan=null!;
        internal string Operation="",OriginalSha="";
        private string? prior;private int sequence;
        internal Case()
        {
            var a=Reopen.Data.Saved.FreshReads();var input=new JsonObject{["ownedReopenHandle"]=Reopen.Handle,["candidateDocumentHandle"]=a["candidateDocumentHandle"]!.DeepClone(),["activeDocumentHandle"]=a["activeDocumentHandle"]!.DeepClone()};
            var p=OwnedExecutionProof.SavedReopenParameters(input,Session.Candidates,Session.Reopens,Session.Documents);Reopen.Execution(Guid.NewGuid().ToString("N"),p);
            var fresh=Reopen.FreshReads();var args=OwnedExecutionProof.VerificationParameters(fresh,Session.Candidates,Session.Documents,false,false,true);var raw=Reopen.Verification(Guid.NewGuid().ToString("N"),args);
            Binding=OwnedReopenProof.IssueBinding(Session,raw,args,fresh["candidateDocumentHandle"]!.GetValue<string>())["workingDocumentBindingHandle"]!.GetValue<string>();
        }
        internal JsonObject SourceWindow()=>new(){["modelId"]=Reopen.Data.Saved.Verification["model"]!["id"]!.DeepClone(),["windowNumber"]=1,["nativeWindowId"]=50,["modelName"]=Reopen.Data.Saved.Verification["model"]!["name"]!.DeepClone(),["modelPath"]=Reopen.Data.Saved.Verification["model"]!["path"]!.DeepClone(),["type"]="Model",["selectedRaw"]=0};
        internal JsonObject ExecuteInput()
        {
            var a=Reopen.FreshReads();var oldChoice=Session.Documents.Consume(a["activeDocumentHandle"]!.GetValue<string>());var listing=ArtifactProof.Verified(Root,"responses",oldChoice["listingRequestId"]!.GetValue<string>(),oldChoice["listingSha256"]!.GetValue<string>(),16777216);var target=listing["activeWindow"]!.DeepClone();listing["windows"]=new JsonArray(SourceWindow(),target);listing["windowCount"]=2;
            var id=Guid.NewGuid().ToString("N");listing["bridgeRequestId"]=id;var sha=ArtifactProof.Freeze(Root,"responses/"+id+".json",listing);var handle=Session.Documents.Issue(listing,sha)["documents"]![0]!["documentHandle"]!.GetValue<string>();
            return new(){["workingDocumentBindingHandle"]=Binding,["candidateDocumentHandle"]=a["candidateDocumentHandle"]!.DeepClone(),["activeDocumentHandle"]=handle,["disposition"]="discard_owned_saved_copy"};
        }
        internal JsonObject After(JsonObject before){var after=before.DeepClone().AsObject();var row=SourceWindow();row["selectedRaw"]=1;row["nativeWindowId"]=49;after["windows"]=new JsonArray(row.DeepClone());after["activeWindow"]=row;after["windowCount"]=1;return after;}
        private void Append(string state,JsonObject details){var e=new JsonObject{["schemaVersion"]=1,["operationId"]=Operation,["planSha256"]=Original["planSha256"]!.DeepClone(),["sequence"]=sequence,["previousSha256"]=prior,["state"]=state,["timestampUtc"]=LeaseTime.Format(Reopen.Data.Target.Data.Utc),["details"]=details};prior=ArtifactProof.Freeze(Root,$"operations/{Operation}/{sequence:D6}.json",e);sequence++;}
        internal JsonObject Execute(string id,JsonObject parameters,bool publish=true,bool unknown=false,bool returned=true)
        {
            Operation=id;var before=ArtifactProof.Verified(Root,"responses",parameters["listingRequestId"]!.GetValue<string>(),parameters["listingSha256"]!.GetValue<string>(),16777216);Plan=parameters.DeepClone().AsObject();Plan["schemaVersion"]=1;Plan["operationId"]=id;Plan["operation"]="close_disposable_saved_reopen_copy";Plan["before"]=before;Plan["savedFileHashesBefore"]=new JsonObject();Plan["command"]="FILE CLOSE SELECTED YES";Plan["fullTopologyEquivalent"]=false;
            var planSha=ArtifactProof.Freeze(Root,"ownedcloseplans/"+id+".json",Plan);string copy=parameters["copyId"]!.GetValue<string>();var claimSha=ArtifactProof.Freeze(Root,"workingcopies/"+copy+"/close-once.json",new(){["operationId"]=id,["planSha256"]=planSha,["automaticReplayForbidden"]=true});var fenceSha=ArtifactProof.Freeze(Root,"workingcopies/"+copy+"/mutation-fence.json",new(){["operationId"]=id,["planSha256"]=planSha,["reason"]="test"});
            Original=new(){["state"]=unknown?"owned_saved_copy_close_outcome_unknown":"owned_saved_copy_closed_unverified",["operationId"]=id,["copyId"]=copy,["planSha256"]=planSha,["before"]=before.DeepClone(),["after"]=After(before),["executionClaimSha256"]=claimSha,["executionFenceSha256"]=fenceSha,["contextChangeAttempted"]=true,["cadSaveAttempted"]=false,["geometryMutationAttempted"]=false,["nativeProviderQualified"]=true,["queueQualifiedRoute"]=true,["coreOnlyQualification"]=false,["savedFilesUnchanged"]=true,["checkpointVerified"]=true,["recordedJournalState"]=unknown?"outcome_unknown":"executed_unverified"};
            Append("prepared",new());Append("execution_claimed",new());Append("command_started",new());if(returned)Append("command_returned",new(){["nativeRequestId"]=id});Append(unknown?"outcome_unknown":"executed_unverified",new());
            OriginalSha=ArtifactProof.Freeze(Root,"responses/"+id+(publish?".json":".unpublished"),Original);var reply=Original.DeepClone().AsObject();reply["bridgeRequestId"]=id;return reply;
        }
        internal JsonObject VerifyInput()
        {
            Reopen.Data.Target.Data.Time+=10;Reopen.Data.Target.Data.Utc=Reopen.Data.Target.Data.Utc.AddSeconds(10);var inspection=Reopen.Data.Saved.FreshReads();var candidate=Session.Candidates.DocumentParameters(inspection["candidateDocumentHandle"]!.GetValue<string>());var snapshot=ArtifactProof.Verified(Root,"responses",candidate["inspectionRequestId"]!.GetValue<string>(),candidate["inspectionSha256"]!.GetValue<string>(),16777216);
            var listing=After(Plan["before"]!.AsObject());listing["timestampUtc"]=snapshot["timestampUtc"]!.DeepClone();var active=listing["activeWindow"]!.AsObject();listing["documents"]=new JsonArray(new JsonObject{["modelId"]=active["modelId"]!.DeepClone(),["windowNumber"]=1,["activationSupported"]=true});var id=Guid.NewGuid().ToString("N");listing["bridgeRequestId"]=id;var sha=ArtifactProof.Freeze(Root,"responses/"+id+".json",listing);var choice=Session.Documents.Issue(listing,sha)["documents"]![0]!["documentHandle"]!.GetValue<string>();
            return new(){["closedCopyId"]=Plan["copyId"]!.DeepClone(),["closeRequestId"]=Operation,["closeReceiptSha256"]=OriginalSha,["candidateDocumentHandle"]=inspection["candidateDocumentHandle"]!.DeepClone(),["activeDocumentHandle"]=choice};
        }
        internal JsonObject Verify(string id,JsonObject parameters)
        {
            var evidence=new JsonArray();foreach(var pair in new[]{("closeRequestId","closeReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})evidence.Add(new JsonObject{["requestId"]=parameters[pair.Item1]!.DeepClone(),["sha256"]=parameters[pair.Item2]!.DeepClone()});Append("succeeded_verified",new(){["evidence"]=evidence,["noCommandReplay"]=true});File.Delete(FileArtifacts.FixedPath(Root,"workingcopies",Plan["copyId"]!.GetValue<string>(),"mutation-fence.json"));
            var r=new JsonObject{["state"]="owned_saved_copy_close_verified",["verificationId"]=id,["operationId"]=Operation,["copyId"]=Plan["copyId"]!.DeepClone(),["planSha256"]=Original["planSha256"]!.DeepClone(),["recordedJournalState"]="succeeded_verified",["nativeProviderQualified"]=true,["queueQualifiedRoute"]=true,["coreOnlyQualification"]=false,["savedFilesUnchanged"]=true,["noCommandReplay"]=true,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,["geometryMutationAttempted"]=false};ArtifactProof.Freeze(Root,"responses/"+id+".json",r);r["bridgeRequestId"]=id;return r;
        }
    }
    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,true)]public void FlagAndReadOnly(bool readOnly,bool enabled)
    {var f=new Case();f.Reopen.Data.Target.Data.Files.Config["ownedSavedCopyCloseExperimental"]=enabled;f.Reopen.Data.Target.Data.Files.SaveConfig();using var s=new BridgeSession(f.Reopen.Data.Target.Data.Files.Options(readOnly));Assert.Equal(enabled&&!readOnly,ToolCatalog.Create(s).Any(t=>t.ProtocolTool.Name=="execute_owned_saved_copy_close"));}
    [Theory][InlineData("\"true\"")][InlineData("1")][InlineData("null")]public void MalformedFlag(string raw){var f=new Case();f.Reopen.Data.Target.Data.Files.Config["ownedSavedCopyCloseExperimental"]=JsonNode.Parse(raw);f.Reopen.Data.Target.Data.Files.SaveConfig();Assert.Throws<InvalidDataException>(()=>f.Reopen.Data.Target.Data.Files.Options().ReadConfiguration());}
    [Fact]public void CurrentBindingAndExactDispositionRequired(){var f=new Case();var a=f.ExecuteInput();a["disposition"]="close_all";Assert.Throws<ReferenceRejected>(()=>SavedCopyCloseProof.ExecuteParameters(f.Session,a));a["disposition"]="discard_owned_saved_copy";f.Session.Bindings.Clear();Assert.Throws<ReferenceRejected>(()=>SavedCopyCloseProof.ExecuteParameters(f.Session,a));var requests=Path.Combine(f.Root,"requests");Assert.True(!Directory.Exists(requests)||Directory.GetFiles(requests).Length==0);}
    [Theory][InlineData("raw")][InlineData("window")][InlineData("claim")][InlineData("fence")][InlineData("return")]
    public void RejectCorruptExecutionProof(string fault){var f=new Case();var p=SavedCopyCloseProof.ExecuteParameters(f.Session,f.ExecuteInput());var r=f.Execute(Guid.NewGuid().ToString("N"),p,returned:fault!="return");if(fault=="raw")r["savedFilesUnchanged"]=false;if(fault=="window")r["after"]!["windows"]![0]!["modelName"]="other";if(fault is"claim"or"fence")File.AppendAllText(FileArtifacts.FixedPath(f.Root,"workingcopies",p["copyId"]!.GetValue<string>(),fault=="claim"?"close-once.json":"mutation-fence.json")," ");Assert.Throws<ReferenceRejected>(()=>SavedCopyCloseProof.Execution(f.Session,r,p));}
    [Theory][InlineData(false)][InlineData(true)]public void IndependentVerificationChecksReturnedOriginal(bool unknown){var f=new Case();var p=SavedCopyCloseProof.ExecuteParameters(f.Session,f.ExecuteInput());var r=f.Execute(Guid.NewGuid().ToString("N"),p,unknown:unknown);if(!unknown)SavedCopyCloseProof.Execution(f.Session,r,p);f.Session.RevokeCadLeases(true);var args=SavedCopyCloseProof.VerifyParameters(f.Session,f.VerifyInput());var v=f.Verify(Guid.NewGuid().ToString("N"),args);SavedCopyCloseProof.Verification(f.Session,v,args);Assert.Null(v["workingDocumentBindingHandle"]);}

    private sealed class Connection:IAsyncDisposable
    {
        internal readonly Case Data=new();internal McpClient Client=null!;internal readonly TransportCancellationProof.Recorder Recorder=new();private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(60));private readonly McpServer server;private readonly Task run;private readonly StreamClientTransport transport;
        private Connection(){Data.Reopen.Data.Target.Data.Files.Write("PSBridge.Worker.exe","Opaque marker; never executed.");var i=new Pipe();var o=new Pipe();server=McpServer.Create(new StreamServerTransport(i.Reader.AsStream(),o.Writer.AsStream()),new McpServerOptions{ToolCollection=[..Recorder.Wrap(ToolCatalog.Create(Data.Session))]});run=server.RunAsync(stop.Token);transport=new(i.Writer.AsStream(),o.Reader.AsStream());}
        internal static async Task<Connection> Create(){var c=new Connection();c.Client=await McpClient.CreateAsync(c.transport,cancellationToken:c.stop.Token);return c;}
        internal async Task<(string,JsonObject)> Claim(string recipe){var file=await Data.Reopen.Data.Target.Data.Files.ClaimRequest();var n=JsonNode.Parse(await Data.Reopen.Data.Target.Data.Files.ReadClaimedRequest(file))!;Assert.Equal(recipe,n["arguments"]!["recipeId"]!.GetValue<string>());return(Path.GetFileNameWithoutExtension(file),n["arguments"]!["parameters"]!.AsObject());}
        public async ValueTask DisposeAsync(){await Client.DisposeAsync();stop.Cancel();await server.DisposeAsync();try{await run;}catch(OperationCanceledException){}Data.Session.Dispose();stop.Dispose();}
    }
    private static Dictionary<string,object?> Args(JsonObject v)=>v.ToDictionary(p=>p.Key,p=>(object?)p.Value!.GetValue<string>());
    [Fact]public async Task OfficialSdkOneCloseThenIndependentVerifyNoNewAuthority()
    {
        await using var c=await Connection.Create();var tools=await c.Client.ListToolsAsync();var schema=tools.Single(t=>t.Name=="execute_owned_saved_copy_close").JsonSchema;Assert.False(schema.GetProperty("additionalProperties").GetBoolean());Assert.Equal("discard_owned_saved_copy",schema.GetProperty("properties").GetProperty("disposition").GetProperty("enum")[0].GetString());
        await Assert.ThrowsAsync<McpProtocolException>(()=>c.Client.CallToolAsync("execute_owned_saved_copy_close",new Dictionary<string,object?>{["command"]="FILE CLOSE ALL"}).AsTask());
        var input=c.Data.ExecuteInput();var call=c.Client.CallToolAsync("execute_owned_saved_copy_close",Args(input)).AsTask();var(id,p)=await c.Claim("execute_owned_saved_copy_close");Assert.Equal(10,p.Count);c.Data.Execute(id,p);var reply=await call;Assert.False(reply.IsError,reply.StructuredContent?.GetRawText());Assert.Throws<ReferenceRejected>(()=>c.Data.Session.Bindings.ValidationParameters(c.Data.Binding));Assert.True((await c.Client.CallToolAsync("execute_owned_saved_copy_close",Args(input))).IsError);
        var after=c.Data.VerifyInput();var verify=c.Client.CallToolAsync("verify_owned_saved_copy_close",Args(after)).AsTask();var(vid,vp)=await c.Claim("verify_owned_saved_copy_close");Assert.Equal(8,vp.Count);c.Data.Verify(vid,vp);var verified=await verify;Assert.False(verified.IsError,verified.StructuredContent?.GetRawText());Assert.DoesNotContain("workingDocumentBindingHandle",verified.StructuredContent!.Value.GetRawText());Assert.Equal(2,Directory.GetFiles(Path.Combine(c.Data.Root,"requests")).Length);
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task CancellationKeepsClaimAndCannotReplay(bool notify)
    {
        await using var c=await Connection.Create();var input=c.Data.ExecuteInput();using var cancel=new CancellationTokenSource();var call=c.Client.CallToolAsync("execute_owned_saved_copy_close",Args(input),cancellationToken:cancel.Token).AsTask();var(id,p)=await c.Claim("execute_owned_saved_copy_close");c.Data.Execute(id,p,publish:false);await TransportCancellationProof.CancelClaimed(call,cancel,c.Data.Session,c.Data.Root,id,c.Client,c.Recorder,notify);Assert.True((await c.Client.CallToolAsync("execute_owned_saved_copy_close",Args(input))).IsError);Assert.True(File.Exists(FileArtifacts.FixedPath(c.Data.Root,"workingcopies",p["copyId"]!.GetValue<string>(),"mutation-fence.json")));
    }
    [Fact]public async Task LateReplyCannotRestoreConsumedAuthority()
    {
        await using var c=await Connection.Create();var input=c.Data.ExecuteInput();var call=c.Client.CallToolAsync("execute_owned_saved_copy_close",Args(input)).AsTask();var(id,p)=await c.Claim("execute_owned_saved_copy_close");c.Data.Execute(id,p,publish:false);var response=await call;Assert.True(response.IsError);Assert.Contains("timeout_outcome_unknown",response.StructuredContent!.Value.GetRawText());File.Copy(FileArtifacts.FixedPath(c.Data.Root,"responses",id+".unpublished"),FileArtifacts.FixedPath(c.Data.Root,"responses",id+".json"));Assert.True((await c.Client.CallToolAsync("execute_owned_saved_copy_close",Args(input))).IsError);Assert.Single(Directory.GetFiles(Path.Combine(c.Data.Root,"requests")));
    }
}
