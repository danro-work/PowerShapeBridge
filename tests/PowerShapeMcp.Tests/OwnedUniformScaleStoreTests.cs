using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedUniformScaleStoreTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void CompleteNativePacketIssuesOnlyConnectionScopedPassiveHandle(bool mixed)
    {
        var f=new OwnedUniformScaleFixture(mixed);var native=f.Native(Guid.NewGuid().ToString("N"));
        var output=f.Store.Issue(native,f.Parameters,f.Plan,f.Binding);var handle=output["ownedScaleHandle"]!.GetValue<string>();
        Assert.Equal(native["ownedScaleId"]!.GetValue<string>(),f.Store.ValidationParameters(handle)["ownedScaleId"]!.GetValue<string>());
        Assert.False(output["executionEnabled"]!.GetValue<bool>());Assert.False(Directory.Exists(Path.Combine(f.Root,"requests")));
        Assert.Throws<ReferenceRejected>(()=>new OwnedUniformScaleStore(f.Root,f.Plans,f.Bindings,()=>f.Time,()=>f.Utc).ValidationParameters(handle));
    }
    [Theory][InlineData("reply_identity")][InlineData("geometry_flag")][InlineData("guard_count")]
    [InlineData("packet_bytes")][InlineData("journal_lock")][InlineData("owner_bytes")][InlineData("working_bytes")]
    [InlineData("checkpoint_writable")][InlineData("checkpoint_bytes")][InlineData("fence")][InlineData("expired")]
    public void InvalidProofOrOwnedBytesCannotIssueHandle(string fault)
    {
        var f=new OwnedUniformScaleFixture();var id=Guid.NewGuid().ToString("N");var result=f.Native(id);
        if(fault=="reply_identity")result["bridgeRequestId"]=Guid.NewGuid().ToString("N");
        if(fault=="geometry_flag")result["geometryMutationAttempted"]=true;
        if(fault=="guard_count")result["sameContextObservationReads"]=2;
        if(fault=="packet_bytes")f.Files.Write("ownedscales/"+id+".json","{}");
        if(fault=="journal_lock")f.Files.Write("operations/"+id+"/writer.lock","interrupted");
        if(fault=="owner_bytes")File.AppendAllText(f.OwnerPath," ");
        if(fault=="working_bytes")File.AppendAllText(f.Working,"changed");
        if(fault=="checkpoint_writable")File.SetAttributes(f.Checkpoint,FileAttributes.Normal);
        if(fault=="checkpoint_bytes"){File.SetAttributes(f.Checkpoint,FileAttributes.Normal);File.AppendAllText(f.Checkpoint,"changed");File.SetAttributes(f.Checkpoint,FileAttributes.ReadOnly);}
        if(fault=="fence")File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.Working)!,"geometry-export-fence.json"),"unresolved");
        if(fault=="expired"){f.Time+=301;f.Utc=f.Utc.AddSeconds(301);}
        Assert.Throws<ReferenceRejected>(()=>f.Store.Issue(result,f.Parameters,f.Plan,f.Binding));
    }
    [Theory][InlineData("parameters")][InlineData("keys")][InlineData("requirements")][InlineData("units")]
    [InlineData("native_flag")][InlineData("schema_extra")][InlineData("expiry_extended")][InlineData("prepared_before_source")]
    public void SelfConsistentButWrongNativePacketIsRejected(string fault)
    {
        var f=new OwnedUniformScaleFixture();var result=f.Native(Guid.NewGuid().ToString("N"),packet=>
        {
            if(fault=="parameters")packet["parameters"]!["factor"]=3;
            if(fault=="keys")packet["entityKeys"]=new JsonArray(new JsonArray(40,99));
            if(fault=="requirements")packet["requiredBeforeExecution"]=new JsonArray();
            if(fault=="units")packet["lengthUnitFactorRaw"]=2;
            if(fault=="native_flag")packet["nativeProviderQualified"]=true;
            if(fault=="schema_extra")packet["command"]="NeverExecute";
            if(fault=="expiry_extended")packet["expiresUtc"]=LeaseTime.Format(f.Utc.AddSeconds(301));
            if(fault=="prepared_before_source")packet["preparedUtc"]=LeaseTime.Format(f.Utc.AddSeconds(-1));
        });
        Assert.Throws<ReferenceRejected>(()=>f.Store.Issue(result,f.Parameters,f.Plan,f.Binding));
    }
    [Theory][InlineData("revoke")][InlineData("clear")][InlineData("working_bytes")][InlineData("late_fence")]
    public void IssuedHandleRechecksArtifactsAndCanBeRevoked(string fault)
    {
        var f=new OwnedUniformScaleFixture();var result=f.Native(Guid.NewGuid().ToString("N"));
        var handle=f.Store.Issue(result,f.Parameters,f.Plan,f.Binding)["ownedScaleHandle"]!.GetValue<string>();
        if(fault=="revoke")f.Store.Revoke(handle);
        if(fault=="clear")f.Store.Clear();
        if(fault=="working_bytes")File.AppendAllText(f.Working,"changed");
        if(fault=="late_fence")Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(f.Working)!,"mutation-fence.json"));
        Assert.Throws<ReferenceRejected>(()=>f.Store.ValidationParameters(handle));
    }
}
