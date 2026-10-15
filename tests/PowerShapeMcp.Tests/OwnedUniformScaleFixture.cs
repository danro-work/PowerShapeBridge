using System.Text.Json.Nodes;
using PowerShapeMcp;

namespace PowerShapeMcp.Tests;

// Real owned bytes/checkpoint and synthetic observation metadata; never CAD.
internal sealed class OwnedUniformScaleFixture
{
    internal readonly Fixture Files=new();internal string Root=>Files.Root;
    internal double Time=100;internal DateTimeOffset Utc=new(2026,10,12,10,0,0,TimeSpan.Zero);
    internal readonly CandidateStore Candidates;internal readonly WorkingCopyStore Copies;internal readonly DocumentBindingStore Bindings;
    internal readonly UniformScalePlanStore Plans;internal readonly OwnedUniformScaleStore Store;
    internal readonly JsonObject Source,BindingPacket,Parameters;internal readonly string Plan,Binding,Working,Checkpoint,OwnerPath;
    internal OwnedUniformScaleFixture(bool mixed=false,DateTimeOffset? utc=null,Action<JsonObject>? sourceSetup=null)
    {
        if(utc is not null)Utc=utc.Value;
        Candidates=new(()=>Time,()=>Utc);Copies=new(()=>Time,()=>Utc);Bindings=new(Root,Candidates,Copies,()=>Time,()=>Utc);
        Plans=new(Root,Candidates,()=>Time,()=>Utc);Store=new(Root,Plans,Bindings,()=>Time,()=>Utc);
        var copyId=Guid.NewGuid().ToString("N");Working=FileArtifacts.FixedPath(Root,"workingcopies",copyId,"Working_"+copyId+".psmodel");
        Checkpoint=FileArtifacts.FixedPath(Root,"workingcopies",copyId,"checkpoint","Before.psmodel");
        Files.Write("Source.psmodel","Opaque own fixture, never load into CAD.");Files.Write(Path.GetRelativePath(Root,Working),"Opaque own fixture, never load into CAD.");Files.Write(Path.GetRelativePath(Root,Checkpoint),"Opaque own fixture, never load into CAD.");
        File.SetAttributes(Checkpoint,FileAttributes.ReadOnly);var sha=FileArtifacts.Sha256(File.ReadAllBytes(Working));var size=new FileInfo(Working).Length;
        var owner=new JsonObject{["schemaVersion"]=2,["copyId"]=copyId,["bridgeVersion"]="0.34.0-dev",["createdUtc"]=LeaseTime.Format(Utc),["scope"]="saved_disk_bytes_only",["executionEnabled"]=false,
            ["source"]=new JsonObject{["path"]=Path.Combine(Root,"Source.psmodel"),["sha256"]=sha,["length"]=size},
            ["working"]=new JsonObject{["path"]=Working,["initialSha256"]=sha,["length"]=size},
            ["checkpoint"]=new JsonObject{["path"]=Checkpoint,["sha256"]=sha,["length"]=size}};
        OwnerPath=FileArtifacts.FixedPath(Root,"workingcopies",copyId,"ownership.json");var ownerSha=ArtifactProof.Freeze(Root,Path.GetRelativePath(Root,OwnerPath),owner);
        var copy=Copies.Issue(new(){["state"]="working_copy_prepared_disk",["copyId"]=copyId,["bridgeRequestId"]=copyId,["ownershipSha256"]=ownerSha,["timestampUtc"]=LeaseTime.Format(Utc)})["workingCopyHandle"]!.GetValue<string>();
        Source=new SafetyFixture().Source.DeepClone().AsObject();Source["timestampUtc"]=LeaseTime.Format(Utc);Source["model"]!["path"]=Working;
        sourceSetup?.Invoke(Source);
        var sourceId=Guid.NewGuid().ToString("N");Source["bridgeRequestId"]=sourceId;var sourceSha=ArtifactProof.Freeze(Root,"responses/"+sourceId+".json",Source);
        var document=Candidates.Issue(Source,sourceSha)["candidateDocumentHandle"]!.GetValue<string>();
        var bindingId=Guid.NewGuid().ToString("N");var bindingParameters=Bindings.PrepareParameters(document,copy);BindingPacket=bindingParameters.DeepClone().AsObject();
        BindingPacket["schemaVersion"]=1;BindingPacket["bindingId"]=bindingId;BindingPacket["bridgeVersion"]="0.34.0-dev";BindingPacket["timestampUtc"]=LeaseTime.Format(Utc);
        BindingPacket["observedPropertiesSha256"]=new string('e',64);BindingPacket["windowContextSha256"]=new string('f',64);BindingPacket["executionEnabled"]=false;
        var bindingSha=ArtifactProof.Freeze(Root,"documentbindings/"+bindingId+".json",BindingPacket);
        Binding=Bindings.Issue(new(){["state"]="working_document_bound_observed",["bridgeRequestId"]=bindingId,["bindingId"]=bindingId,["bindingSha256"]=bindingSha,["binding"]=BindingPacket.DeepClone()},bindingParameters,document,copy)["workingDocumentBindingHandle"]!.GetValue<string>();
        var keys=mixed?new JsonArray(new JsonArray(40,5),new JsonArray(21,8),new JsonArray(59,10)):new JsonArray(new JsonArray(40,5));
        var setId=Guid.NewGuid().ToString("N");var set=new JsonObject{["inspectionRequestId"]=sourceId,["inspectionSha256"]=sourceSha,["expiresUtc"]=BindingPacket["expiresUtc"]!.DeepClone(),["entityKeys"]=keys};
        var setSha=ArtifactProof.Freeze(Root,"entitysets/"+setId+".json",set);
        var setHandle=Candidates.IssueSet(new(){["state"]="entity_set_prepared_observed",["entitySetId"]=setId,["entitySetSha256"]=setSha,["expiresUtc"]=set["expiresUtc"]!.DeepClone()})["entitySetHandle"]!.GetValue<string>();
        Plan=Plans.Prepare(setHandle,JsonNode.Parse("""{"factor":2,"centre":[10,20,30],"coordinateFrame":"world","lengthUnits":"document_raw","mode":"scale_in_place"}""")!.AsObject())["scalePlanHandle"]!.GetValue<string>();
        Parameters=Store.PrepareParameters(Plan,Binding);
    }
    internal JsonObject Native(string id,Action<JsonObject>? mutate=null)
    {
        var plan=Plans.Inspect(Plan);var set=OperationPlanStore.ReadSet(Root,plan["entitySet"]!.AsObject(),Utc);var packet=Parameters.DeepClone().AsObject();
        packet["schemaVersion"]=1;packet["ownedScaleId"]=id;packet["operation"]="uniform_scale_entities";packet["preparedUtc"]=LeaseTime.Format(Utc);packet["expiresUtc"]=BindingPacket["expiresUtc"]!.DeepClone();packet["executionEnabled"]=false;
        foreach(var field in new[]{"copyId","ownershipSha256","windowContextSha256"})packet[field]=BindingPacket[field]!.DeepClone();
        packet["workingSha256"]=FileArtifacts.Sha256(File.ReadAllBytes(Working));packet["nativeObservedPropertiesSha256"]=BindingPacket["observedPropertiesSha256"]!.DeepClone();
        packet["entityKeys"]=set.Plan["entityKeys"]!.DeepClone();packet["setInspectionRequestId"]=set.Plan["inspectionRequestId"]!.DeepClone();packet["setInspectionSha256"]=set.Plan["inspectionSha256"]!.DeepClone();
        packet["bindingInspectionRequestId"]=BindingPacket["inspectionRequestId"]!.DeepClone();packet["bindingInspectionSha256"]=BindingPacket["inspectionSha256"]!.DeepClone();
        packet["lengthUnitsRaw"]=Source["lengthUnitsRaw"]?.DeepClone();packet["lengthUnitFactorRaw"]=Source["lengthUnitFactorRaw"]?.DeepClone();packet["nativeProviderQualified"]=false;
        packet["requiredBeforeExecution"]=plan["requiredBeforeExecution"]!.DeepClone();mutate?.Invoke(packet);
        var sha=ArtifactProof.Freeze(Root,"ownedscales/"+id+".json",packet);PreparedJournal.Create(Root,id,sha,Utc,new(){["executionEnabled"]=false,["geometryMutationAttempted"]=false});
        return new(){["state"]="owned_uniform_scale_prepared_observed",["bridgeRequestId"]=id,["ownedScaleId"]=id,["ownedScaleSha256"]=sha,["ownedScale"]=packet,["expiresUtc"]=packet["expiresUtc"]!.DeepClone(),
            ["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,["nativeProviderQualified"]=false,["guardPasses"]=2,["sameContextObservationReads"]=4};
    }
}
