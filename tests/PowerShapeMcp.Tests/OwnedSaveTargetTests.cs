using System.Text.Json.Nodes;
using PowerShapeMcp;
using Xunit;

namespace PowerShapeMcp.Tests;

public sealed class OwnedSaveTargetTests
{
    internal sealed class TargetFixture
    {
        public SafetyFixture Data { get; }=new();
        public DocumentBindingStore Bindings { get; }
        public OwnedSaveTargetStore Targets { get; }
        public BridgeSession Session { get; }
        public string Binding { get; }
        public string Id { get; }=Guid.NewGuid().ToString("N");
        public JsonObject Parameters { get; }
        public JsonObject Receipt { get; }
        public JsonObject Plan=>Receipt["target"]!.AsObject();
        public TargetFixture(bool readOnly=false,bool enabled=true,bool saves=false,bool reopen=false,bool restore=false,bool close=false)
        {
            foreach(var flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})Data.Files.Config[flag]=true;
            Data.Files.Config["ownedSaveTargetsExperimental"]=enabled;
            Data.Files.Config["ownedSavesExperimental"]=saves;
            if(reopen){Data.Files.Config["workingCopyOpenExperimental"]=true;Data.Files.Config["ownedSavedReopenExperimental"]=true;}
            if(restore)Data.Files.Config["ownedSavedResultRestoreExperimental"]=true;
            if(close)Data.Files.Config["ownedSavedCopyCloseExperimental"]=true;
            // No session observes intermediate configuration. Publish once to
            // avoid redundant replacement of the same isolated fixture file.
            Data.Files.SaveConfig();
            // Fake replies fsync complete durable plans/journals. Allow a loaded
            // runner to finish those writes; dedicated DeadlineTests cover timing.
            Session=new(Data.Files.Options(readOnly,close?10:5),()=>Data.Time,()=>Data.Utc);
            var copies=Session.Copies;var candidates=Session.Candidates;Bindings=Session.Bindings;Targets=Session.SaveTargets;
            var copyId=Guid.NewGuid().ToString("N");var work=FileArtifacts.FixedPath(Data.Root,"workingcopies",copyId,"Working_"+copyId+".psmodel");
            var checkpoint=FileArtifacts.FixedPath(Data.Root,"workingcopies",copyId,"checkpoint","Before.psmodel");
            var hash=new string('a',64);var ownership=new JsonObject{["schemaVersion"]=2,["copyId"]=copyId,["bridgeVersion"]="0.33.0-dev",["createdUtc"]=LeaseTime.Format(Data.Utc),
                ["scope"]="saved_disk_bytes_only",["executionEnabled"]=false,["source"]=new JsonObject{["path"]="C:/Source.psmodel"},
                ["working"]=new JsonObject{["path"]=work,["initialSha256"]=hash},["checkpoint"]=new JsonObject{["path"]=checkpoint,["sha256"]=hash}};
            var ownershipSha=ArtifactProof.Freeze(Data.Root,"workingcopies/"+copyId+"/ownership.json",ownership);
            var copy=copies.Issue(new(){["state"]="working_copy_prepared_disk",["copyId"]=copyId,["bridgeRequestId"]=copyId,["ownershipSha256"]=ownershipSha,["timestampUtc"]=LeaseTime.Format(Data.Utc)})["workingCopyHandle"]!.GetValue<string>();
            var source=Data.Source.DeepClone().AsObject();source["model"]!["path"]=work;var sourceId=Guid.NewGuid().ToString("N");source["bridgeRequestId"]=sourceId;
            var sourceSha=ArtifactProof.Freeze(Data.Root,"responses/"+sourceId+".json",source);
            var doc=candidates.Issue(source,sourceSha)["candidateDocumentHandle"]!.GetValue<string>();
            var bindingId=Guid.NewGuid().ToString("N");var parameters=Bindings.PrepareParameters(doc,copy);var binding=parameters.DeepClone().AsObject();
            binding["schemaVersion"]=1;binding["bindingId"]=bindingId;binding["bridgeVersion"]="0.33.0-dev";binding["timestampUtc"]=LeaseTime.Format(Data.Utc);
            binding["observedPropertiesSha256"]=new string('e',64);binding["windowContextSha256"]=new string('f',64);binding["executionEnabled"]=false;
            var bindingSha=ArtifactProof.Freeze(Data.Root,"documentbindings/"+bindingId+".json",binding);
            Binding=Bindings.Issue(new(){["state"]="working_document_bound_observed",["bindingId"]=bindingId,["bindingSha256"]=bindingSha,["binding"]=binding,["bridgeRequestId"]=bindingId},parameters,doc,copy)["workingDocumentBindingHandle"]!.GetValue<string>();
            Parameters=Targets.PrepareParameters(Binding);
            var plan=new JsonObject{["schemaVersion"]=1,["targetId"]=Id,["operation"]="save_owned_document_as",["bridgeVersion"]="0.33.0-dev",["preparedUtc"]=LeaseTime.Format(Data.Utc),
                ["expiresUtc"]=binding["expiresUtc"]!.DeepClone(),["executionEnabled"]=false,["bindingId"]=bindingId,["bindingSha256"]=bindingSha,
                ["copyId"]=copyId,["ownershipSha256"]=ownershipSha,["inspectionRequestId"]=sourceId,["inspectionSha256"]=sourceSha,
                ["nativeObservedPropertiesSha256"]=new string('e',64),["beforeModel"]=source["model"]!.DeepClone(),
                ["workingPath"]=work,["workingSha256"]=hash,["checkpointPath"]=checkpoint,["checkpointSha256"]=hash,
                ["resultPath"]=FileArtifacts.FixedPath(Data.Root,"savedresults",Id,"Result_"+Id+".psmodel"),
                ["directoryIdentity"]=new JsonObject{["volumeSerial"]=new string('a',8),["fileIndex"]=new string('b',16),["creationTime"]=new string('c',16)}};
            var sha=ArtifactProof.Freeze(Data.Root,"savedresults/"+Id+"/target.json",plan);
            Receipt=new(){["state"]="owned_save_target_prepared_files",["targetId"]=Id,["targetSha256"]=sha,["target"]=plan,["bridgeRequestId"]=Id,
                ["executionEnabled"]=false,["cadSaveAttempted"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false};
        }
        public string Issue()=>Targets.Issue(Receipt,Parameters,Binding)["ownedSaveAsHandle"]!.GetValue<string>();
        public void Rewrite(){var raw=PythonJson.Indented(System.Text.Json.JsonSerializer.SerializeToElement(Plan));Data.Files.Write("savedresults/"+Id+"/target.json",raw);Receipt["targetSha256"]=FileArtifacts.Sha256(System.Text.Encoding.UTF8.GetBytes(raw));}
    }

