using System.Text.Json.Nodes;
using PowerShapeMcp;

namespace PowerShapeMcp.Tests;

internal sealed class OwnedRotationFixture
{
    internal readonly SafetyFixture Data=new();internal readonly RotationPlanStore Plans;internal readonly OwnedRotationStore Store;
    internal readonly string Plan;internal readonly JsonObject Parameters;
    internal OwnedRotationFixture()
    {
        Plans=new(Data.Root,Data.Candidates,()=>Data.Time,()=>Data.Utc);Store=new(Data.Root,Plans,Data.Bindings,()=>Data.Time,()=>Data.Utc);
        Plan=Plans.Prepare(Data.Sets[0],RotationPlanTests.Parameters())["rotationPlanHandle"]!.GetValue<string>();Parameters=Store.PrepareParameters(Plan,Data.Binding);
    }
    internal JsonObject Native(string id)
    {
        var packet=Parameters.DeepClone().AsObject();var binding=Data.BindingReceipt["binding"]!.AsObject();
        packet["schemaVersion"]=1;packet["ownedRotationId"]=id;packet["preparedUtc"]=LeaseTime.Format(Data.Utc);packet["expiresUtc"]=binding["expiresUtc"]!.DeepClone();packet["executionEnabled"]=false;
        packet["nativeObservedPropertiesSha256"]=binding["observedPropertiesSha256"]!.DeepClone();packet["windowContextSha256"]=binding["windowContextSha256"]!.DeepClone();packet["workingSha256"]=new string('a',64);packet["copyId"]=binding["copyId"]!.DeepClone();packet["ownershipSha256"]=binding["ownershipSha256"]!.DeepClone();
        var sha=ArtifactProof.Freeze(Data.Root,"ownedrotations/"+id+".json",packet);PreparedJournal.Create(Data.Root,id,sha,Data.Utc,new(){["executionEnabled"]=false,["geometryMutationAttempted"]=false});
        return new(){["state"]="owned_rotation_prepared_observed",["bridgeRequestId"]=id,["ownedRotationId"]=id,["ownedRotationSha256"]=sha,["ownedRotation"]=packet,["expiresUtc"]=packet["expiresUtc"]!.DeepClone(),["executionEnabled"]=false,["geometryMutationAttempted"]=false,["contextChangeAttempted"]=false,["cadSaveAttempted"]=false};
    }
}
