using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedDgkTargetStoreTests
{
    internal sealed class Data
    {
        internal readonly OwnedUniformScaleFixture Base=new(true);
        internal string Root=>Base.Root;
        internal readonly OwnedDgkTargetStore Store;
        internal readonly string Set;
        internal readonly JsonObject Request;
        internal Data()
        {
            var set=new JsonObject{["state"]="entity_set_prepared_observed",["entitySetId"]=Base.Parameters["entitySetId"]!.DeepClone(),["entitySetSha256"]=Base.Parameters["entitySetSha256"]!.DeepClone(),["expiresUtc"]=Base.BindingPacket["expiresUtc"]!.DeepClone()};
            Set=Base.Candidates.IssueSet(set)["entitySetHandle"]!.GetValue<string>();
            Store=new(Root,Base.Candidates,Base.Bindings,()=>Base.Time,()=>Base.Utc);
            Request=Store.PrepareParameters(Set,Base.Binding,Parameters());
        }
        internal static JsonObject Parameters()=>JsonNode.Parse("""{"coordinateFrame":"world","exportVariant":"geometry","triangleCompanions":[],"featureCompanions":[],"settingsQualification":"planned_unqualified"}""")!.AsObject();
        internal JsonObject Native(string id,Action<JsonObject>? mutate=null)
        {
            var packet=Request.DeepClone().AsObject();var binding=Base.BindingPacket;
            packet["schemaVersion"]=1;packet["targetId"]=id;packet["operation"]="export_owned_dgk";packet["format"]="dgk";
            packet["selectionPolicy"]="explicit_target_set; no empty-selection fallback";packet["parametersSha256"]=ObservationPolicy.Digest(packet["parameters"]!);
            packet["preparedUtc"]=LeaseTime.Format(Base.Utc);packet["expiresUtc"]=binding["expiresUtc"]!.DeepClone();
            foreach(var field in new[]{"executionEnabled","exportSettingsQualified","nativeProviderQualified"})packet[field]=false;
            foreach(var field in new[]{"copyId","ownershipSha256","inspectionRequestId","inspectionSha256","windowContextSha256"})packet[field]=binding[field]!.DeepClone();
            packet["nativeObservedPropertiesSha256"]=binding["observedPropertiesSha256"]!.DeepClone();
            var set=OperationPlanStore.ReadSet(Root,new(){["entitySetId"]=Request["entitySetId"]!.DeepClone(),["entitySetSha256"]=Request["entitySetSha256"]!.DeepClone()},Base.Utc);
            packet["entityKeys"]=set.Plan["entityKeys"]!.DeepClone();packet["workingPath"]=Base.Working;packet["workingSha256"]=FileArtifacts.Sha256(File.ReadAllBytes(Base.Working));
            packet["checkpointPath"]=Base.Checkpoint;packet["checkpointSha256"]=FileArtifacts.Sha256(File.ReadAllBytes(Base.Checkpoint));
            var folder=FileArtifacts.FixedPath(Root,"dgkexports",id);var bundle=FileArtifacts.FixedPath(Root,"dgkexports",id,"artifacts");Directory.CreateDirectory(bundle);
            packet["bundleDirectory"]=bundle;packet["targetPath"]=Path.Combine(bundle,"Geometry_"+id+".dgk");packet["artifactManifest"]=DgkTargetDefinition.Manifest(id,packet["parameters"]!.AsObject());
            packet["directoryIdentity"]=WindowsDirectoryIdentity.Read(folder);packet["bundleDirectoryIdentity"]=WindowsDirectoryIdentity.Read(bundle);packet["fenceObservation"]=new JsonObject{["present"]=false};
            packet["qualification"]="Disabled DGK target/bundle plan; settings/provider/geometry unqualified; no CAD command authority";
            var reservation=new JsonObject{["schemaVersion"]=1,["targetId"]=id,["format"]="dgk",["bindingId"]=Request["bindingId"]!.DeepClone(),["bindingSha256"]=Request["bindingSha256"]!.DeepClone(),["parametersSha256"]=packet["parametersSha256"]!.DeepClone(),["automaticReplayForbidden"]=true};
            ArtifactProof.Freeze(Root,"dgkexports/"+id+".reservation.json",reservation);mutate?.Invoke(packet);
            var sha=ArtifactProof.Freeze(Root,"dgkexports/"+id+"/target.json",packet);
            var result=new JsonObject{["state"]="owned_dgk_target_prepared_files",["bridgeRequestId"]=id,["targetId"]=id,["targetSha256"]=sha,["target"]=packet};
            foreach(var field in new[]{"executionEnabled","exportAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","powerShapeConnectionAttempted","retryAllowed","exportSettingsQualified","nativeProviderQualified"})result[field]=false;
            return result;
        }
    }
    [Fact]
    public void PrivateHandleValidatesAgainAndResponseIsDetached()
    {
        var data=new Data();var result=data.Native(Guid.NewGuid().ToString("N"));
        var issued=data.Store.Issue(result,data.Request,data.Set,data.Base.Binding);var handle=issued["ownedDgkTargetHandle"]!.GetValue<string>();
        issued["target"]!["targetPath"]="foreign";
        Assert.Equal(result["targetId"]!.GetValue<string>(),data.Store.ValidationParameters(handle)["targetId"]!.GetValue<string>());
        Assert.Throws<ReferenceRejected>(()=>new OwnedDgkTargetStore(data.Root,data.Base.Candidates,data.Base.Bindings).ValidationParameters(handle));
        data.Store.Revoke(handle);Assert.Throws<ReferenceRejected>(()=>data.Store.ValidationParameters(handle));
    }
    [Theory]
    [InlineData("targetPath")][InlineData("bundleDirectory")][InlineData("entityKeys")][InlineData("parametersSha256")]
    [InlineData("nativeObservedPropertiesSha256")][InlineData("expiresUtc")][InlineData("manifest")][InlineData("extra")][InlineData("executionEnabled")]
    public void TamperedNativePacketIsRefused(string fault)
    {
        var data=new Data();var result=data.Native(Guid.NewGuid().ToString("N"),packet=>
        {
            if(fault is "targetPath" or "bundleDirectory")packet[fault]=data.Base.Working;
            if(fault=="entityKeys")packet[fault]=new JsonArray(new JsonArray(40,999));
            if(fault is "parametersSha256" or "nativeObservedPropertiesSha256")packet[fault]=new string('f',64);
            if(fault=="expiresUtc")packet[fault]=LeaseTime.Format(data.Base.Utc.AddSeconds(400));
            if(fault=="manifest")packet["artifactManifest"]=new JsonArray();
            if(fault=="extra")packet["command"]="Never execute";
            if(fault=="executionEnabled")packet[fault]=true;
        });
        Assert.Throws<ReferenceRejected>(()=>data.Store.Issue(result,data.Request,data.Set,data.Base.Binding));
    }
    [Theory][InlineData("request")][InlineData("sha")][InlineData("flag")][InlineData("working")][InlineData("checkpoint")][InlineData("claim")][InlineData("output")][InlineData("fence")][InlineData("reservation")]
    public void ReplyAndOwnedArtifactDriftAreRefused(string fault)
    {
        var data=new Data();var id=Guid.NewGuid().ToString("N");var result=data.Native(id);
        if(fault=="request")result["bridgeRequestId"]=Guid.NewGuid().ToString("N");
        if(fault=="sha")result["targetSha256"]=new string('f',64);
        if(fault=="flag")result["nativeProviderQualified"]=true;
        if(fault=="working")File.AppendAllText(data.Base.Working,"changed");
        if(fault=="checkpoint")File.SetAttributes(data.Base.Checkpoint,FileAttributes.Normal);
        if(fault=="claim")File.WriteAllText(FileArtifacts.FixedPath(data.Root,"dgkexports",id,"export-once.json"),"existing");
        if(fault=="output")File.WriteAllText(FileArtifacts.FixedPath(data.Root,"dgkexports",id,"artifacts","stray.xml"),"stray");
        if(fault=="fence")File.WriteAllText(Path.Combine(Path.GetDirectoryName(data.Base.Working)!,"mutation-fence.json"),"unknown");
        if(fault=="reservation")File.AppendAllText(FileArtifacts.FixedPath(data.Root,"dgkexports",id+".reservation.json"),"invalid");
        Assert.ThrowsAny<Exception>(()=>data.Store.Issue(result,data.Request,data.Set,data.Base.Binding));
    }
    [Fact]
    public void HandleExpiresAndTargetHashDriftCannotBeRevived()
    {
        var data=new Data();var id=Guid.NewGuid().ToString("N");var result=data.Native(id);var handle=data.Store.Issue(result,data.Request,data.Set,data.Base.Binding)["ownedDgkTargetHandle"]!.GetValue<string>();
        File.AppendAllText(FileArtifacts.FixedPath(data.Root,"dgkexports",id,"target.json")," ");Assert.Throws<ReferenceRejected>(()=>data.Store.ValidationParameters(handle));
        data.Base.Time+=301;data.Base.Utc=data.Base.Utc.AddSeconds(301);Assert.Throws<ReferenceRejected>(()=>data.Store.ValidationParameters(handle));
    }
    [Theory][InlineData("frame")][InlineData("numeric_required")][InlineData("unsafe_name")][InlineData("features_missing")][InlineData("extra")][InlineData("duplicate")]
    public void ParametersCannotInjectCommandsOrImplicitSelection(string fault)
    {
        var value=Data.Parameters();
        if(fault=="frame")value["coordinateFrame"]=true;
        if(fault=="numeric_required")value["triangleCompanions"]=JsonNode.Parse("""[{"suffix":".dmt","required":1}]""");
        if(fault=="unsafe_name"){value["exportVariant"]="geometry_features";value["featureCompanions"]=JsonNode.Parse("""[{"name":"../holes.mac","role":"features_mac","required":true}]""");}
        if(fault=="features_missing")value["exportVariant"]="geometry_features";
        if(fault=="extra")value["command"]="Never execute";
        if(fault=="duplicate")value["triangleCompanions"]=JsonNode.Parse("""[{"suffix":".dmt","required":true},{"suffix":".dmt","required":false}]""");
        Assert.Throws<ReferenceRejected>(()=>DgkTargetDefinition.Read(value));
    }
}
