using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class CheckpointRecoveryTests
{
    [Fact]
    public void IntentIsClientScopedOneUseAndExpiredCannotCreateCopy()
    {
        var data=new CheckpointRecoveryFixture();var handle=data.Issue();using var other=new BridgeSession(data.Files.Options());
        Assert.Throws<ReferenceRejected>(()=>other.CheckpointRecoveries.Consume(handle));
        var parameters=data.Session.CheckpointRecoveries.Consume(handle);Assert.Equal(data.Intent,parameters["recoveryId"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>data.Session.CheckpointRecoveries.Consume(handle));
        var next=data.Issue();data.Time+=301;Assert.Throws<ReferenceRejected>(()=>data.Session.CheckpointRecoveries.Consume(next));
        Assert.False(Directory.Exists(Path.Combine(data.Files.Root,"requests")));
    }
    [Theory]
    [InlineData("readonly_lost")][InlineData("checkpoint_changed")][InlineData("owner_changed")][InlineData("plan_changed")][InlineData("source_changed")]
    public void ChangedPinnedDependenciesCannotIssueRecoveryIntent(string fault)
    {
        var data=new CheckpointRecoveryFixture();var result=data.Prepared.DeepClone().AsObject();
        if(fault=="readonly_lost")File.SetAttributes(data.Plan["checkpoint"]!["path"]!.GetValue<string>(),FileAttributes.Normal);
        if(fault=="checkpoint_changed"){var path=data.Plan["checkpoint"]!["path"]!.GetValue<string>();File.SetAttributes(path,FileAttributes.Normal);File.AppendAllText(path,"changed");File.SetAttributes(path,FileAttributes.ReadOnly);}
        if(fault=="owner_changed")File.AppendAllText(FileArtifacts.FixedPath(data.Files.Root,"workingcopies",data.Copy,"ownership.json")," ");
        if(fault=="plan_changed")File.AppendAllText(FileArtifacts.FixedPath(data.Files.Root,"checkpointrecoveries",data.Intent+".json")," ");
        var source=data.Parameters.DeepClone().AsObject();if(fault=="source_changed")source["operationId"]=Guid.NewGuid().ToString("N");
        Assert.Throws<ReferenceRejected>(()=>data.Session.CheckpointRecoveries.Issue(result,source));
    }
    [Fact]
    public void CreatedProofIssuesOnlyNewSavedByteWorkingCopyLease()
    {
        var data=new CheckpointRecoveryFixture();var parameters=data.Session.CheckpointRecoveries.Consume(data.Issue());var id=Guid.NewGuid().ToString("N");var result=data.Materialize(id);
        var output=data.Session.Copies.Issue(data.Session.CheckpointRecoveries.CheckCreated(result,parameters));
        Assert.NotNull(data.Session.Copies.InspectParameters(output["workingCopyHandle"]!.GetValue<string>()));
        Assert.False(output["originalOutcomeResolved"]!.GetValue<bool>());Assert.Null(output["workingDocumentBindingHandle"]);
        Assert.Equal(data.Copy,data.Plan["origin"]!["copyId"]!.GetValue<string>());Assert.NotEqual(data.Copy,id);
    }
    [Theory]
    [InlineData("raw_proof_changed")][InlineData("working_changed")][InlineData("new_checkpoint_writable")][InlineData("claim_changed")]
    public void RejectedMaterializationNeverGrantsWorkingCopyAuthority(string fault)
    {
        var data=new CheckpointRecoveryFixture();var parameters=data.Session.CheckpointRecoveries.Consume(data.Issue());var id=Guid.NewGuid().ToString("N");var result=data.Materialize(id);
        if(fault=="raw_proof_changed")File.AppendAllText(FileArtifacts.FixedPath(data.Files.Root,"checkpointcopyplans",id+".result.json")," ");
        if(fault=="working_changed")File.AppendAllText(result["ownership"]!["working"]!["path"]!.GetValue<string>(),"changed");
        if(fault=="new_checkpoint_writable")File.SetAttributes(result["ownership"]!["checkpoint"]!["path"]!.GetValue<string>(),FileAttributes.Normal);
        if(fault=="claim_changed")data.Files.Write("checkpointrecoveries/"+data.Intent+".copy-once.json","{}");
        Assert.Throws<ReferenceRejected>(()=>data.Session.CheckpointRecoveries.CheckCreated(result,parameters));
    }
}