    [Fact]
    public void PassiveTargetIssuesConnectionLeaseAndUsesMinimumDependencyExpiry()
    {
        var fixture=new TargetFixture();fixture.Data.Time+=20;fixture.Data.Utc=fixture.Data.Utc.AddSeconds(20);
        var issued=fixture.Targets.Issue(fixture.Receipt,fixture.Parameters,fixture.Binding);Assert.Equal(280,issued["saveAsTargetLifetimeSeconds"]!.GetValue<int>());
        var parameters=fixture.Targets.ValidationParameters(issued["ownedSaveAsHandle"]!.GetValue<string>());Assert.Equal(fixture.Id,parameters["targetId"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(fixture.Data.Root,"requests")));
        Assert.False(File.Exists(fixture.Plan["resultPath"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("bool_schema")]
    [InlineData("enabled")]
    [InlineData("target_source")]
    [InlineData("checkpoint_working")]
    [InlineData("digest")]
    [InlineData("model")]
    [InlineData("directory_identity")]
    [InlineData("extra")]
    public void RecomputedShaCannotMakeForgedTargetDependenciesValid(string fault)
    {
        var fixture=new TargetFixture();
        switch(fault)
        {
            case "bool_schema":fixture.Plan["schemaVersion"]=true;break;
            case "enabled":fixture.Plan["executionEnabled"]=true;break;
            case "target_source":fixture.Plan["resultPath"]=fixture.Plan["workingPath"]!.DeepClone();break;
            case "checkpoint_working":fixture.Plan["checkpointPath"]=fixture.Plan["workingPath"]!.DeepClone();break;
            case "digest":fixture.Plan["nativeObservedPropertiesSha256"]=new string('d',64);break;
            case "model":fixture.Plan["beforeModel"]!["id"]=999;break;
            case "directory_identity":fixture.Plan["directoryIdentity"]!["volumeSerial"]=true;break;
            case "extra":fixture.Plan["command"]="SAVE";break;
        }
        fixture.Rewrite();Assert.Throws<ReferenceRejected>(()=>fixture.Issue());
        Assert.False(Directory.Exists(Path.Combine(fixture.Data.Root,"requests")));
    }

    [Theory]
    [InlineData("result")]
    [InlineData("claim")]
    [InlineData("fence")]
    public void ExistingResultClaimOrFenceRejectsLeaseBeforeAnyQueue(string kind)
    {
        var fixture=new TargetFixture();var handle=fixture.Issue();
        var relative=kind switch{"result"=>"savedresults/"+fixture.Id+"/Result_"+fixture.Id+".psmodel","claim"=>"savedresults/"+fixture.Id+"/save-once.json",_=>"workingcopies/"+fixture.Plan["copyId"]!.GetValue<string>()+"/mutation-fence.json"};
        fixture.Data.Files.Write(relative,"Opaque synthetic collision.");Assert.Throws<ReferenceRejected>(()=>fixture.Targets.ValidationParameters(handle));
        Assert.False(Directory.Exists(Path.Combine(fixture.Data.Root,"requests")));
    }

    [Fact]
    public void ForeignExpiredRevokedAndRawByteChangedTargetsNeverSurvive()
    {
        var fixture=new TargetFixture();var handle=fixture.Issue();var foreign=new OwnedSaveTargetStore(fixture.Data.Root,fixture.Bindings);
        Assert.Throws<ReferenceRejected>(()=>foreign.ValidationParameters(handle));
        File.AppendAllText(Path.Combine(fixture.Data.Root,"savedresults",fixture.Id,"target.json")," ");
        Assert.Throws<ReferenceRejected>(()=>fixture.Targets.ValidationParameters(handle));
        var expired=new TargetFixture();var old=expired.Issue();expired.Data.Time+=301;expired.Data.Utc=expired.Data.Utc.AddSeconds(301);
        Assert.Throws<ReferenceRejected>(()=>expired.Targets.ValidationParameters(old));
        var revoked=new TargetFixture();var valid=revoked.Issue();revoked.Bindings.Clear();Assert.Throws<ReferenceRejected>(()=>revoked.Targets.ValidationParameters(valid));
    }

    [Fact]
    public void BindingLostAfterNativeReturnCannotIssueHandle()
    {
        var fixture=new TargetFixture();fixture.Bindings.Clear();Assert.Throws<ReferenceRejected>(()=>fixture.Issue());
        Assert.False(Directory.Exists(Path.Combine(fixture.Data.Root,"requests")));
    }

    [Fact]
    public void TargetAppearingAfterNativeReturnCannotIssueHandle()
    {
        var fixture=new TargetFixture();fixture.Data.Files.Write("savedresults/"+fixture.Id+"/Result_"+fixture.Id+".psmodel","Opaque late collision.");
        Assert.Throws<ReferenceRejected>(()=>fixture.Issue());Assert.False(Directory.Exists(Path.Combine(fixture.Data.Root,"requests")));
    }
}
