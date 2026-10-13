using System.Text.Json.Nodes;
using ModelContextProtocol;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedSavedReopenExecutionTests
{
    private static JsonObject OpenArgs(SavedReopenFixture fixture)=>new(){["ownedReopenHandle"]=fixture.Handle,["candidateDocumentHandle"]=fixture.Data.Saved.Candidate,["activeDocumentHandle"]=fixture.Data.Saved.FreshReads()["activeDocumentHandle"]!.DeepClone()};
    [Fact]
    public void OneUseParametersConsumeIntentAndUseMinimumPreparedExpiry()
    {
        var data=new SavedReopenFixture();var args=OpenArgs(data);var session=data.Session;
        var parameters=OwnedExecutionProof.SavedReopenParameters(args,session.Candidates,session.Reopens,session.Documents);
        Assert.Equal(9,parameters.Count);Assert.Equal(data.Prepared["reopenId"]!.GetValue<string>(),parameters["reopenId"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>session.Reopens.Parameters(data.Handle));
        var receipt=data.Execution(Guid.NewGuid().ToString("N"),parameters);OwnedReopenProof.Execution(session,receipt,parameters);
        Assert.Throws<ReferenceRejected>(()=>OwnedExecutionProof.OpenReceipt(data.Root,receipt,parameters));
    }
    [Fact]
    public void IndependentFreshProofIssuesNewBindingWithNewEntityIds()
    {
        var data=new SavedReopenFixture();var session=data.Session;var parameters=OwnedExecutionProof.SavedReopenParameters(OpenArgs(data),session.Candidates,session.Reopens,session.Documents);
        data.Execution(Guid.NewGuid().ToString("N"),parameters);session.RevokeCadLeases(true);
        var fresh=data.FreshReads();var proof=OwnedExecutionProof.VerificationParameters(fresh,session.Candidates,session.Documents,false,false,true);
        // Independently consumed choice can expire earlier than the candidate.
        proof["expiresUtc"]=LeaseTime.Format(data.Data.Target.Data.Utc.AddSeconds(100));
        var verified=data.Verification(Guid.NewGuid().ToString("N"),proof);var issued=OwnedReopenProof.IssueBinding(session,verified,proof,fresh["candidateDocumentHandle"]!.GetValue<string>());
        Assert.Equal(500,issued["model"]!["id"]!.GetValue<int>());Assert.NotNull(issued["workingDocumentBindingHandle"]);
        var binding=session.Bindings.ValidationParameters(issued["workingDocumentBindingHandle"]!.GetValue<string>());Assert.Equal(verified["bindingId"]!.GetValue<string>(),binding["bindingId"]!.GetValue<string>());
        Assert.Equal(data.Prepared["reopenId"]!.GetValue<string>(),session.Copies.InspectParameters(issued["workingCopyHandle"]!.GetValue<string>())["copyId"]!.GetValue<string>());
        Assert.Equal(100,issued["bindingLifetimeSeconds"]!.GetValue<int>());
    }
    [Theory]
    [InlineData("flags")][InlineData("binding")][InlineData("source")][InlineData("fresh_choice")][InlineData("retained_fence")][InlineData("missing_return")][InlineData("working_path")]
    public void ForgedOrChangedProofCannotIssueBinding(string fault)
    {
        var data=new SavedReopenFixture();var session=data.Session;var parameters=OwnedExecutionProof.SavedReopenParameters(OpenArgs(data),session.Candidates,session.Reopens,session.Documents);data.Execution(Guid.NewGuid().ToString("N"),parameters);
        var fresh=data.FreshReads();var proof=OwnedExecutionProof.VerificationParameters(fresh,session.Candidates,session.Documents,false,false,true);var verified=data.Verification(Guid.NewGuid().ToString("N"),proof);
        if(fault=="flags")verified["noCommandReplay"]=false;
        if(fault=="binding")verified["bindingSha256"]=new string('f',64);
        if(fault=="working_path")verified["model"]!["path"]=Path.Combine(data.Root,"Different.psmodel");
        if(fault=="source")File.AppendAllText(data.Prepared["reopen"]!["savedResult"]!["path"]!.GetValue<string>()," changed");
        if(fault=="fresh_choice")proof["modelId"]=9;
        if(fault=="retained_fence")data.Data.Target.Data.Files.Write("workingcopies/"+data.Prepared["reopenId"]!.GetValue<string>()+"/mutation-fence.json","unresolved operation");
        if(fault=="missing_return")File.AppendAllText(FileArtifacts.FixedPath(data.Root,"operations",data.Operation,"000003.json")," ");
        data.Data.Target.Data.Files.Write("responses/"+verified["bridgeRequestId"]!.GetValue<string>()+".json",verified.ToJsonString());
        Assert.Throws<ReferenceRejected>(()=>OwnedReopenProof.IssueBinding(session,verified,proof,fresh["candidateDocumentHandle"]!.GetValue<string>()));
    }
    [Fact]
    public void ForwardSlashNativeModelPathIssuesOnlyTheOwnedWorkingBinding()
    {
        var data=new SavedReopenFixture();var session=data.Session;
        var parameters=OwnedExecutionProof.SavedReopenParameters(OpenArgs(data),session.Candidates,session.Reopens,session.Documents);
        data.Execution(Guid.NewGuid().ToString("N"),parameters);session.RevokeCadLeases(true);
        var fresh=data.FreshReads(forwardPath:true);var proof=OwnedExecutionProof.VerificationParameters(fresh,session.Candidates,session.Documents,false,false,true);
        var verified=data.Verification(Guid.NewGuid().ToString("N"),proof);
        var originalBytes=File.ReadAllBytes(FileArtifacts.FixedPath(data.Root,"responses",verified["bridgeRequestId"]!.GetValue<string>()+".json"));
        var issued=OwnedReopenProof.IssueBinding(session,verified,proof,fresh["candidateDocumentHandle"]!.GetValue<string>());
        Assert.Contains('/',issued["model"]!["path"]!.GetValue<string>());Assert.NotNull(issued["workingDocumentBindingHandle"]);
        Assert.Equal(data.Prepared["reopenId"]!.GetValue<string>(),session.Copies.InspectParameters(issued["workingCopyHandle"]!.GetValue<string>())["copyId"]!.GetValue<string>());
        Assert.Equal(originalBytes,File.ReadAllBytes(FileArtifacts.FixedPath(data.Root,"responses",verified["bridgeRequestId"]!.GetValue<string>()+".json")));
    }
    [Fact]
    public async Task CancellationAfterPublishedOpenRetainsClaimedEnvelopeAndCannotReplay()
    {
        var data=new SavedReopenFixture();var args=OpenArgs(data);data.Data.Target.Data.Files.Write("PSBridge.Worker.exe","Opaque marker; never executed.");using var cancel=new CancellationTokenSource();
        var tools=new OwnedSavedReopenTools(data.Session);var call=tools.ExecuteOwnedSavedReopen(data.Handle,args["candidateDocumentHandle"]!.GetValue<string>(),args["activeDocumentHandle"]!.GetValue<string>(),cancel.Token);
        var request=await data.Data.Target.Data.Files.ClaimRequest();cancel.Cancel();var result=await call;Assert.True(result.IsError);
        var body=JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!;Assert.True(body["contextChangeMayHaveOccurred"]!.GetValue<bool>());Assert.False(body["retryAllowed"]!.GetValue<bool>());
        Assert.Throws<ReferenceRejected>(()=>data.Session.Reopens.Parameters(data.Handle));Assert.True(File.Exists(request+".processing"));Assert.Single(Directory.GetFiles(Path.Combine(data.Root,"requests")));
    }
    [Fact]
    public void ReadonlyCatalogExcludesNewExecutionAndVerifier()
    {
        var fixture=new OwnedSaveTargetTests.TargetFixture(readOnly:true,saves:true,reopen:true);
        foreach(var name in new[]{"execute_owned_saved_reopen","verify_owned_saved_reopen"})Assert.Throws<McpProtocolException>(()=>fixture.Session.AssertToolAllowed(name));
        Assert.Equal(6,ToolCatalog.Create(fixture.Session).Length);
    }
}
