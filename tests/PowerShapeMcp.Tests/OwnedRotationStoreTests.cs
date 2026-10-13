using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedRotationStoreTests
{
    [Fact]
    public void ValidNativePacketIssuesOnlyPassiveScopedHandle()
    {
        var f=new OwnedRotationFixture();var native=f.Native(Guid.NewGuid().ToString("N"));var result=f.Store.Issue(native,f.Parameters,f.Plan,f.Data.Binding);
        var handle=result["ownedRotationHandle"]!.GetValue<string>();Assert.False(result["executionEnabled"]!.GetValue<bool>());
        Assert.Equal(native["ownedRotationId"]!.GetValue<string>(),f.Store.ValidationParameters(handle)["ownedRotationId"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>new OwnedRotationStore(f.Data.Root,f.Plans,f.Data.Bindings,()=>f.Data.Time,()=>f.Data.Utc).ValidationParameters(handle));
        Assert.False(Directory.Exists(Path.Combine(f.Data.Root,"requests")));
    }
    [Theory]
    [InlineData("reply_identity")]
    [InlineData("execution_flag")]
    [InlineData("packet_bytes")]
    [InlineData("binding_changed")]
    [InlineData("journal_changed")]
    [InlineData("expired")]
    public void InvalidNativeProofCannotIssueHandle(string fault)
    {
        var f=new OwnedRotationFixture();var id=Guid.NewGuid().ToString("N");var native=f.Native(id);
        if(fault=="reply_identity")native["bridgeRequestId"]=Guid.NewGuid().ToString("N");
        if(fault=="execution_flag")native["geometryMutationAttempted"]=true;
        if(fault=="packet_bytes")f.Data.Files.Write("ownedrotations/"+id+".json","{}");
        if(fault=="binding_changed")f.Data.Files.Write("documentbindings/"+f.Parameters["bindingId"]!.GetValue<string>()+".json","{}");
        if(fault=="journal_changed")f.Data.Files.Write("operations/"+id+"/writer.lock","Own interrupted publication.");
        if(fault=="expired"){f.Data.Time+=301;f.Data.Utc=f.Data.Utc.AddSeconds(301);}
        Assert.Throws<ReferenceRejected>(()=>f.Store.Issue(native,f.Parameters,f.Plan,f.Data.Binding));
    }
    [Fact]
    public void PlanAndBindingMustDescribeSameObservedDocument()
    {
        var f=new OwnedRotationFixture();var bindingId=f.Parameters["bindingId"]!.GetValue<string>();var path=FileArtifacts.FixedPath(f.Data.Root,"documentbindings",bindingId+".json");
        var changed=JsonNode.Parse(File.ReadAllText(path))!.AsObject();changed["inspectionSha256"]=new string('f',64);f.Data.Files.Write(Path.GetRelativePath(f.Data.Root,path),changed.ToJsonString());
        Assert.Throws<ReferenceRejected>(()=>f.Store.PrepareParameters(f.Plan,f.Data.Binding));
    }
    [Fact]
    public void RevokeInvalidatesOnlyNewHandle()
    {
        var f=new OwnedRotationFixture();var first=f.Store.Issue(f.Native(Guid.NewGuid().ToString("N")),f.Parameters,f.Plan,f.Data.Binding)["ownedRotationHandle"]!.GetValue<string>();
        var second=f.Store.Issue(f.Native(Guid.NewGuid().ToString("N")),f.Parameters,f.Plan,f.Data.Binding)["ownedRotationHandle"]!.GetValue<string>();
        f.Store.Revoke(second);Assert.Throws<ReferenceRejected>(()=>f.Store.ValidationParameters(second));Assert.NotNull(f.Store.ValidationParameters(first));
    }
}
