using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedSavedResultTests
{
    private static (SavedResultFixture Fixture,JsonObject Verified) Qualified()
    {
        var target=new OwnedSaveTargetTests.TargetFixture(saves:true);var fixture=new SavedResultFixture(target);fixture.Execute(Guid.NewGuid().ToString("N"));
        var input=fixture.FreshReads();var parameters=OwnedExecutionProof.VerificationParameters(input,target.Session.Candidates,target.Session.Documents,false,true);
        return(fixture,fixture.Verify(Guid.NewGuid().ToString("N"),parameters));
    }
    [Fact]
    public void ActualHashChainAndSavedBytesIssueOneScopedLeaseWithMinimumExpiry()
    {
        var (fixture,verified)=Qualified();fixture.Target.Data.Time+=20;fixture.Target.Data.Utc=fixture.Target.Data.Utc.AddSeconds(20);
        var issued=fixture.Session.SavedResults.Issue(verified,fixture.Parameters,fixture.Candidate);Assert.Equal(280,issued["savedResultLifetimeSeconds"]!.GetValue<int>());
        var input=fixture.Session.SavedResults.Parameters(issued["savedResultHandle"]!.GetValue<string>());
        Assert.Equal(verified["bridgeRequestId"]!.GetValue<string>(),input["verificationRequestId"]!.GetValue<string>());
        Assert.Equal(2,input.Count);Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
        Assert.True(File.Exists(FileArtifacts.FixedPath(fixture.Root,"savedresults",fixture.Target.Id,"save-once.json")));
    }
    [Theory]
    [InlineData("changed_file")]
    [InlineData("extra_file_identity")]
    [InlineData("bool_length")]
    [InlineData("candidate_lost")]
    [InlineData("wrong_model")]
    [InlineData("wrong_dependency")]
    [InlineData("wrong_sha")]
    [InlineData("retained_fence")]
    [InlineData("partial_journal")]
    [InlineData("wrong_terminal_evidence")]
    [InlineData("expired")]
    public void ForgedChangedOrStaleSuccessCannotIssueSavedResult(string fault)
    {
        var (fixture,verified)=Qualified();var parameters=fixture.Parameters.DeepClone().AsObject();
        switch(fault)
        {
            case "changed_file":File.AppendAllText(fixture.Target.Plan["resultPath"]!.GetValue<string>()," changed");break;
            case "extra_file_identity":verified["savedResult"]!["fileIdentity"]!["extra"]=true;break;
            case "bool_length":verified["savedResult"]!["length"]=true;break;
            case "candidate_lost":fixture.Session.Candidates.Clear();break;
            case "wrong_model":verified["model"]!["id"]=99;break;
            case "wrong_dependency":verified["targetId"]=Guid.NewGuid().ToString("N");break;
            case "wrong_sha":parameters["saveReceiptSha256"]=new string('f',64);break;
            case "retained_fence":fixture.Target.Data.Files.Write("workingcopies/"+fixture.Target.Plan["copyId"]!.GetValue<string>()+"/mutation-fence.json","synthetic unresolved operation");break;
            case "partial_journal":fixture.Target.Data.Files.Write("operations/"+fixture.SaveId+"/writer.lock","synthetic partial journal");break;
            case "wrong_terminal_evidence":verified["evidence"]![0]!["sha256"]=new string('f',64);break;
            case "expired":fixture.Target.Data.Time+=301;fixture.Target.Data.Utc=fixture.Target.Data.Utc.AddSeconds(301);break;
        }
        // Updating a fake native response does not repair its dependent journal,
        // original receipt, model choice or immutable execution target.
        fixture.Target.Data.Files.Write("responses/"+verified["bridgeRequestId"]!.GetValue<string>()+".json",verified.ToJsonString());
        Assert.Throws<ReferenceRejected>(()=>fixture.Session.SavedResults.Issue(verified,parameters,fixture.Candidate));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root,"requests")));
    }
    [Fact]
    public void NewFreshVerificationUsesOriginalTerminalEvidenceWithoutAppendingAgain()
    {
        var (fixture,first)=Qualified();var terminal=FileArtifacts.FixedPath(fixture.Root,"operations",fixture.SaveId,"000005.json");var hash=FileArtifacts.Sha256(File.ReadAllBytes(terminal));
        var input=fixture.FreshReads();var parameters=OwnedExecutionProof.VerificationParameters(input,fixture.Session.Candidates,fixture.Session.Documents,false,true);
        var second=fixture.Verify(Guid.NewGuid().ToString("N"),parameters,false);
        Assert.NotNull(fixture.Session.SavedResults.Issue(second,parameters,fixture.Candidate)["savedResultHandle"]);
        Assert.Equal(hash,FileArtifacts.Sha256(File.ReadAllBytes(terminal)));Assert.Equal(6,Directory.GetFiles(Path.GetDirectoryName(terminal)!,"*.json").Length);
    }
    [Fact]
    public void CrossConnectionExpiryRevocationAndRawReceiptTamperRejectBeforeQueue()
    {
        var (fixture,verified)=Qualified();var issued=fixture.Session.SavedResults.Issue(verified,fixture.Parameters,fixture.Candidate);var handle=issued["savedResultHandle"]!.GetValue<string>();
        var foreign=new OwnedSavedResultStore(fixture.Root,fixture.Session.SaveTargets,fixture.Session.Candidates,()=>fixture.Target.Data.Time,()=>fixture.Target.Data.Utc);
        Assert.Throws<ReferenceRejected>(()=>foreign.Parameters(handle));
        File.AppendAllText(FileArtifacts.FixedPath(fixture.Root,"responses",verified["bridgeRequestId"]!.GetValue<string>()+".json")," ");
        Assert.Throws<ReferenceRejected>(()=>fixture.Session.SavedResults.Parameters(handle));
        var (expired,response)=Qualified();var old=expired.Session.SavedResults.Issue(response,expired.Parameters,expired.Candidate)["savedResultHandle"]!.GetValue<string>();
        expired.Target.Data.Time+=301;expired.Target.Data.Utc=expired.Target.Data.Utc.AddSeconds(301);Assert.Throws<ReferenceRejected>(()=>expired.Session.SavedResults.Parameters(old));
        var (revoked,valid)=Qualified();var active=revoked.Session.SavedResults.Issue(valid,revoked.Parameters,revoked.Candidate)["savedResultHandle"]!.GetValue<string>();
        revoked.Session.SavedResults.Clear();Assert.Throws<ReferenceRejected>(()=>revoked.Session.SavedResults.Parameters(active));
    }
}
