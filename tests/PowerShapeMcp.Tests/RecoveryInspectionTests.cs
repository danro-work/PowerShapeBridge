using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class RecoveryInspectionTests
{
    private sealed class RecoveryFixture
    {
        internal readonly Fixture Files=new();
        internal readonly string Id=Guid.NewGuid().ToString("N"),Copy=Guid.NewGuid().ToString("N");
        internal string PlanSha { get; }
        private string? prior;
        internal RecoveryFixture(int last,bool writer=false,bool changedWork=false,bool changedCheckpoint=false)
        {
            var work=FileArtifacts.FixedPath(Files.Root,"workingcopies",Copy,"Working_"+Copy+".psmodel");var before=FileArtifacts.FixedPath(Files.Root,"workingcopies",Copy,"checkpoint","Before.psmodel");
            Files.Write(Path.GetRelativePath(Files.Root,work),"Opaque recovery source");Files.Write(Path.GetRelativePath(Files.Root,before),"Opaque recovery source");File.SetAttributes(before,FileAttributes.ReadOnly);
            var hash=FileArtifacts.Sha256(File.ReadAllBytes(work));
            var ownership=new JsonObject{["schemaVersion"]=2,["copyId"]=Copy,["bridgeVersion"]="0.33.0-dev",["createdUtc"]="2026-10-12T10:00:00Z",["scope"]="saved_disk_bytes_only",["executionEnabled"]=false,
                ["source"]=new JsonObject{["path"]="C:/NeverReadArbitrarySource.psmodel"},["working"]=new JsonObject{["path"]=work,["initialSha256"]=hash},["checkpoint"]=new JsonObject{["path"]=before,["sha256"]=hash}};
            var ownSha=ArtifactProof.Freeze(Files.Root,"workingcopies/"+Copy+"/ownership.json",ownership);
            var plan=new JsonObject{["schemaVersion"]=1,["operationId"]=Id,["operation"]="open_working_copy",["copyId"]=Copy,["ownershipSha256"]=ownSha};
            PlanSha=ArtifactProof.Freeze(Files.Root,"copyopenplans/"+Id+".json",plan);
            if(last>0)ArtifactProof.Freeze(Files.Root,"workingcopies/"+Copy+"/open-once.json",new(){["operationId"]=Id,["planSha256"]=PlanSha,["copyId"]=Copy,["ownershipSha256"]=ownSha,["automaticReplayForbidden"]=true});
            var evidenceId=Guid.NewGuid().ToString("N");var evidenceSha=ArtifactProof.Freeze(Files.Root,"responses/"+evidenceId+".json",new(){["state"]="opaque_independent_evidence"});
            var states=new[]{"prepared","execution_claimed","command_started","command_returned","executed_unverified","succeeded_verified"};
            for(int sequence=0;sequence<=last;sequence++)
            {
                var details=sequence==3?new JsonObject{["nativeRequestId"]=Id}:sequence==5?new JsonObject{["evidence"]=new JsonArray(new JsonObject{["requestId"]=evidenceId,["sha256"]=evidenceSha})}:new JsonObject();
                prior=ArtifactProof.Freeze(Files.Root,$"operations/{Id}/{sequence:D6}.json",new(){["schemaVersion"]=1,["operationId"]=Id,["planSha256"]=PlanSha,["sequence"]=sequence,["previousSha256"]=prior,["state"]=states[sequence],["timestampUtc"]="2026-10-12T10:00:00Z",["details"]=details});
            }
            if(writer)Files.Write("operations/"+Id+"/writer.lock","Unresolved writer marker");
            if(changedWork)File.AppendAllText(work,"Changed saved bytes");
            if(changedCheckpoint){File.SetAttributes(before,FileAttributes.Normal);File.AppendAllText(before,"Changed checkpoint");}
        }
        internal JsonObject Read()=>new RecoveryInspection(Files.Root).Read(Id,TimeSpan.FromSeconds(5));
    }
    [Theory]
    [InlineData(0,"no_authoritative_command_start_recorded")]
    [InlineData(1,"no_authoritative_command_start_recorded")]
    [InlineData(2,"started_without_authoritative_return")]
    [InlineData(3,"returned_requires_independent_verification")]
    [InlineData(4,"returned_requires_independent_verification")]
    [InlineData(5,"historically_verified_requires_fresh_context")]
    public void PhaseMatrixNeverGrantsReplayOrCurrentCadSuccess(int last,string assessment)
    {
        var fixture=new RecoveryFixture(last);var before=Directory.GetFiles(fixture.Files.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>FileArtifacts.Sha256(File.ReadAllBytes(p)));
        var result=fixture.Read();Assert.Equal("recovery_inspected_recorded",result["state"]!.GetValue<string>());Assert.Equal(assessment,result["assessment"]!.GetValue<string>());
        Assert.False(result["retryAllowed"]!.GetValue<bool>());Assert.False(result["executionAuthorized"]!.GetValue<bool>());Assert.False(result["cadConnectionAttempted"]!.GetValue<bool>());
        Assert.Equal("not_resolved_by_recorded_inspection",result["outcome"]!.GetValue<string>());
        Assert.Equal(before.Count,Directory.GetFiles(fixture.Files.Root,"*",SearchOption.AllDirectories).Length);foreach(var item in before)Assert.Equal(item.Value,FileArtifacts.Sha256(File.ReadAllBytes(item.Key)));
        Assert.False(Directory.Exists(Path.Combine(fixture.Files.Root,"requests")));
    }
    [Theory]
    [InlineData("writer","journal_writer_or_partial_publication")]
    [InlineData("work","working_saved_bytes_changed")]
    [InlineData("checkpoint","checkpoint_protection_changed")]
    public void ChangedArtifactsRemainUnresolvedAndPreserved(string fault,string assessment)
    {
        var fixture=new RecoveryFixture(2,writer:fault=="writer",changedWork:fault=="work",changedCheckpoint:fault=="checkpoint");var result=fixture.Read();
        Assert.Equal(assessment,result["assessment"]!.GetValue<string>());Assert.True(result["claimPresent"]!.GetValue<bool>());Assert.False(result["executionAuthorized"]!.GetValue<bool>());
    }
    [Theory]
    [InlineData("ambiguous_plan")][InlineData("bad_plan_sha")][InlineData("claim_other")][InlineData("missing_claim")][InlineData("partial_journal")][InlineData("foreign_fence")]
    public void InvalidDependenciesCannotProduceRecoveryAuthority(string fault)
    {
        var fixture=new RecoveryFixture(2);
        if(fault=="ambiguous_plan")fixture.Files.Write("saveexecutions/"+fixture.Id+".json","{}");
        if(fault=="bad_plan_sha")File.AppendAllText(FileArtifacts.FixedPath(fixture.Files.Root,"copyopenplans",fixture.Id+".json")," ");
        if(fault=="claim_other")fixture.Files.Write("workingcopies/"+fixture.Copy+"/open-once.json",new JsonObject{["operationId"]=Guid.NewGuid().ToString("N")}.ToJsonString());
        if(fault=="missing_claim")File.Delete(FileArtifacts.FixedPath(fixture.Files.Root,"workingcopies",fixture.Copy,"open-once.json"));
        if(fault=="partial_journal")File.AppendAllText(FileArtifacts.FixedPath(fixture.Files.Root,"operations",fixture.Id,"000002.json"),"{");
        if(fault=="foreign_fence")fixture.Files.Write("workingcopies/"+fixture.Copy+"/mutation-fence.json","{}");
        var result=fixture.Read();Assert.Equal("recovery_inspection_unavailable",result["state"]!.GetValue<string>());Assert.False(result["retryAllowed"]!.GetValue<bool>());
        Assert.False(Directory.Exists(Path.Combine(fixture.Files.Root,"requests")));
    }
    [Fact]
    public void CancelledDeadlineAndInvalidIdentityNeverContactCad()
    {
        var fixture=new RecoveryFixture(2);using var stop=new CancellationTokenSource();stop.Cancel();var reader=new RecoveryInspection(fixture.Files.Root);
        Assert.Equal("recovery_inspection_cancelled",reader.Read(fixture.Id,TimeSpan.FromSeconds(5),stop.Token)["state"]!.GetValue<string>());
        Assert.Equal("recovery_inspection_unavailable",reader.Read(fixture.Id,TimeSpan.Zero)["state"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>reader.Read("../arbitrary",TimeSpan.FromSeconds(5)));
    }
    [Theory]
    [InlineData(0,false)][InlineData(-1,false)][InlineData(1,true)]
    public void RecordedTranslationCodeNeverBecomesCurrentCadSuccess(int returnCode,bool successReturn)
    {
        var files=new Fixture();var id=Guid.NewGuid().ToString("N");var copy=Guid.NewGuid().ToString("N");var packetId=Guid.NewGuid().ToString("N");var localId=Guid.NewGuid().ToString("N");
        var work=FileArtifacts.FixedPath(files.Root,"workingcopies",copy,"Working_"+copy+".psmodel");var checkpoint=FileArtifacts.FixedPath(files.Root,"workingcopies",copy,"checkpoint","Before.psmodel");files.Write(Path.GetRelativePath(files.Root,work),"opaque");files.Write(Path.GetRelativePath(files.Root,checkpoint),"opaque");File.SetAttributes(checkpoint,FileAttributes.ReadOnly);var hash=FileArtifacts.Sha256(File.ReadAllBytes(work));
        var owner=new JsonObject{["schemaVersion"]=2,["copyId"]=copy,["bridgeVersion"]="synthetic",["createdUtc"]="2026-10-12T10:00:00Z",["scope"]="saved_disk_bytes_only",["executionEnabled"]=false,["source"]=new JsonObject(),["working"]=new JsonObject{["path"]=work,["initialSha256"]=hash},["checkpoint"]=new JsonObject{["path"]=checkpoint,["sha256"]=hash}};
        var ownSha=ArtifactProof.Freeze(files.Root,"workingcopies/"+copy+"/ownership.json",owner);
        var packet=new JsonObject{["copyId"]=copy,["ownershipSha256"]=ownSha,["operationPlanId"]=localId};var packetSha=ArtifactProof.Freeze(files.Root,"ownedoperations/"+packetId+".json",packet);
        var plan=new JsonObject{["schemaVersion"]=1,["operationId"]=id,["operation"]="translate_owned_solid",["copyId"]=copy,["ownershipSha256"]=ownSha,["ownedOperationId"]=packetId,["ownedOperationSha256"]=packetSha};var planSha=ArtifactProof.Freeze(files.Root,"ownedexecutions/"+id+".json",plan);
        ArtifactProof.Freeze(files.Root,"operationplans/"+localId+".execution-once.json",new(){["operationId"]=id,["planSha256"]=planSha,["ownedOperationId"]=packetId,["copyId"]=copy,["automaticReplayForbidden"]=true});
        ArtifactProof.Freeze(files.Root,"workingcopies/"+copy+"/mutation-fence.json",new(){["schemaVersion"]=1,["operationId"]=id,["planSha256"]=planSha,["copyId"]=copy,["ownershipSha256"]=ownSha,["automaticReplayForbidden"]=true});
        string? previous=null;var states=new[]{"prepared","execution_claimed","command_started","command_returned","outcome_unknown"};
        for(int i=0;i<states.Length;i++)previous=ArtifactProof.Freeze(files.Root,$"operations/{id}/{i:D6}.json",new(){["schemaVersion"]=1,["operationId"]=id,["planSha256"]=planSha,["sequence"]=i,["previousSha256"]=previous,["state"]=states[i],["timestampUtc"]="2026-10-12T10:00:00Z",["details"]=i==3?new JsonObject{["nativeRequestId"]=id,["returnCode"]=returnCode}:new JsonObject()});
        var inspected=new RecoveryInspection(files.Root).Read(id,TimeSpan.FromSeconds(5));Assert.Equal("recovery_inspected_recorded",inspected["state"]!.GetValue<string>());Assert.Equal(successReturn,inspected["authoritativeSuccessReturnRecorded"]!.GetValue<bool>());
        Assert.Equal(successReturn?"returned_requires_independent_verification":"non_success_or_unproven_return_requires_recovery",inspected["assessment"]!.GetValue<string>());Assert.True(inspected["fencePresent"]!.GetValue<bool>());Assert.False(inspected["executionAuthorized"]!.GetValue<bool>());
    }
}
