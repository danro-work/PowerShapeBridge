using System.Text.Json.Nodes;
using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OutcomeResolutionJournalTests
{
    internal sealed class Data
    {
        internal readonly Fixture Files=new();
        internal readonly string OperationId=Guid.NewGuid().ToString("N"),VerifyId=Guid.NewGuid().ToString("N"),CopyId=Guid.NewGuid().ToString("N");
        internal readonly DateTimeOffset Now=LeaseTime.PythonPrecision(DateTimeOffset.UtcNow);
        internal readonly JsonObject Parameters=new(),Receipt=new();
        internal readonly string Verifier,Working,Checkpoint;
        internal Data(string verifier="verify_working_copy_open",bool returned=true,int code=1)
        {
            Verifier=verifier;
            var kind=verifier switch
            {
                "verify_owned_translation"=>("translate_owned_solid","executionRequestId","executionReceiptSha256","executionOperationId","owned_translation_verified_observed","ownedexecutions"),
                "verify_owned_save_as"=>("save_owned_document_as","saveRequestId","saveReceiptSha256","saveOperationId","owned_save_as_verified_observed","saveexecutions"),
                "verify_owned_saved_reopen"=>("open_owned_saved_result","reopenRequestId","reopenReceiptSha256","reopenOperationId","owned_saved_reopen_verified_observed","copyopenplans"),
                _=>("open_working_copy","openRequestId","openReceiptSha256","openOperationId","owned_open_verified_observed","copyopenplans")
            };
            Working=FileArtifacts.FixedPath(Files.Root,"workingcopies",CopyId,"Working_"+CopyId+".psmodel");
            Checkpoint=FileArtifacts.FixedPath(Files.Root,"workingcopies",CopyId,"checkpoint","Before.psmodel");
            Files.Write(Path.GetRelativePath(Files.Root,Working),"opaque saved bytes");Files.Write(Path.GetRelativePath(Files.Root,Checkpoint),"opaque saved bytes");File.SetAttributes(Checkpoint,FileAttributes.ReadOnly);
            var hash=FileArtifacts.Sha256(File.ReadAllBytes(Working));
            var ownership=ArtifactProof.Freeze(Files.Root,"workingcopies/"+CopyId+"/ownership.json",new()
            {
                ["schemaVersion"]=2,["copyId"]=CopyId,["bridgeVersion"]="0.33.0-dev",["createdUtc"]=LeaseTime.Format(Now.AddSeconds(-30)),
                ["scope"]="saved_disk_bytes_only",["executionEnabled"]=false,["source"]=new JsonObject{["path"]="C:/NeverReadThis.psmodel"},
                ["working"]=new JsonObject{["path"]=Working,["initialSha256"]=hash},["checkpoint"]=new JsonObject{["path"]=Checkpoint,["sha256"]=hash}
            });
            var plan=new JsonObject{["schemaVersion"]=1,["operationId"]=OperationId,["operation"]=kind.Item1,["copyId"]=CopyId,["ownershipSha256"]=ownership};
            var target=Guid.NewGuid().ToString("N");var owned=Guid.NewGuid().ToString("N");var local=Guid.NewGuid().ToString("N");
            if(kind.Item1=="save_owned_document_as")plan["targetId"]=target;
            if(kind.Item1=="translate_owned_solid")
            {
                plan["ownedOperationId"]=owned;plan["ownedOperationSha256"]=ArtifactProof.Freeze(Files.Root,"ownedoperations/"+owned+".json",
                    new(){["copyId"]=CopyId,["ownershipSha256"]=ownership,["operationPlanId"]=local});
            }
            var planSha=ArtifactProof.Freeze(Files.Root,kind.Item6+"/"+OperationId+".json",plan);
            var claim=new JsonObject{["operationId"]=OperationId,["planSha256"]=planSha,["automaticReplayForbidden"]=true};string claimPath;
            if(kind.Item1=="translate_owned_solid") {claim["ownedOperationId"]=owned;claim["copyId"]=CopyId;claimPath="operationplans/"+local+".execution-once.json";}
            else if(kind.Item1=="save_owned_document_as"){claim["targetId"]=target;claimPath="savedresults/"+target+"/save-once.json";}
            else {claim["copyId"]=CopyId;claim["ownershipSha256"]=ownership;claimPath="workingcopies/"+CopyId+"/open-once.json";}
            ArtifactProof.Freeze(Files.Root,claimPath,claim);
            var sourceSha=ArtifactProof.Freeze(Files.Root,"responses/"+OperationId+".json",new(){["operationId"]=OperationId,["timestampUtc"]=LeaseTime.Format(Now.AddSeconds(-8))});
            Parameters[kind.Item2]=OperationId;Parameters[kind.Item3]=sourceSha;
            Parameters["modelId"]=9;Parameters["windowNumber"]=4;Parameters["expiresUtc"]=LeaseTime.Format(Now.AddSeconds(200));
            var inspectionId=Guid.NewGuid().ToString("N");var listingId=Guid.NewGuid().ToString("N");
            var identity=new JsonObject{["processId"]=123,["processStartedUtc"]="2026-10-12T10:00:00Z",["windowsSessionId"]=1};
            var inspection=identity.DeepClone().AsObject();inspection["state"]="succeeded_verified";inspection["timestampUtc"]=LeaseTime.Format(Now.AddSeconds(-6));
            inspection["model"]=new JsonObject{["id"]=9,["name"]="Working_"+CopyId,["path"]=Working};inspection["entityCount"]=0;inspection["entities"]=new JsonArray();
            var listing=identity.DeepClone().AsObject();listing["state"]="documents_listed_verified";listing["timestampUtc"]=LeaseTime.Format(Now.AddSeconds(-5));listing["activeWindow"]=new JsonObject{["modelId"]=9,["windowNumber"]=4};
            listing["documents"]=new JsonArray(new JsonObject{["modelId"]=9,["windowNumber"]=4,["activationSupported"]=true});
            Parameters["inspectionRequestId"]=inspectionId;Parameters["inspectionSha256"]=ArtifactProof.Freeze(Files.Root,"responses/"+inspectionId+".json",inspection);
            Parameters["listingRequestId"]=listingId;Parameters["listingSha256"]=ArtifactProof.Freeze(Files.Root,"responses/"+listingId+".json",listing);
            var evidence=new JsonArray();foreach(var pair in new[]{(kind.Item2,kind.Item3),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})
                evidence.Add(new JsonObject{["requestId"]=Parameters[pair.Item1]!.DeepClone(),["sha256"]=Parameters[pair.Item2]!.DeepClone()});
            var phases=returned?new[]{"prepared","execution_claimed","command_started","command_returned","executed_unverified","succeeded_verified"}:
                new[]{"prepared","execution_claimed","command_started","outcome_unknown","succeeded_verified"};
            string? previous=null;
            for(int i=0;i<phases.Length;i++)
            {
                var details=new JsonObject();if(phases[i]=="command_returned")
                {details["nativeRequestId"]=OperationId;if(kind.Item1=="translate_owned_solid")details["returnCode"]=code;if(kind.Item1=="save_owned_document_as")details["saveCommandReturned"]=true;}
                if(phases[i]=="succeeded_verified"){details["evidence"]=evidence.DeepClone();details["noCommandReplay"]=true;}
                previous=ArtifactProof.Freeze(Files.Root,$"operations/{OperationId}/{i:D6}.json",new(){["schemaVersion"]=1,["operationId"]=OperationId,["planSha256"]=planSha,
                    ["sequence"]=i,["previousSha256"]=previous,["state"]=phases[i],["timestampUtc"]=LeaseTime.Format(Now.AddSeconds(phases[i]=="command_returned"?-8:-12+i)),["details"]=details});
            }
            Receipt["state"]=kind.Item5;Receipt[kind.Item4]=OperationId;Receipt[kind.Item3]=sourceSha;Receipt["copyId"]=CopyId;Receipt["planSha256"]=planSha;Receipt["ownershipSha256"]=ownership;
            Receipt["recordedJournalState"]="succeeded_verified";Receipt["timestampUtc"]=LeaseTime.Format(Now.AddSeconds(-2));Receipt["expiresUtc"]=Parameters["expiresUtc"]!.DeepClone();Receipt["evidence"]=evidence.DeepClone();
            foreach(var flag in new[]{"cadSaveAttempted","geometryMutationAttempted","contextChangeAttempted"})Receipt[flag]=false;
            PublishReceipt();
        }
        internal void PublishReceipt(){var raw=Receipt.DeepClone().AsObject();raw.Remove("bridgeRequestId");Files.Write("responses/"+VerifyId+".json",raw.ToJsonString());Receipt["bridgeRequestId"]=VerifyId;}
        internal JsonObject Record(CancellationToken cancellation=default)=>new OutcomeResolutionJournal(Files.Root,()=>Now).Record(Verifier,Receipt,Parameters,cancellation);
        internal string RecordPath=>FileArtifacts.FixedPath(Files.Root,"recoveryresolutions",OperationId,VerifyId+".json");
    }
    [Theory]
    [InlineData("verify_working_copy_open")][InlineData("verify_owned_translation")][InlineData("verify_owned_save_as")][InlineData("verify_owned_saved_reopen")]
    public void FreshVerifiedOutcomeIsRecordedOnceWithoutCommandAuthority(string verifier)
    {
        var data=new Data(verifier);var before=Directory.GetFiles(data.Files.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>FileArtifacts.Sha256(File.ReadAllBytes(p)));
        var result=data.Record();var record=JsonNode.Parse(File.ReadAllText(data.RecordPath))!;Assert.Equal("succeeded_verified_at_observation",record["outcome"]!.GetValue<string>());
        Assert.False(record["executionAuthorized"]!.GetValue<bool>());Assert.False(record["retryAllowed"]!.GetValue<bool>());Assert.False(record["checkpointRestoreAuthorized"]!.GetValue<bool>());
        Assert.Equal(123,record["observedDocumentIdentity"]!["processId"]!.GetValue<int>());Assert.Equal(result.ToJsonString(),data.Record().ToJsonString());
        Assert.Equal(before.Count+1,Directory.GetFiles(data.Files.Root,"*",SearchOption.AllDirectories).Length);
        foreach(var item in before)Assert.Equal(item.Value,FileArtifacts.Sha256(File.ReadAllBytes(item.Key)));
        var history=new OutcomeResolutionJournal(data.Files.Root).Read(data.OperationId);Assert.Equal("resolution_history_read",history["state"]!.GetValue<string>());
        Assert.Single(history["records"]!.AsArray());Assert.False(history["records"]![0]!["currentContextChecked"]!.GetValue<bool>());
        var inspected=new RecoveryInspection(data.Files.Root).Read(data.OperationId,TimeSpan.FromSeconds(5));Assert.Single(inspected["resolutionHistory"]!["records"]!.AsArray());
        Assert.Equal("not_resolved_by_recorded_inspection",inspected["outcome"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(data.Files.Root,"requests")));
    }
    [Theory]
    [InlineData("verify_working_copy_open")][InlineData("verify_owned_translation")][InlineData("verify_owned_save_as")][InlineData("verify_owned_saved_reopen")]
    public void TerminalSuccessWithoutAuthoritativeReturnCannotBeRecorded(string verifier)
    {var data=new Data(verifier,returned:false);Assert.Throws<ReferenceRejected>(()=>data.Record());Assert.False(File.Exists(data.RecordPath));}
    [Theory]
    [InlineData(0)][InlineData(-1)]
    public void NonSuccessReturnNeverBecomesResolutionSuccess(int code)
    {var data=new Data("verify_owned_translation",code:code);Assert.Throws<ReferenceRejected>(()=>data.Record());Assert.False(File.Exists(data.RecordPath));}
    [Theory]
    [InlineData("expired")][InlineData("raw_differs")][InlineData("wrong_document")][InlineData("wrong_process")][InlineData("old_read")][InlineData("working_changed")][InlineData("checkpoint_writable")][InlineData("writer_marker")]
    public void InvalidOrChangedEvidenceCreatesNoResolution(string fault)
    {
        var data=new Data();
        if(fault=="expired")data.Parameters["expiresUtc"]=LeaseTime.Format(data.Now);
        if(fault=="raw_differs")data.Receipt["state"]="fake_success";
        if(fault=="working_changed")File.AppendAllText(data.Working,"changed");
        if(fault=="checkpoint_writable")File.SetAttributes(data.Checkpoint,FileAttributes.Normal);
        if(fault=="writer_marker")data.Files.Write("operations/"+data.OperationId+"/writer.lock","unresolved");
        if(fault is "wrong_document" or "wrong_process" or "old_read")
        {
            var oldId=data.Parameters["listingRequestId"]!.GetValue<string>();
            var listing=JsonNode.Parse(File.ReadAllText(FileArtifacts.FixedPath(data.Files.Root,"responses/"+oldId+".json")))!.AsObject();
            var id=Guid.NewGuid().ToString("N");var relative="responses/"+id+".json";
            if(fault=="wrong_document")listing["activeWindow"]!["modelId"]=10;
            if(fault=="wrong_process")listing["processId"]=456;
            if(fault=="old_read")listing["timestampUtc"]=LeaseTime.Format(data.Now.AddSeconds(-20));
            data.Files.Write(relative,listing.ToJsonString());var sha=FileArtifacts.Sha256(File.ReadAllBytes(FileArtifacts.FixedPath(data.Files.Root,relative)));
            data.Parameters["listingRequestId"]=id;data.Parameters["listingSha256"]=sha;
            data.Receipt["evidence"]![2]!["requestId"]=id;data.Receipt["evidence"]![2]!["sha256"]=sha;data.PublishReceipt();
        }
        var rejected=Assert.Throws<ReferenceRejected>(()=>data.Record());
        if(fault=="wrong_document")Assert.Equal("resolution_current_document_differs",rejected.Code);
        if(fault=="wrong_process")Assert.Equal("resolution_current_process_differs",rejected.Code);
        if(fault=="old_read")Assert.Equal("resolution_reads_not_fresh_after_return",rejected.Code);
        Assert.False(File.Exists(data.RecordPath));Assert.False(Directory.Exists(Path.Combine(data.Files.Root,"requests")));
    }
    [Fact]
    public void CancellationAndExistingDifferentRecordCannotOverwriteEvidence()
    {
        var data=new Data();using var stop=new CancellationTokenSource();stop.Cancel();Assert.Throws<OperationCanceledException>(()=>data.Record(stop.Token));Assert.False(File.Exists(data.RecordPath));
        data.Record();File.AppendAllText(data.RecordPath," "); // Whitespace keeps JSON equal; same bytes remain authoritative.
        var sha=FileArtifacts.Sha256(File.ReadAllBytes(data.RecordPath));Assert.Equal(sha,data.Record()["resolutionSha256"]!.GetValue<string>());
        data.Files.Write(Path.GetRelativePath(data.Files.Root,data.RecordPath),"{}");Assert.Throws<ReferenceRejected>(()=>data.Record());Assert.Equal("{}",File.ReadAllText(data.RecordPath));
        Assert.Equal("resolution_history_unavailable",new OutcomeResolutionJournal(data.Files.Root).Read(data.OperationId)["state"]!.GetValue<string>());
    }
    [Theory]
    [InlineData(false,false)][InlineData(false,true)][InlineData(true,true)]
    public void WriterFlagIsExplicitAndAlwaysDisabledInReadonly(bool readOnly,bool enabled)
    {
        var files=new Fixture();files.Config["recoveryInspectionExperimental"]=true;files.Config["recoveryResolutionExperimental"]=enabled;files.SaveConfig();
        using var session=new BridgeSession(files.Options(readOnly));Assert.Equal(enabled&&!readOnly,session.RecoveryResolutionEnabled);
        if(!session.RecoveryResolutionEnabled){session.RecordResolution("not_a_verifier",new(),new(),default);Assert.False(Directory.Exists(Path.Combine(files.Root,"recoveryresolutions")));}
    }
    [Fact]
    public void InvalidWriterFlagAndMissingReaderDependencyRejectConfiguration()
    {
        var files=new Fixture();files.Config["recoveryResolutionExperimental"]=true;files.SaveConfig();Assert.Throws<InvalidDataException>(()=>files.Options().ReadConfiguration());
        files.Config["recoveryInspectionExperimental"]=true;files.Config["recoveryResolutionExperimental"]="true";files.SaveConfig();Assert.Throws<InvalidDataException>(()=>files.Options().ReadConfiguration());
    }
    [Fact]
    public void ExpiryAtPublicationDoesNotPublishAResolution()
    {
        var data=new Data();int calls=0;var journal=new OutcomeResolutionJournal(data.Files.Root,()=>++calls==1?data.Now:data.Now.AddSeconds(201));
        var error=Assert.Throws<ReferenceRejected>(()=>journal.Record(data.Verifier,data.Receipt,data.Parameters));
        Assert.Equal("resolution_expired_before_publication",error.Code);Assert.False(File.Exists(data.RecordPath));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task McpVerificationPublishesResolutionBeforeIssuingCopyLease(bool conflictingRecord)
    {
        var data=new Data();foreach(var flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental","workingCopyOpenExperimental","recoveryInspectionExperimental","recoveryResolutionExperimental"})data.Files.Config[flag]=true;
        data.Files.SaveConfig();data.Files.Write("PSBridge.Worker.exe","Opaque fake marker; never launched.");
        using var session=new BridgeSession(data.Files.Options(false,5),utcClock:()=>data.Now);
        JsonObject Load(string key)
        {
            var id=data.Parameters[key]!.GetValue<string>();var snapshot=JsonNode.Parse(File.ReadAllText(FileArtifacts.FixedPath(data.Files.Root,"responses",id+".json")))!.AsObject();snapshot["bridgeRequestId"]=id;return snapshot;
        }
        var candidate=session.Candidates.Issue(Load("inspectionRequestId"),data.Parameters["inspectionSha256"]!.GetValue<string>())["candidateDocumentHandle"]!.GetValue<string>();
        var choice=session.Documents.Issue(Load("listingRequestId"),data.Parameters["listingSha256"]!.GetValue<string>())["documents"]![0]!["documentHandle"]!.GetValue<string>();
        var input=new Pipe();var output=new Pipe();using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var server=McpServer.Create(new StreamServerTransport(input.Reader.AsStream(),output.Writer.AsStream()),new McpServerOptions{ToolCollection=[..ToolCatalog.Create(session)]});var run=server.RunAsync(stop.Token);
        await using var client=await McpClient.CreateAsync(new StreamClientTransport(input.Writer.AsStream(),output.Reader.AsStream()),cancellationToken:stop.Token);
        Assert.False((await client.ListToolsAsync()).Single(t=>t.Name=="verify_working_copy_open").ProtocolTool.Annotations!.ReadOnlyHint);
        var call=client.CallToolAsync("verify_working_copy_open",new Dictionary<string,object?>{["openRequestId"]=data.OperationId,["openReceiptSha256"]=data.Parameters["openReceiptSha256"]!.GetValue<string>(),["candidateDocumentHandle"]=candidate,["activeDocumentHandle"]=choice});
        var claimed=await data.Files.ClaimRequest();var envelope=JsonNode.Parse(await data.Files.ReadClaimedRequest(claimed))!;var id=Path.GetFileNameWithoutExtension(claimed);
        Assert.Equal("verify_working_copy_open",envelope["arguments"]!["recipeId"]!.GetValue<string>());
        var parameters=envelope["arguments"]!["parameters"]!.AsObject();var reply=data.Receipt.DeepClone().AsObject();reply.Remove("bridgeRequestId");reply["expiresUtc"]=parameters["expiresUtc"]!.DeepClone();
        var recordPath=FileArtifacts.FixedPath(data.Files.Root,"recoveryresolutions",data.OperationId,id+".json");
        if(conflictingRecord)data.Files.Write(Path.GetRelativePath(data.Files.Root,recordPath),"{}");
        data.Files.Write("responses/"+id+".json",reply.ToJsonString());var result=await call;var body=JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!;
        if(conflictingRecord){Assert.True(result.IsError);Assert.Null(body["workingCopyHandle"]);Assert.Equal("{}",File.ReadAllText(recordPath));}
        else
        {
            Assert.False(result.IsError,body.ToJsonString());Assert.NotNull(body["workingCopyHandle"]);Assert.Equal(id,body["outcomeResolution"]!["verificationRequestId"]!.GetValue<string>());
            Assert.Equal(FileArtifacts.Sha256(File.ReadAllBytes(recordPath)),body["outcomeResolution"]!["resolutionSha256"]!.GetValue<string>());
            Assert.False(body["outcomeResolution"]!["executionAuthorized"]!.GetValue<bool>());
        }
        Assert.Single(Directory.GetFiles(Path.Combine(data.Files.Root,"requests"),"*.processing"));
        stop.Cancel();try{await run;}catch(OperationCanceledException){}
    }
}
