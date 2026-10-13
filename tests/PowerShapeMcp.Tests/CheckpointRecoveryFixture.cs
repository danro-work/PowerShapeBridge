using System.Text.Json.Nodes;
using PowerShapeMcp;

namespace PowerShapeMcp.Tests;

// Synthetic declared file identities; native physical-identity tests are separate.
internal sealed class CheckpointRecoveryFixture
{
    internal readonly Fixture Files=new();internal readonly string Origin=Guid.NewGuid().ToString("N"),Copy=Guid.NewGuid().ToString("N"),Intent=Guid.NewGuid().ToString("N");
    internal double Time=100;internal DateTimeOffset Utc=LeaseTime.PythonPrecision(DateTimeOffset.UtcNow);
    internal readonly BridgeSession Session;internal readonly JsonObject Parameters,Plan,Prepared;internal readonly string PlanSha;
    internal CheckpointRecoveryFixture(bool readOnly=false,bool enabled=true)
    {
        Files.Config["workingCopiesExperimental"]=true;Files.Config["recoveryInspectionExperimental"]=true;Files.Config["checkpointRecoveryExperimental"]=enabled;Files.SaveConfig();
        Session=new(Files.Options(readOnly,5),()=>Time,()=>Utc);
        var working=FileArtifacts.FixedPath(Files.Root,"workingcopies",Copy,"Working_"+Copy+".psmodel");var cp=FileArtifacts.FixedPath(Files.Root,"workingcopies",Copy,"checkpoint","Before.psmodel");
        foreach(var path in new[]{working,cp})Files.Write(Path.GetRelativePath(Files.Root,path),"Opaque saved checkpoint data, never CAD.");File.SetAttributes(cp,FileAttributes.ReadOnly);
        var hash=FileArtifacts.Sha256(File.ReadAllBytes(cp));var length=File.ReadAllBytes(cp).Length;
        var checkpoint=Record(cp,"sha256",hash,length,2);
        var owner=new JsonObject{["schemaVersion"]=2,["copyId"]=Copy,["bridgeVersion"]="0.33.5-dev",["createdUtc"]=LeaseTime.Format(Utc),["scope"]="saved_disk_bytes_only",["executionEnabled"]=false,
            ["source"]=Record("C:/NeverReadThis.psmodel","sha256",hash,length,0),["working"]=Record(working,"initialSha256",hash,length,1),["checkpoint"]=checkpoint.DeepClone()};
        var ownerSha=ArtifactProof.Freeze(Files.Root,"workingcopies/"+Copy+"/ownership.json",owner);
        var origin=new JsonObject{["schemaVersion"]=1,["operationId"]=Origin,["operation"]="open_working_copy",["copyId"]=Copy,["ownershipSha256"]=ownerSha};
        var originSha=ArtifactProof.Freeze(Files.Root,"copyopenplans/"+Origin+".json",origin);Parameters=new(){["operationId"]=Origin,["operationPlanSha256"]=originSha};
        Plan=new(){["schemaVersion"]=1,["recoveryId"]=Intent,["operation"]="copy_owned_checkpoint",["bridgeVersion"]="0.33.5-dev",["preparedUtc"]=LeaseTime.Format(Utc),["expiresUtc"]=LeaseTime.Format(Utc.AddSeconds(300)),["cadExecutionEnabled"]=false,["originalOutcomeResolved"]=false,
            ["origin"]=new JsonObject{["operationId"]=Origin,["operationPlanSha256"]=originSha,["planFolder"]="copyopenplans",["copyId"]=Copy,["ownershipSha256"]=ownerSha},["checkpoint"]=checkpoint};
        PlanSha=ArtifactProof.Freeze(Files.Root,"checkpointrecoveries/"+Intent+".json",Plan);
        Prepared=Base("checkpoint_recovery_prepared_files");Prepared["bridgeRequestId"]=Intent;Prepared["recoveryId"]=Intent;Prepared["recoverySha256"]=PlanSha;Prepared["recovery"]=Plan.DeepClone();Prepared["originalArtifactsUnchanged"]=true;
    }
    internal static JsonObject Record(string path,string field,string hash,int length,int identity)=>new(){["path"]=path,[field]=hash,["length"]=length,["fileIdentity"]=new JsonObject{["volumeSerial"]="12345678",["fileIndex"]=identity.ToString("x16"),["creationTime"]="1234567812345678"}};
    internal JsonObject Base(string state)=>new(){["state"]=state,["bridgeVersion"]="0.33.5-dev",["timestampUtc"]=LeaseTime.Format(Utc),["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false,["powerShapeConnectionAttempted"]=false,["originalOutcomeResolved"]=false,["retryAllowed"]=false,["qualification"]="Synthetic disk recovery fixture"};
    internal string Issue()=>Session.CheckpointRecoveries.Issue(Prepared,Parameters)["checkpointRecoveryHandle"]!.GetValue<string>();
    internal JsonObject Materialize(string id,bool publish=true)
    {
        var original=TransportCancellationProof.Snapshot(Files.Root).Where(pair=>!pair.Key.Contains(Path.DirectorySeparatorChar+"requests"+Path.DirectorySeparatorChar)).ToDictionary(pair=>pair.Key,pair=>(JsonNode?)JsonValue.Create(pair.Value.ToLowerInvariant()));
        var source=Plan["checkpoint"]!.AsObject();var hash=source["sha256"]!.GetValue<string>();var bytes=File.ReadAllBytes(source["path"]!.GetValue<string>());
        var work=FileArtifacts.FixedPath(Files.Root,"workingcopies",id,"Working_"+id+".psmodel");var cp=FileArtifacts.FixedPath(Files.Root,"workingcopies",id,"checkpoint","Before.psmodel");
        foreach(var path in new[]{work,cp})Files.Write(Path.GetRelativePath(Files.Root,path),System.Text.Encoding.UTF8.GetString(bytes));File.SetAttributes(cp,FileAttributes.ReadOnly);
        var owner=new JsonObject{["schemaVersion"]=2,["copyId"]=id,["bridgeVersion"]="0.33.5-dev",["createdUtc"]=LeaseTime.Format(Utc),["scope"]="saved_disk_bytes_only",["executionEnabled"]=false,["source"]=source.DeepClone(),["working"]=Record(work,"initialSha256",hash,bytes.Length,3),["checkpoint"]=Record(cp,"sha256",hash,bytes.Length,4)};
        var ownerSha=ArtifactProof.Freeze(Files.Root,"workingcopies/"+id+"/ownership.json",owner);
        var creation=new JsonObject{["schemaVersion"]=1,["operationId"]=id,["operation"]="materialize_owned_checkpoint",["recoveryId"]=Intent,["recoverySha256"]=PlanSha,["origin"]=Plan["origin"]!.DeepClone(),["checkpoint"]=source.DeepClone(),["cadExecutionEnabled"]=false,["originalOutcomeResolved"]=false,["createdUtc"]=LeaseTime.Format(Utc)};
        var creationSha=ArtifactProof.Freeze(Files.Root,"checkpointcopyplans/"+id+".json",creation);
        ArtifactProof.Freeze(Files.Root,"checkpointrecoveries/"+Intent+".copy-once.json",new(){["operationId"]=id,["recoveryId"]=Intent,["planSha256"]=creationSha,["automaticReplayForbidden"]=true});
        var result=Base("checkpoint_working_copy_prepared_disk");result["copyId"]=id;result["ownershipSha256"]=ownerSha;result["ownership"]=owner;result["recoveryId"]=Intent;result["recoverySha256"]=PlanSha;result["creationPlan"]=creation;result["creationPlanSha256"]=creationSha;result["originalArtifactsUnchanged"]=true;result["originalArtifactHashes"]=new JsonObject(original);
        result["materializationProofSha256"]=ArtifactProof.Freeze(Files.Root,"checkpointcopyplans/"+id+".result.json",result);
        if(publish)Files.Write("responses/"+id+".json",result.ToJsonString());result["bridgeRequestId"]=id;return result;
    }
}
