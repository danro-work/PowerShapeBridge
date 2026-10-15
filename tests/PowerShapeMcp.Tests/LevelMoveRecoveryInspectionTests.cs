using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class LevelMoveRecoveryInspectionTests
{
    private sealed class Data
    {
        internal readonly OwnedLevelMoveStoreTests.Data Source=new();
        internal string Root=>Source.Root;
        internal readonly string Id=Guid.NewGuid().ToString("N");
        internal readonly string Copy,Packet,PlanSha,ClaimPath,FencePath;
        internal Data(int last=4,int code=1,string nativeType="System.Boolean",string fault="")
        {
            var prepared=Source.Native();Packet=prepared["ownedLevelMoveId"]!.GetValue<string>();
            Copy=prepared["ownedLevelMove"]!["copyId"]!.GetValue<string>();
            var plan=new JsonObject{["schemaVersion"]=1,["operationId"]=Id,["operation"]=fault=="wrong_operation"?"translate_owned_solid":"move_owned_solid_to_level",
                ["copyId"]=Copy,["ownershipSha256"]=prepared["ownedLevelMove"]!["ownershipSha256"]!.DeepClone(),
                ["ownedLevelMoveId"]=Packet,["ownedLevelMoveSha256"]=prepared["ownedLevelMoveSha256"]!.DeepClone(),["levelsBefore"]=Source.Levels.DeepClone()};
            PlanSha=ArtifactProof.Freeze(Root,"levelmoveexecutions/"+Id+".json",plan);
            ClaimPath=FileArtifacts.FixedPath(Root,"ownedlevelmoves",Packet+".execution-once.json");
            FencePath=FileArtifacts.FixedPath(Root,"workingcopies",Copy,"mutation-fence.json");
            if(last>0)
            {
                var claim=new JsonObject{["operationId"]=Id,["planSha256"]=PlanSha,["copyId"]=Copy,["ownedLevelMoveId"]=fault=="claim_other_packet"?Guid.NewGuid().ToString("N"):Packet,["automaticReplayForbidden"]=true};
                if(fault=="claim_extra")claim["command"]="never accepted";
                ArtifactProof.Freeze(Root,Path.GetRelativePath(Root,ClaimPath),claim);
                if(last<5||fault=="verified_retained_fence")ArtifactProof.Freeze(Root,Path.GetRelativePath(Root,FencePath),new(){["schemaVersion"]=1,["operationId"]=fault=="foreign_fence"?Guid.NewGuid().ToString("N"):Id,["planSha256"]=PlanSha,["copyId"]=Copy,["ownershipSha256"]=plan["ownershipSha256"]!.DeepClone(),["automaticReplayForbidden"]=true});
            }
            var evidence=Guid.NewGuid().ToString("N");var evidenceSha=ArtifactProof.Freeze(Root,"responses/"+evidence+".json",new(){["state"]="recorded_evidence_only"});
            string? prior=null;var phases=new[]{"prepared","execution_claimed","command_started","command_returned","executed_unverified","succeeded_verified"};
            for(int i=0;i<=last;i++)
            {
                var details=i==3?new JsonObject{["nativeRequestId"]=Id,["returnCode"]=code,["nativeReturnType"]=nativeType}:
                    i==5?new JsonObject{["evidence"]=new JsonArray(new JsonObject{["requestId"]=evidence,["sha256"]=evidenceSha})}:new JsonObject();
                if(i==3&&fault=="return_bool")details["returnCode"]=true;
                if(i==3&&fault=="return_extra")details["extra"]=true;
                prior=ArtifactProof.Freeze(Root,$"operations/{Id}/{i:D6}.json",new(){["schemaVersion"]=1,["operationId"]=Id,["planSha256"]=PlanSha,["sequence"]=i,["previousSha256"]=prior,["state"]=phases[i],["timestampUtc"]="2026-10-15T08:00:00Z",["details"]=details});
            }
            if(fault=="missing_claim")File.Delete(ClaimPath);
            if(fault=="missing_fence")File.Delete(FencePath);
            if(fault=="ambiguous_plan")Source.Base.Files.Write("saveexecutions/"+Id+".json","{}");
            if(fault=="packet_changed")File.AppendAllText(FileArtifacts.FixedPath(Root,"ownedlevelmoves",Packet+".json")," ");
            if(fault=="oversized_plan")File.AppendAllText(FileArtifacts.FixedPath(Root,"levelmoveexecutions",Id+".json"),new string(' ',16000001));
        }
        internal JsonObject Read()=>new RecoveryInspection(Root).Read(Id,TimeSpan.FromSeconds(20));
        internal Dictionary<string,string> Inventory()=>Directory.GetFiles(Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>FileArtifacts.Sha256(File.ReadAllBytes(p)));
        internal void AssertUnchanged(Dictionary<string,string> before)
        {
            var after=Inventory();Assert.Equal(before.Count,after.Count);foreach(var item in before)Assert.Equal(item.Value,after[item.Key]);
            Assert.False(Directory.Exists(Path.Combine(Root,"requests")));
        }
    }
    [Theory]
    [InlineData(0,"no_authoritative_command_start_recorded")]
    [InlineData(1,"no_authoritative_command_start_recorded")]
    [InlineData(2,"started_without_authoritative_return")]
    [InlineData(3,"returned_requires_independent_verification")]
    [InlineData(4,"returned_requires_independent_verification")]
    [InlineData(5,"historically_verified_requires_fresh_context")]
    public void LevelJournalPhasesNeverAuthorizeReplayOrResolveCurrentCad(int last,string assessment)
    {
        var data=new Data(last);var before=data.Inventory();var read=data.Read();
        Assert.True(new FileInfo(FileArtifacts.FixedPath(data.Root,"levelmoveexecutions",data.Id+".json")).Length>65536);
        Assert.Equal("recovery_inspected_recorded",read["state"]!.GetValue<string>());Assert.Equal(assessment,read["assessment"]!.GetValue<string>());
        Assert.Equal(last>=3,read["authoritativeSuccessReturnRecorded"]!.GetValue<bool>());
        foreach(var flag in new[]{"executionAuthorized","retryAllowed","cadConnectionAttempted","artifactWritesAttempted"})Assert.False(read[flag]!.GetValue<bool>());
        Assert.Equal("not_resolved_by_recorded_inspection",read["outcome"]!.GetValue<string>());data.AssertUnchanged(before);
    }
    [Theory][InlineData(0,"System.Boolean",false)][InlineData(1,"System.Boolean",true)][InlineData(1,"System.Int32",false)][InlineData(-1,"null",false)]
    public void OnlyNativeBooleanTrueIsAnAuthoritativeReturn(int code,string nativeType,bool authoritative)
    {
        var data=new Data(code:code,nativeType:nativeType);var before=data.Inventory();var result=data.Read();
        Assert.Equal("recovery_inspected_recorded",result["state"]!.GetValue<string>());Assert.Equal(authoritative,result["authoritativeSuccessReturnRecorded"]!.GetValue<bool>());
        Assert.Equal(authoritative?"returned_requires_independent_verification":"non_success_or_unproven_return_requires_recovery",result["assessment"]!.GetValue<string>());
        Assert.True(result["fencePresent"]!.GetValue<bool>());data.AssertUnchanged(before);
    }
    [Theory][InlineData("claim_other_packet")][InlineData("claim_extra")][InlineData("missing_claim")][InlineData("foreign_fence")]
    [InlineData("ambiguous_plan")][InlineData("packet_changed")][InlineData("wrong_operation")][InlineData("return_bool")][InlineData("return_extra")][InlineData("oversized_plan")]
    public void InvalidDependenciesRemainUnavailableWithoutRepair(string fault)
    {
        var data=new Data(fault:fault);var before=data.Inventory();var result=data.Read();
        Assert.Equal("recovery_inspection_unavailable",result["state"]!.GetValue<string>());Assert.False(result["executionAuthorized"]!.GetValue<bool>());data.AssertUnchanged(before);
    }
    [Theory][InlineData(4,"missing_fence","unresolved_fence_missing")][InlineData(5,"verified_retained_fence","verified_record_but_fence_retained")]
    public void FenceDiscrepanciesAreReportedAndNeverReleased(int last,string fault,string assessment)
    {
        var data=new Data(last,fault:fault);var before=data.Inventory();var result=data.Read();
        Assert.Equal(assessment,result["assessment"]!.GetValue<string>());Assert.False(result["executionAuthorized"]!.GetValue<bool>());data.AssertUnchanged(before);
    }
}
