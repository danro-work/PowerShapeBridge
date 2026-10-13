using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class LegacyActivationRecoveryTests
{
    private sealed class Data
    {
        internal readonly Fixture Files=new();internal readonly string Id=Guid.NewGuid().ToString("N");
        internal readonly JsonObject Receipt;
        internal Data(string phase="before_activate",bool partial=false)
        {
            Receipt=new(){["operationId"]=Id,["state"]=phase=="completed"?"document_active_verified":phase=="before_activate"?"context_change_outcome_pending":phase=="rejected"?"rejected_before_context_change":"context_change_outcome_unknown",
                ["phase"]=phase=="rejected"?"stopped":phase,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=phase!="rejected",["updatedUtc"]="2026-10-13T11:00:00Z",
                ["savedFileHashesBefore"]=new JsonObject{["C:/NeverReadThis.psmodel"]=new string('f',64)}};
            Publish();if(phase!="rejected")Files.Write("operations/"+Id+".once","");if(partial)Files.Write("operations/"+Id+".json.tmp","Partial bytes; never parsed or repaired");
        }
        internal void Publish()=>Files.Write("operations/"+Id+".json",Receipt.ToJsonString());
        internal JsonObject Read()=>new RecoveryInspection(Files.Root).Read(Id,TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("before_activate",false,"legacy_activation_without_authoritative_return")]
    [InlineData("stopped",false,"legacy_activation_without_authoritative_return")]
    [InlineData("completed",false,"legacy_activation_historical_observation_requires_fresh_context")]
    [InlineData("rejected",false,"legacy_activation_recorded_rejection")]
    [InlineData("before_activate",true,"legacy_activation_partial_publication")]
    public void LegacyPhaseObservationPreservesArtifactsAndNeverGrantsAuthority(string phase,bool partial,string assessment)
    {
        var data=new Data(phase,partial);var before=TransportCancellationProof.Snapshot(data.Files.Root);
        var result=data.Read();Assert.Equal("recovery_inspected_recorded",result["state"]!.GetValue<string>());Assert.Equal(assessment,result["assessment"]!.GetValue<string>());
        Assert.Equal("activate_document",result["operation"]!.GetValue<string>());Assert.Equal("legacy_single_receipt",result["journalFormat"]!.GetValue<string>());
        Assert.Equal("not_resolved_by_recorded_inspection",result["outcome"]!.GetValue<string>());
        foreach(var flag in new[]{"executionAuthorized","checkpointRestoreAuthorized","retryAllowed","authoritativeSuccessReturnRecorded","commandReturnedRecorded","currentContextChecked","savedFilesChecked","cadConnectionAttempted","artifactWritesAttempted"})Assert.False(result[flag]!.GetValue<bool>());
        TransportCancellationProof.Unchanged(data.Files.Root,before);Assert.False(Directory.Exists(Path.Combine(data.Files.Root,"requests")));
    }

    [Theory]
    [InlineData("wrong_phase")][InlineData("wrong_state")][InlineData("geometry")][InlineData("save")][InlineData("invalid_time")]
    [InlineData("missing_claim")][InlineData("nonempty_claim")][InlineData("marker_directory")]
    public void MalformedLegacyActivationNeverProducesRecoveryAuthority(string fault)
    {
        var data=new Data();
        if(fault=="wrong_phase")data.Receipt["phase"]="executed_unverified";
        if(fault=="wrong_state")data.Receipt["state"]="succeeded_verified";
        if(fault=="geometry")data.Receipt["geometryMutationAttempted"]=true;
        if(fault=="save")data.Receipt["cadSaveAttempted"]=true;
        if(fault=="invalid_time")data.Receipt["updatedUtc"]="not-a-time";
        if(fault=="missing_claim")File.Delete(FileArtifacts.FixedPath(data.Files.Root,"operations",data.Id+".once"));
        if(fault=="nonempty_claim")data.Files.Write("operations/"+data.Id+".once","different content");
        if(fault=="marker_directory")Directory.CreateDirectory(FileArtifacts.FixedPath(data.Files.Root,"operations",data.Id+".json.tmp"));
        data.Publish();var before=TransportCancellationProof.Snapshot(data.Files.Root);var result=data.Read();
        Assert.Equal("recovery_inspection_unavailable",result["state"]!.GetValue<string>());Assert.False(result["executionAuthorized"]!.GetValue<bool>());Assert.False(result["retryAllowed"]!.GetValue<bool>());
        TransportCancellationProof.Unchanged(data.Files.Root,before);Assert.False(Directory.Exists(Path.Combine(data.Files.Root,"requests")));
    }
}
